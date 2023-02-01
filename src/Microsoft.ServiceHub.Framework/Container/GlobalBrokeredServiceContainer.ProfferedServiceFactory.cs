// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Nerdbank.Streams;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Services a brokered service that is proffered via an in-proc factory.
	/// </summary>
	[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
	protected class ProfferedServiceFactory : IProffered
	{
		private static readonly ProtectedOperation ClientIsOwnerProtectedOperation = WellKnownProtectedOperations.CreateClientIsOwner();

		internal ProfferedServiceFactory(GlobalBrokeredServiceContainer container, ServiceRpcDescriptor descriptor, BrokeredServiceFactory factory)
		{
			this.Container = container ?? throw new ArgumentNullException(nameof(container));
			this.Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
			this.Factory = factory ?? throw new ArgumentNullException(nameof(factory));
			this.Monikers = ImmutableHashSet.Create(this.Descriptor.Moniker);
			this.RemoteServiceBrokerWrapper = new RemoteServiceBrokerWrapper(this);
		}

		internal ProfferedServiceFactory(GlobalBrokeredServiceContainer container, ServiceRpcDescriptor descriptor, AuthorizingBrokeredServiceFactory factory)
		{
			this.Container = container ?? throw new ArgumentNullException(nameof(container));
			this.Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
			this.AuthorizingFactory = factory ?? throw new ArgumentNullException(nameof(factory));
			this.Monikers = ImmutableHashSet.Create(this.Descriptor.Moniker);
			this.RemoteServiceBrokerWrapper = new RemoteServiceBrokerWrapper(this);
		}

		/// <summary>
		/// We never raise this event, so just drop the handlers on the floor.
		/// </summary>
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		/// <inheritdoc/>
		public ServiceSource Source => ServiceSource.SameProcess; // individual service factories are *always* local to this process.

		/// <inheritdoc/>
		public ImmutableHashSet<ServiceMoniker> Monikers { get; }

		/// <summary>
		/// Gets the descriptor that was provided with the factory.
		/// </summary>
		protected internal ServiceRpcDescriptor Descriptor { get; }

		/// <summary>
		/// Gets the factory, if one was provided that did not take an <see cref="AuthorizationServiceClient"/>.
		/// </summary>
		protected BrokeredServiceFactory? Factory { get; }

		/// <summary>
		/// Gets the factory, if one was provided that takes an <see cref="AuthorizationServiceClient"/>.
		/// </summary>
		protected AuthorizingBrokeredServiceFactory? AuthorizingFactory { get; }

		/// <summary>
		/// Gets the container.
		/// </summary>
		protected GlobalBrokeredServiceContainer Container { get; }

		private protected RemoteServiceBrokerWrapper RemoteServiceBrokerWrapper { get; }

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string DebuggerDisplay => $"{this.Descriptor.Moniker}";

		/// <inheritdoc/>
		public void Dispose()
		{
			this.Container.RemoveRegistrations(this);
		}

		/// <inheritdoc/>
		public virtual async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			(IDuplexPipe, IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();

			ServiceRpcDescriptor descriptor = this.Descriptor is ServiceJsonRpcDescriptor serviceJsonRpcDescriptor
				 ? serviceJsonRpcDescriptor.MultiplexingStreamOptions is object ? this.Descriptor :

					// We encourage users to migrate to descriptors configured with ServiceJsonRpcDescriptor.WithMultiplexingStream(MultiplexingStream.Options).
#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
					this.Descriptor.WithMultiplexingStream(options.MultiplexingStream)
#pragma warning restore CS0618 // Type or member is obsolete
				 : this.Descriptor;

			IServiceBroker serviceBroker = this.Container.GetSecureServiceBroker(options);
			descriptor = descriptor
				.WithTraceSource(await this.Container.GetTraceSourceForConnectionAsync(serviceBroker, serviceMoniker, options, clientRole: false, cancellationToken).ConfigureAwait(false));

			using (options.ApplyCultureToCurrentContext())
			{
				ServiceRpcDescriptor.RpcConnection connection = descriptor.ConstructRpcConnection(pipePair.Item1);

				// If the service needs to be able to call back to the client, arrange for it.
				// If the client is remote, we need to create an RPC proxy back to the client.
				// If the client is local, it should provide itself as the RPC target. So we only provide one if the client hasn't already done so.
				// FWIW: it would be pretty odd if (ClientRpcTarget != null) since they're asking for a pipe so presumably they are remote.
				if (descriptor.ClientInterface != null && options.ClientRpcTarget == null)
				{
					options.ClientRpcTarget = connection.ConstructRpcClient(descriptor.ClientInterface);
				}

				object? server = await this.InvokeFactoryAsync(serviceBroker, descriptor.Moniker, options, cancellationToken).ConfigureAwait(false);
				try
				{
					if (server is object)
					{
						connection.AddLocalRpcTarget(server);
						connection.StartListening();
						return pipePair.Item2;
					}
					else
					{
						connection.Dispose();
						return null;
					}
				}
				catch
				{
					(server as IDisposable)?.Dispose();
					throw;
				}
			}
		}

		/// <inheritdoc/>
		public virtual async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			cancellationToken.ThrowIfCancellationRequested();

			IServiceBroker serviceBroker = this.Container.GetSecureServiceBroker(options);
			serviceDescriptor = serviceDescriptor
				.WithTraceSource(await this.Container.GetTraceSourceForConnectionAsync(serviceBroker, serviceDescriptor.Moniker, options, clientRole: false, cancellationToken).ConfigureAwait(false));

			object? liveObject = await this.InvokeFactoryAsync(serviceBroker, serviceDescriptor.Moniker, options, cancellationToken).ConfigureAwait(false);
			try
			{
				return serviceDescriptor.ConstructLocalProxy((T?)liveObject);
			}
			catch
			{
				(liveObject as IDisposable)?.Dispose();
				throw;
			}
		}

		/// <inheritdoc />
		Task IRemoteServiceBroker.HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			return this.RemoteServiceBrokerWrapper.HandshakeAsync(clientMetadata, cancellationToken);
		}

		/// <inheritdoc />
		Task<RemoteServiceConnectionInfo> IRemoteServiceBroker.RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken)
		{
			return this.RemoteServiceBrokerWrapper.RequestServiceChannelAsync(serviceMoniker, serviceActivationOptions, cancellationToken);
		}

		/// <inheritdoc />
		Task IRemoteServiceBroker.CancelServiceRequestAsync(Guid serviceRequestId)
		{
			return this.RemoteServiceBrokerWrapper.CancelServiceRequestAsync(serviceRequestId);
		}

		private async ValueTask<object?> InvokeFactoryAsync(IServiceBroker serviceBroker, ServiceMoniker moniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			bool allowsGuests = false;
			if (this.Container.TryLookupServiceRegistration(moniker, out ServiceRegistration? serviceRegistration, out _))
			{
				allowsGuests = serviceRegistration.AllowGuestClients;
			}

			IAuthorizationService? authorizationService = null;

			try
			{
				if (!allowsGuests)
				{
					// To prevent infinite recursion, only try to acquire the Authorization service if allowsGuests is false
					authorizationService = await serviceBroker.GetProxyAsync<IAuthorizationService>(FrameworkServices.Authorization, cancellationToken).ConfigureAwait(false);
					Assumes.Present(authorizationService);
					if (!await authorizationService.CheckAuthorizationAsync(ClientIsOwnerProtectedOperation, cancellationToken).ConfigureAwait(false))
					{
						return null;
					}
				}

				if (this.Factory != null)
				{
					return await this.Factory(moniker, options, serviceBroker, cancellationToken).ConfigureAwait(false);
				}
				else if (this.AuthorizingFactory != null)
				{
					AuthorizationServiceClient? authClient = null;
					try
					{
						if (authorizationService is null)
						{
							authorizationService = await serviceBroker.GetProxyAsync<IAuthorizationService>(FrameworkServices.Authorization, cancellationToken).ConfigureAwait(false);
							Assumes.Present(authorizationService);
						}

						authClient = new AuthorizationServiceClient(authorizationService);
						authorizationService = null; // we no longer own this instance, so don't dispose it later.

						object? result = await this.AuthorizingFactory(moniker, options, serviceBroker, authClient, cancellationToken).ConfigureAwait(false);
						if (result is null)
						{
							authClient.Dispose();
						}

						return result;
					}
					catch
					{
						authClient?.Dispose();
						throw;
					}
				}
				else
				{
					throw Assumes.NotReachable();
				}
			}
			finally
			{
				(authorizationService as IDisposable)?.Dispose();
			}
		}
	}
}
