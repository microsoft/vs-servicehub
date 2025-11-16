// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Composition;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.ServiceBroker;
using Nerdbank.Streams;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Implements the <see cref="IServiceBroker"/> to be proffered into the <see cref="GlobalBrokeredServiceContainer"/>
/// in order to effectively proffer all the MEF-activated brokered services in the IDE.
/// </summary>
/// <remarks>
/// A host IDE should derive from this class and apply <see cref="ExportAttribute"/> to the derived type.
/// At startup, the IDE should acquire this export and call <see cref="RegisterAndProfferServicesAsync(System.Threading.CancellationToken)"/>
/// to add MEF exported brokered services to the container.
/// </remarks>
public abstract class ServiceBrokerOfExportedServices : IServiceBroker
{
	private static readonly ProtectedOperation ClientIsOwnerProtectedOperation = WellKnownProtectedOperations.CreateClientIsOwner();

	/// <summary>
	/// The registration data for all MEF brokered services.
	/// </summary>
	private ImmutableDictionary<ServiceMoniker, ServiceRegistration> serviceRegistration = ImmutableDictionary<ServiceMoniker, ServiceRegistration>.Empty;

	/// <summary>
	/// The monikers to all MEF brokered services.
	/// </summary>
	private ImmutableHashSet<ServiceMoniker> serviceMonikers = ImmutableHashSet<ServiceMoniker>.Empty;

	/// <summary>
	/// We never raise this event, so just drop the handlers on the floor.
	/// </summary>
	event EventHandler<BrokeredServicesChangedEventArgs>? IServiceBroker.AvailabilityChanged
	{
		add { }
		remove { }
	}

	/// <summary>
	/// Gets or sets the sharing boundary factory used to activate each MEF brokered service.
	/// </summary>
	[Import]
	[SharingBoundary(ServiceBrokerForExportedBrokeredServices.ExportedBrokeredServiceSharingBoundary)]
	private ExportFactory<ServiceBrokerForExportedBrokeredServices> ServiceBrokerFactory { get; set; } = null!;

	/// <inheritdoc cref="RegisterAndProfferServices(GlobalBrokeredServiceContainer)"/>
	public async Task RegisterAndProfferServicesAsync(CancellationToken cancellationToken)
	{
		GlobalBrokeredServiceContainer container = await this.GetBrokeredServiceContainerAsync(cancellationToken).ConfigureAwait(false);
		this.RegisterAndProfferServices(container);
	}

	/// <summary>
	/// Registers MEF exported brokered services and proffers a factory for them.
	/// </summary>
	/// <param name="container">The container to register and proffer the services with.</param>
	public void RegisterAndProfferServices(GlobalBrokeredServiceContainer container)
	{
		Requires.NotNull(container);

		this.Initialize();

		container.RegisterServices(this.serviceRegistration);
		container.Proffer(this, this.serviceMonikers);
	}

	async ValueTask<IDuplexPipe?> IServiceBroker.GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			ServiceRpcDescriptor.RpcConnection? connection = null;
			GlobalBrokeredServiceContainer container = await this.GetBrokeredServiceContainerAsync(cancellationToken).ConfigureAwait(false);
			IServiceBroker contextualServiceBroker = container.GetSecureServiceBroker(options);

			Export<ServiceBrokerForExportedBrokeredServices>? export = await this.ActivateBrokeredServiceAsync(serviceMoniker, contextualServiceBroker, options, cancellationToken).ConfigureAwait(false);
			if (export is null)
			{
				return null;
			}

			IExportedBrokeredService? brokeredService = null;

			try
			{
				brokeredService = export.Value.CreateBrokeredService(cancellationToken);
				if (brokeredService is null)
				{
					export.Dispose();
					return null;
				}

				ServiceRpcDescriptor? descriptor = brokeredService.Descriptor;
				if (descriptor is null)
				{
					// This is the service's way of refusing to be activated.
					// This *could* be because the service is exported with a null version,
					// which matches on any client version, yet the client requested a version
					// that the service doesn't support.
					export.Dispose();
					return null;
				}

				descriptor = await container.ApplyDescriptorSettingsInternalAsync(descriptor, contextualServiceBroker, options, clientRole: false, cancellationToken).ConfigureAwait(false);

				(IDuplexPipe, IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();

				using (options.ApplyCultureToCurrentContext())
				{
					if (descriptor is ServiceJsonRpcDescriptor { MultiplexingStreamOptions: null } oldJsonRpcDescriptor)
					{
						// We encourage users to migrate to descriptors configured with ServiceJsonRpcDescriptor.WithMultiplexingStream(MultiplexingStream.Options).
#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
						descriptor = oldJsonRpcDescriptor.WithMultiplexingStream(options.MultiplexingStream);
#pragma warning restore CS0618 // Type or member is obsolete
					}

					connection = descriptor.ConstructRpcConnection(pipePair.Item1);

					// If the service needs to be able to call back to the client, arrange for it.
					// If the client is remote, we need to create an RPC proxy back to the client.
					// If the client is local, it should provide itself as the RPC target. So we only provide one if the client hasn't already done so.
					// FWIW: it would be pretty odd if (ClientRpcTarget != null) since they're asking for a pipe so presumably they are remote.
					if (descriptor.ClientInterface is not null && options.ClientRpcTarget is null)
					{
						options.ClientRpcTarget = connection.ConstructRpcClient(descriptor.ClientInterface);

						// Given we're constructing with MEF, and the brokered service has already been constructed (and imported the ServiceActivationOptions),
						// we have no choice but to tear that instance down and rebuild with the modified ServiceActivationOptions at this point.
						// We could only avoid this if we had had access to the descriptor before constructing the brokered service.
						(brokeredService as IDisposable)?.Dispose();
						export.Dispose();

						export = await this.ActivateBrokeredServiceAsync(serviceMoniker, contextualServiceBroker, options, cancellationToken).ConfigureAwait(false);
						Assumes.NotNull(export);
						brokeredService = export.Value.CreateBrokeredService(cancellationToken);
						Assumes.NotNull(brokeredService);
					}

					await brokeredService.InitializeAsync(cancellationToken).ConfigureAwait(false);

					// Arrange for proxy disposal to also dispose of our export to avoid a MEF leak.
					connection.Completion.ContinueWith((_, s) => ((IDisposable)s!).Dispose(), export, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Forget();

					connection.AddClientLocalRpcTarget(brokeredService);
					connection.StartListening();
					return pipePair.Item2;
				}
			}
			catch
			{
				(brokeredService as IDisposable)?.Dispose();
				export?.Dispose();
				connection?.Dispose();
				throw;
			}
		}
		catch (Exception ex)
		{
			throw new ServiceActivationFailedException(serviceMoniker, ex);
		}
	}

	async ValueTask<T?> IServiceBroker.GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
		where T : class
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			GlobalBrokeredServiceContainer container = await this.GetBrokeredServiceContainerAsync(cancellationToken).ConfigureAwait(false);
			IServiceBroker contextualServiceBroker = container.GetSecureServiceBroker(options);

			Export<ServiceBrokerForExportedBrokeredServices>? export = await this.ActivateBrokeredServiceAsync(serviceDescriptor.Moniker, contextualServiceBroker, options, cancellationToken).ConfigureAwait(false);
			if (export is null)
			{
				return null;
			}

			IExportedBrokeredService? brokeredService = null;

			try
			{
				serviceDescriptor = await container.ApplyDescriptorSettingsInternalAsync(serviceDescriptor, contextualServiceBroker, options, clientRole: false, cancellationToken).ConfigureAwait(false);

				brokeredService = export.Value.CreateBrokeredService(cancellationToken);
				if (brokeredService is null || brokeredService.Descriptor is null)
				{
					export.Dispose();
					return null;
				}

				await brokeredService.InitializeAsync(cancellationToken).ConfigureAwait(false);
				T proxy = serviceDescriptor.ConstructLocalProxy((T)brokeredService);

				// Arrange for proxy disposal to also dispose of our export to avoid a MEF leak.
				// It's critical that the client disposing of the proxy lead to the disposal of the MEF Export<T>
				// or else MEF will never release a strong reference it holds on the Export<T> as a non-shared part.
				// But disposing the Export<T> also leads to MEF disposing the exported brokered service, which disposing of the proxy also did.
				// This means the exported brokered service instance will be disposed of twice.
				// This isn't great, but it's permissible (https://docs.microsoft.com/en-us/dotnet/api/system.idisposable.dispose?redirectedfrom=MSDN&view=net-6.0#remarks)
				// "If an object's Dispose method is called more than once, the object must ignore all calls after the first one."
				// If we ever have to avoid disposing twice, we could use dynamic code generation to wrap the proxy we have
				// (which is itself a dynamic code gen class) in another proxy that redirects the Dispose call but forwards all others.
				((INotifyDisposable)proxy).Disposed += (s, e) => export.Dispose();

				return proxy;
			}
			catch
			{
				(brokeredService as IDisposable)?.Dispose();
				export.Dispose();
				throw;
			}
		}
		catch (Exception ex)
		{
			throw new ServiceActivationFailedException(serviceDescriptor.Moniker, ex);
		}
	}

	/// <summary>
	/// Gets the global brokered service container.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The global brokered service container.</returns>
	protected abstract Task<GlobalBrokeredServiceContainer> GetBrokeredServiceContainerAsync(CancellationToken cancellationToken);

	private static ServiceMoniker CreateServiceMoniker(IBrokeredServicesExportMetadata metadata, int index)
	{
		return new ServiceMoniker(metadata.ServiceName[index], metadata.ServiceVersion[index] is string v ? new Version(v) : null);
	}

	/// <summary>
	/// Creates an instance of a MEF sharing boundary within which a brokered service with the specified moniker will be activated.
	/// </summary>
	/// <param name="serviceMoniker">The moniker of the required service.</param>
	/// <param name="contextualServiceBroker">The service broker that is created specifically for this brokered service.</param>
	/// <param name="serviceActivationOptions">The activation options to use with this service.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The MEF export representing this sharing boundary.</returns>
	private async ValueTask<Export<ServiceBrokerForExportedBrokeredServices>?> ActivateBrokeredServiceAsync(ServiceMoniker serviceMoniker, IServiceBroker contextualServiceBroker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken)
	{
		Export<ServiceBrokerForExportedBrokeredServices> sb = this.ServiceBrokerFactory.CreateExport();
		IAuthorizationService? authorizationService = null;

		try
		{
			sb.Value.InnerServiceBroker = contextualServiceBroker;
			sb.Value.ServiceActivationOptions = serviceActivationOptions;
			sb.Value.ActivatedMoniker = serviceMoniker;

			if (!this.serviceRegistration.TryGetValue(serviceMoniker, out ServiceRegistration? serviceRegistration))
			{
				if (!this.serviceRegistration.TryGetValue(new ServiceMoniker(serviceMoniker.Name, null), out serviceRegistration))
				{
					sb.Dispose();
					return null;
				}
			}

			authorizationService = await contextualServiceBroker.GetProxyAsync<IAuthorizationService>(FrameworkServices.Authorization, cancellationToken).ConfigureAwait(false);
			Assumes.Present(authorizationService);
			if (!serviceRegistration.AllowGuestClients && !await authorizationService.CheckAuthorizationAsync(ClientIsOwnerProtectedOperation, cancellationToken).ConfigureAwait(false))
			{
				sb.Dispose();
				return null;
			}

			sb.Value.AuthorizationServiceClient = new AuthorizationServiceClient(authorizationService);
			authorizationService = null; // we no longer own this instance, so don't dispose it later.

			return sb;
		}
		catch
		{
			sb.Dispose();
			throw;
		}
		finally
		{
			(authorizationService as IDisposable)?.Dispose();
		}
	}

	/// <summary>
	/// Initializes internal data structures after MEF has set importing properties.
	/// </summary>
	private void Initialize()
	{
		// Only run once.
		if (this.serviceMonikers.Count > 0)
		{
			return;
		}

		var monikers = this.serviceMonikers.ToBuilder();
		var registrations = this.serviceRegistration.ToBuilder();

		// Create one sharing boundary temporarily so we can inspect the metadata on the exports within that boundary.
		using Export<ServiceBrokerForExportedBrokeredServices> lowerExport = this.ServiceBrokerFactory.CreateExport();

		foreach (IBrokeredServicesExportMetadata exportMetadata in lowerExport.Value.ExportedServiceMetadata)
		{
			for (int i = 0; i < exportMetadata.ServiceName.Length; i++)
			{
				ServiceMoniker moniker = CreateServiceMoniker(exportMetadata, i);

				// When a brokered service MEF part exports itself more than once with distinct monikers, each moniker appears as its own export,
				// but each export also contains metadata for every moniker on that export.
				// To keep the MEF part simple, we accommodate this by just swallowing the duplicates.
				// This means that if two distinct MEF parts were both exporting the same moniker we would quietly pick one in a non-deterministic way
				// rather than detect and report it. :(
				if (monikers.Add(moniker))
				{
					ServiceRegistration registration = new(exportMetadata.Audience[i], null, exportMetadata.AllowTransitiveGuestClients[i])
					{
						AdditionalServiceInterfaceTypeNames = exportMetadata.OptionalInterfacesImplemented?[i] is string?[] ifaces ? ifaces.ToImmutableArray() : ImmutableArray<string>.Empty,
					};
					registrations.Add(moniker, registration);
				}
			}
		}

		this.serviceMonikers = monikers.ToImmutable();
		this.serviceRegistration = registrations.ToImmutable();
	}
}
