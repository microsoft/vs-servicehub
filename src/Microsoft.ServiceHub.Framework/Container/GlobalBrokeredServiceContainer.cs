// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

/// <summary>
/// A container of brokered services that supports multiple service sources and multiple consumer roles that get filtered <see cref="IServiceBroker"/> views into the available services.
/// </summary>
/// <remarks>
/// <para>When a service is registered without a version, it doubles as a fallback service when a request for that service name is made but no exact version match can be found.</para>
/// </remarks>
[RequiresUnreferencedCode(Reasons.TypeLoad)]
public abstract partial class GlobalBrokeredServiceContainer : IBrokeredServiceContainer, IBrokeredServiceContainerInternal, IBrokeredServiceContainerDiagnostics
{
	/// <summary>
	/// Defines the order of sources to check for remote services.
	/// </summary>
	private static readonly ServiceSource[] PreferredSourceOrderForRemoteServices = new ServiceSource[] { ServiceSource.TrustedExclusiveClient, ServiceSource.TrustedExclusiveServer, ServiceSource.TrustedServer, ServiceSource.UntrustedServer };

	/// <summary>
	/// Defines the order of sources to check for locally proffered services.
	/// </summary>
	private static readonly ServiceSource[] PreferredSourceOrderForLocalServices = new ServiceSource[] { ServiceSource.SameProcess, ServiceSource.OtherProcessOnSameMachine };

	private readonly object syncObject = new object();

	private readonly HashSet<object> loadedPackageIds = new HashSet<object>();

	private readonly TraceSource traceSource;

	private readonly JoinableTaskFactory? joinableTaskFactory;

	/// <summary>
	/// A dictionary of registered services, keyed by their monikers.
	/// </summary>
	private ImmutableDictionary<ServiceMoniker, ServiceRegistration> registeredServices;

	/// <summary>
	/// A value indicating whether this process is dedicated as a client of a Codespace.
	/// </summary>
	/// <remarks>
	/// If we're running in a Codespace client, block all local services that may be obtained from the Codespace server.
	/// </remarks>
	private bool isClientOfExclusiveServer;

	/// <summary>
	/// A dictionary for looking up a proffered service by a source and moniker.
	/// </summary>
	private ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>> profferedServiceIndex = ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>>.Empty;

	/// <summary>
	/// The remote sources from which we can expect services and the proffering sources for them.
	/// </summary>
	private ImmutableDictionary<ServiceSource, IProffered> remoteSources = ImmutableDictionary<ServiceSource, IProffered>.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="GlobalBrokeredServiceContainer"/> class.
	/// </summary>
	/// <param name="services">
	/// A map of service monikers to their registration details.
	/// Only registered services will be obtainable from the <see cref="IServiceBroker"/> returned from methods on this class.
	/// </param>
	/// <param name="isClientOfExclusiveServer"><see langword="true"/> when this process is or will be connected to a dedicated, trusted server (e.g. a Codespace) that will provide the environment to this client; <see langword="false"/> otherwise.</param>
	/// <param name="joinableTaskFactory">An optional <see cref="JoinableTaskFactory"/> to use when scheduling async work, to avoid deadlocks in an application with a main thread.</param>
	/// <param name="traceSource">A means of logging.</param>
	protected GlobalBrokeredServiceContainer(ImmutableDictionary<ServiceMoniker, ServiceRegistration> services, bool isClientOfExclusiveServer, JoinableTaskFactory? joinableTaskFactory, TraceSource traceSource)
	{
		this.registeredServices = services;
		this.isClientOfExclusiveServer = isClientOfExclusiveServer;
		this.joinableTaskFactory = joinableTaskFactory;
		this.traceSource = traceSource;

		// Add built-in services.
		this.ProfferIntrinsicService(
			FrameworkServices.RemoteBrokeredServiceManifest,
			new ServiceRegistration((ServiceAudience.RemoteExclusiveServer | ServiceAudience.AllClientsIncludingGuests) & ~ServiceAudience.Local, null, allowGuestClients: true),
			(view, mk, options, sb, ct) => new ValueTask<object?>(new BrokeredServiceManifest(this, view.Audience)));
		this.ProfferIntrinsicService(
			MissingServiceDiagnostics,
			new ServiceRegistration(ServiceAudience.Local, null, allowGuestClients: false),
			(view, mk, options, sb, ct) => new ValueTask<object?>(new MissingServiceDiagnosticsService(view)));
	}

	private event EventHandler<(ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>>?, ImmutableHashSet<ServiceMoniker>)>? AvailabilityChanged;

	/// <summary>
	/// Gets a descriptor for the service that can diagnose the cause of a missing brokered service.
	/// Use <see cref="IMissingServiceDiagnosticsService"/> to interact with this service.
	/// </summary>
	public static ServiceRpcDescriptor MissingServiceDiagnostics { get; } = new ServiceJsonRpcPolyTypeDescriptor(
		new ServiceMoniker("Microsoft.VisualStudio.GlobalBrokeredServiceContainer.MissingServiceDiagnostics", new Version(1, 0)),
		PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework.Default.IMissingServiceDiagnosticsService,
		ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
		ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);

	/// <inheritdoc />
	public abstract IReadOnlyDictionary<string, string> LocalUserCredentials { get; }

	/// <summary>
	/// Gets the services currently registered.
	/// </summary>
	protected ImmutableDictionary<ServiceMoniker, ServiceRegistration> RegisteredServices => this.registeredServices;

	/// <inheritdoc />
	public IDisposable Proffer(ServiceRpcDescriptor serviceDescriptor, BrokeredServiceFactory factory) => this.Proffer(new ProfferedServiceFactory(this, serviceDescriptor, factory));

	/// <inheritdoc />
	public IDisposable Proffer(ServiceRpcDescriptor serviceDescriptor, AuthorizingBrokeredServiceFactory factory) => this.Proffer(new ProfferedServiceFactory(this, serviceDescriptor, factory));

	/// <summary>
	/// Proffers services from another <see cref="IServiceBroker"/> into this container.
	/// </summary>
	/// <param name="serviceBroker">A service broker offering local services.</param>
	/// <param name="serviceMonikers">The monikers to services that should be obtained from this <paramref name="serviceBroker"/>.</param>
	/// <returns>A value that can be disposed to remove this <paramref name="serviceBroker"/> from the container.</returns>
	public IDisposable Proffer(IServiceBroker serviceBroker, IReadOnlyCollection<ServiceMoniker> serviceMonikers) => this.Proffer(new ProfferedServiceBroker(this, serviceBroker, ServiceSource.SameProcess, serviceMonikers));

#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters

	/// <summary>
	/// Proffers services offered by a remote <see cref="IServiceBroker"/> for access by this container.
	/// </summary>
	/// <param name="serviceBroker">The service broker for remote services.</param>
	/// <param name="source">Where the remote services that are being proffered come from.</param>
	/// <param name="serviceMonikers">
	/// The set of service monikers that may be requested of this service broker. May be null for truly remote brokers that we don't know the full set of services for.
	/// Only services registered with this container will ever be requested from this <paramref name="serviceBroker"/>.
	/// </param>
	/// <returns>A value that can be disposed to remove this <paramref name="serviceBroker"/> from the container.</returns>
	public IDisposable ProfferRemoteBroker(IServiceBroker serviceBroker, ServiceSource source, ImmutableHashSet<ServiceMoniker>? serviceMonikers = null)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		Requires.Argument(source != ServiceSource.SameProcess, nameof(source), "Use the public {0} method to proffer services from the same process.", nameof(IBrokeredServiceContainer.Proffer));

		this.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Proffered, "IServiceBroker proffered from remote source: {0}.", source);
		return this.Proffer(new ProfferedServiceBroker(this, serviceBroker, source, this.GetAllowedMonikers(source, serviceMonikers)));
	}

	/// <summary>
	/// Proffers services offered by a remote <see cref="IRemoteServiceBroker"/> for access by this container.
	/// </summary>
	/// <inheritdoc cref="ProfferRemoteBroker(IServiceBroker, ServiceSource, ImmutableHashSet{ServiceMoniker}?)"/>
	public IDisposable ProfferRemoteBroker(IRemoteServiceBroker serviceBroker, ServiceSource source, ImmutableHashSet<ServiceMoniker>? serviceMonikers = null)
		=> this.ProfferRemoteBroker(serviceBroker, null, source, serviceMonikers);

	/// <summary>
	/// Proffers services offered by a remote <see cref="IRemoteServiceBroker"/> for access by this container.
	/// </summary>
	/// <inheritdoc cref="ProfferRemoteBroker(IServiceBroker, ServiceSource, ImmutableHashSet{ServiceMoniker}?)" path="/returns"/>
	/// <inheritdoc cref="ProfferRemoteBroker(IServiceBroker, ServiceSource, ImmutableHashSet{ServiceMoniker}?)" path="/remarks"/>
	/// <param name="serviceBroker"><inheritdoc cref="ProfferRemoteBroker(IServiceBroker, ServiceSource, ImmutableHashSet{ServiceMoniker}?)" path="/param[@name='serviceBroker]"/></param>
	/// <param name="multiplexingStream">An optional <see cref="MultiplexingStream"/> that may be used to provision pipes for each brokered service.</param>
	/// <param name="source"><inheritdoc cref="ProfferRemoteBroker(IServiceBroker, ServiceSource, ImmutableHashSet{ServiceMoniker}?)" path="/param[@name='source]"/></param>
	/// <param name="serviceMonikers"><inheritdoc cref="ProfferRemoteBroker(IServiceBroker, ServiceSource, ImmutableHashSet{ServiceMoniker}?)" path="/param[@name='serviceMonikers]"/></param>
	public IDisposable ProfferRemoteBroker(IRemoteServiceBroker serviceBroker, MultiplexingStream? multiplexingStream, ServiceSource source, ImmutableHashSet<ServiceMoniker>? serviceMonikers = null)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		Requires.Argument(source != ServiceSource.SameProcess, nameof(source), "Use the public {0} method to proffer services from the same process.", nameof(IBrokeredServiceContainer.Proffer));

		this.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Proffered, "IRemoteServiceBroker proffered from remote source: {0}.", source);
		return this.Proffer(new ProfferedRemoteServiceBroker(this, serviceBroker, multiplexingStream, source, this.GetAllowedMonikers(source, serviceMonikers)));
	}

#pragma warning restore RS0026 // Do not add multiple public overloads with optional parameters

	/// <inheritdoc />
	public IServiceBroker GetFullAccessServiceBroker()
	{
		return new View(this, ServiceAudience.Process, this.LocalUserCredentials, ClientCredentialsPolicy.RequestOverridesDefault);
	}

	/// <inheritdoc />
	public IServiceBroker GetLimitedAccessServiceBroker(ServiceAudience audience, IReadOnlyDictionary<string, string> clientCredentials, ClientCredentialsPolicy credentialPolicy)
	{
		return new View(this, audience, clientCredentials, credentialPolicy);
	}

	/// <inheritdoc cref="GetLimitedAccessServiceBroker(ServiceAudience, IReadOnlyDictionary{string, string}, ClientCredentialsPolicy)" />
	/// <returns>A <see cref="IRemoteServiceBroker"/> for use in sharing directly on a remote connection.</returns>
	public IRemoteServiceBroker GetLimitedAccessRemoteServiceBroker(ServiceAudience audience, IReadOnlyDictionary<string, string> clientCredentials, ClientCredentialsPolicy credentialPolicy)
	{
		return new View(this, audience, clientCredentials, credentialPolicy);
	}

	/// <summary>
	/// Writes a bunch of diagnostic data to a JSON file.
	/// </summary>
	/// <param name="filePath">The path to the JSON file to be written. If it already exists it will be overwritten.</param>
	/// <param name="serviceAudience">The audience.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes when the writing is done.</returns>
	/// <remarks>
	/// Rough schema of JSON file:
	/// <code><![CDATA[
	///  {
	///    "perspectiveAudience": "Process",
	///    "activeRemoteSources" : [ "TrustedServer" ],
	///    "brokeredServices": [
	///      {
	///        name: "Calculator",
	///        version: "1.0",
	///        audience: "Local, Process, Guest",
	///        allowGuestClients: false,
	///        profferingPackage: "{28074D43-B498-47FE-97CF-4A182DA71C59}"
	///        profferedLocally: true,
	///        activeSource: "TrustedServer",
	///        includedByRemoteSourceManifest: true
	///      },
	///      {
	///        // ...
	///      },
	///      // ...
	///    ]
	///  }
	/// ]]></code>
	/// </remarks>
	public async Task ExportDiagnosticsAsync(string filePath, ServiceAudience serviceAudience, CancellationToken cancellationToken = default)
	{
		using var jsonWriter = new JsonTextWriter(new StreamWriter(new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 2048, useAsync: true), Encoding.UTF8, 2048))
		{
			Formatting = Formatting.Indented,
		};

		JObject json = await this.GetDiagnosticsAsync(serviceAudience, cancellationToken).ConfigureAwait(false);
		await json.WriteToAsync(jsonWriter).ConfigureAwait(false);
	}

	/// <summary>
	/// Returns the services that are registered locally that *may* be proffered by a particular remote source.
	/// </summary>
	/// <param name="remoteSource">The source of services.</param>
	/// <returns>A sequence of registered services that we may expect from the source.</returns>
	public IEnumerable<ServiceMoniker> GetServicesThatMayBeExpected(ServiceSource remoteSource)
	{
		foreach (KeyValuePair<ServiceMoniker, ServiceRegistration> tuple in this.registeredServices)
		{
			if (tuple.Value.IsExposedTo(ConvertRemoteSourceToLocalAudience(remoteSource)))
			{
				yield return tuple.Key;
			}
		}
	}

	/// <summary>
	/// Gets a service broker that may be provided to a <see cref="BrokeredServiceFactory"/>
	/// in order to automatically propagate <see cref="ServiceActivationOptions.ClientCredentials"/> from one service to its dependencies.
	/// </summary>
	/// <param name="options">The options passed to the originally requested service.</param>
	/// <returns>The filtering, authorizing service broker.</returns>
	public IServiceBroker GetSecureServiceBroker(ServiceActivationOptions options)
	{
		ClientCredentialsPolicy policy = ClientCredentialsPolicy.RequestOverridesDefault; // Apply client credentials where none are given to match that of the caller.
		return new View(this, ServiceAudience.Process, options.ClientCredentials ?? ImmutableDictionary<string, string>.Empty, policy, options.ClientCulture, options.ClientUICulture);
	}

	/// <inheritdoc cref="GetTraceSourceForConnectionAsync(IServiceBroker, ServiceMoniker, ServiceActivationOptions, bool, CancellationToken)"/>
	/// <devremarks>
	/// This method was created because <see cref="GetTraceSourceForConnectionAsync(IServiceBroker, ServiceMoniker, ServiceActivationOptions, bool, CancellationToken)"/>
	/// needed to be exposed publicly but as a protected virtual method, making it public would be a binary breaking change.
	/// </devremarks>
	public ValueTask<TraceSource?> GetTraceSourceForBrokeredServiceAsync(IServiceBroker serviceBroker, ServiceMoniker serviceMoniker, ServiceActivationOptions options, bool clientRole, CancellationToken cancellationToken) => this.GetTraceSourceForConnectionAsync(serviceBroker, serviceMoniker, options, clientRole, cancellationToken);

	/// <summary>
	/// Applies typical transformations on a descriptor fro brokered service clients and services.
	/// </summary>
	/// <param name="descriptor">The stock descriptor used for this service.</param>
	/// <param name="serviceBroker">A service broker that may be used to acquire other, related services as necessary to mutate the <paramref name="descriptor"/>.</param>
	/// <param name="serviceActivationOptions">The activation options for the service.</param>
	/// <param name="clientRole">A value indicating whether the <paramref name="descriptor"/> is about to be used to activate a client proxy or client connection; use <see langword="false" /> when activating the service itself.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The modified descriptor.</returns>
	internal async ValueTask<ServiceRpcDescriptor> ApplyDescriptorSettingsInternalAsync(ServiceRpcDescriptor descriptor, IServiceBroker serviceBroker, ServiceActivationOptions serviceActivationOptions, bool clientRole, CancellationToken cancellationToken)
	{
		TraceSource? traceSource = await this.GetTraceSourceForConnectionAsync(serviceBroker, descriptor.Moniker, serviceActivationOptions, clientRole, cancellationToken).ConfigureAwait(false);

		descriptor = descriptor
			.WithJoinableTaskFactory(this.joinableTaskFactory)
			.WithTraceSource(traceSource);

		if (descriptor is ServiceJsonRpcDescriptor { AdditionalServiceInterfaces: null } jsonRpcDescriptor &&
			this.RegisteredServices.TryGetValue(descriptor.Moniker, out ServiceRegistration? registration) &&
			registration.AdditionalServiceInterfaceTypeNames.Length > 0)
		{
			descriptor = jsonRpcDescriptor.WithAdditionalServiceInterfaces(registration.GetAdditionalServiceInterfaceTypes(this.traceSource));
		}

		return this.ApplyDescriptorSettings(descriptor, clientRole);
	}

	/// <summary>
	/// Registers a set of services with the global broker. This is separate from proffering a service. A service should be registered before it is proffered.
	/// An <see cref="IServiceBroker.AvailabilityChanged"/> event is never
	/// fired as a result of calling this method, but instead will be fired once the service is proffered.
	/// </summary>
	/// <param name="services">The set of services to be registered.</param>
	protected internal void RegisterServices(IReadOnlyDictionary<ServiceMoniker, ServiceRegistration> services)
	{
		Requires.NotNull(services);

		if (services.Count == 0)
		{
			return;
		}

		lock (this.syncObject)
		{
			foreach (KeyValuePair<ServiceMoniker, ServiceRegistration> service in services)
			{
				if (!this.registeredServices.ContainsKey(service.Key))
				{
					ImmutableInterlocked.TryAdd(ref this.registeredServices, service.Key, service.Value);
				}
				else
				{
					this.traceSource.TraceEvent(TraceEventType.Warning, (int)TraceEvents.Registered, "Service '{0}' has already been registered. Skipping duplicate registration.", service.Key);
				}
			}
		}
	}

	/// <summary>
	/// Applies typical transformations on a descriptor for brokered service clients and services.
	/// </summary>
	/// <param name="descriptor">The stock descriptor used for this service.</param>
	/// <param name="clientRole">A value indicating whether the <paramref name="descriptor"/> is about to be used to activate a client proxy or client connection; use <see langword="false" /> when activating the service itself.</param>
	/// <returns>The modified descriptor.</returns>
	protected virtual ServiceRpcDescriptor ApplyDescriptorSettings(ServiceRpcDescriptor descriptor, bool clientRole)
	{
		return descriptor;
	}

	/// <inheritdoc cref="ProfferIntrinsicService(ServiceRpcDescriptor, ServiceRegistration, ViewIntrinsicBrokeredServiceFactory)"/>
	protected IDisposable ProfferIntrinsicService(ServiceRpcDescriptor serviceDescriptor, ServiceRegistration newRegistration, BrokeredServiceFactory factory)
	{
		return this.ProfferIntrinsicService(serviceDescriptor, newRegistration, (view, mk, options, sb, ct) => factory(mk, options, sb, ct));
	}

	/// <summary>
	/// Proffers a very special brokered service that is intrinsic to each <see cref="View"/>.
	/// </summary>
	/// <param name="serviceDescriptor">The <see cref="ServiceRpcDescriptor"/> of the service.</param>
	/// <param name="newRegistration">The <see cref="ServiceRegistration"/> representing the service being registered.</param>
	/// <param name="factory">The factory that generates the new service.</param>
	/// <returns>An <see cref="IDisposable"/> that will remove the service when disposed.</returns>
	protected IDisposable ProfferIntrinsicService(ServiceRpcDescriptor serviceDescriptor, ServiceRegistration newRegistration, ViewIntrinsicBrokeredServiceFactory factory)
	{
		Requires.NotNull(serviceDescriptor);

		this.registeredServices = this.registeredServices.Add(serviceDescriptor.Moniker, newRegistration);
		return this.Proffer(new ProfferedViewIntrinsicService(this, serviceDescriptor, factory));
	}

	/// <summary>
	/// Unregisters a set of services with the global broker. This is separate from unproffering a service. A service should be unregistered before it is unproffered.
	/// An <see cref="IServiceBroker.AvailabilityChanged"/> event is never
	/// fired as a result of calling this method, but instead will be fired once the service is unproffered. To unproffer a service, simply dispose of it's proffering source.
	/// </summary>
	/// <param name="services">The set of services to be unregistered.</param>
	protected void UnregisterServices(IEnumerable<ServiceMoniker> services)
	{
		lock (this.syncObject)
		{
			this.registeredServices = this.registeredServices.RemoveRange(services);
		}
	}

	/// <summary>
	/// Gets a <see cref="TraceSource"/> to apply to some brokered service.
	/// </summary>
	/// <param name="serviceBroker">A service broker that may be used to create the <see cref="TraceSource"/>.</param>
	/// <param name="serviceMoniker">The moniker of the service being requested.</param>
	/// <param name="options">The activation options accompanying the request.</param>
	/// <param name="clientRole"><see langword="true"/> if the <see cref="TraceSource"/> will be used by the client of the service; <see langword="false"/> if used by the service itself.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A <see cref="TraceSource"/> instance that has the appropriate verbosity and listeners preconfigured, or <see langword="null" /> if the host provides no instance.</returns>
	/// <remarks>
	/// This method should be called by <see cref="IServiceBroker"/> implementations when requesting or activating services.
	/// The result of this method should be passed to <see cref="ServiceRpcDescriptor.WithTraceSource(TraceSource?)"/> before constructing the RPC connection.
	/// </remarks>
	protected virtual ValueTask<TraceSource?> GetTraceSourceForConnectionAsync(IServiceBroker serviceBroker, ServiceMoniker serviceMoniker, ServiceActivationOptions options, bool clientRole, CancellationToken cancellationToken) => default;

	/// <summary>
	/// Indexes a proffered service factory or broker for fast lookup.
	/// </summary>
	/// <param name="proffered">The proffering wrapper.</param>
	/// <returns>A value that may be disposed to cancel the proffer and remove its services from the index.</returns>
	protected virtual IDisposable Proffer(IProffered proffered)
	{
		Requires.NotNull(proffered);

		ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>> oldIndex;
		lock (this.syncObject)
		{
			oldIndex = this.profferedServiceIndex;

			if (proffered.Source > ServiceSource.OtherProcessOnSameMachine)
			{
				this.remoteSources = this.remoteSources.Add(proffered.Source, proffered);
			}

			if (!this.profferedServiceIndex.TryGetValue(proffered.Source, out ImmutableDictionary<ServiceMoniker, IProffered>? monikerAndProffer))
			{
				monikerAndProffer = ImmutableDictionary<ServiceMoniker, IProffered>.Empty;
			}

			// Index each service, and also validate that all proffered services are registered.
			var monikerAndProfferBuilder = monikerAndProffer.ToBuilder();
			ImmutableHashSet<ServiceMoniker> unregisteredMonikers = ImmutableHashSet<ServiceMoniker>.Empty;
			foreach (ServiceMoniker moniker in proffered.Monikers)
			{
				if (!this.registeredServices.ContainsKey(moniker))
				{
					unregisteredMonikers = unregisteredMonikers.Add(moniker);
				}

				if (monikerAndProfferBuilder.ContainsKey(moniker))
				{
					// Only load the resources assembly when we know we're in the failure case.
					Verify.FailOperation(Strings.ServiceMoniker_AlreadyProffered, moniker);
				}

				monikerAndProfferBuilder.Add(moniker, proffered);
			}

			Verify.Operation(unregisteredMonikers.IsEmpty, "Cannot proffer unregistered service(s): {0}", string.Join(", ", unregisteredMonikers));
			this.profferedServiceIndex = this.profferedServiceIndex.SetItem(proffered.Source, monikerAndProfferBuilder.ToImmutable());
		}

		if (this.traceSource.Switch.ShouldTrace(TraceEventType.Information))
		{
			this.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Proffered, "{0} proffered brokered service(s): {1}.", proffered.Source, ServiceBrokerUtilities.DeferredFormatting(() => string.Join(", ", proffered.Monikers)));
		}

		this.OnAvailabilityChanged(oldIndex, proffered);

		return proffered;
	}

	/// <summary>
	/// When overridden by a derived class, provides a hook to raise events, post telemetry, or log how each brokered service request was handled.
	/// </summary>
	/// <param name="moniker">The moniker for the requested service.</param>
	/// <param name="descriptor">The descriptor associated with the request, if available.</param>
	/// <param name="type">The nature of the brokered service request.</param>
	/// <param name="result">An indicator as to how the brokered service request was handled.</param>
	/// <param name="proffered">The proffering source that was used when activating the service.</param>
	protected virtual void OnRequestHandled(ServiceMoniker moniker, ServiceRpcDescriptor? descriptor, RequestType type, RequestResult result, IProffered? proffered)
	{
	}

	private static ServiceAudience ConvertRemoteSourceToLocalAudience(ServiceSource source)
	{
		return source switch
		{
			ServiceSource.OtherProcessOnSameMachine => ServiceAudience.Local,
			ServiceSource.TrustedExclusiveServer => ServiceAudience.RemoteExclusiveClient,
			ServiceSource.TrustedExclusiveClient => ServiceAudience.RemoteExclusiveServer,
			ServiceSource.TrustedServer => ServiceAudience.LiveShareGuest,
			ServiceSource.UntrustedServer => ServiceAudience.LiveShareGuest,
			_ => throw new NotSupportedException(),
		};
	}

	/// <summary>
	/// Gets a value indicating whether a given service audience represents a local consumer (vs. a remote one).
	/// </summary>
	/// <param name="filter">The filter in effect on the <see cref="IServiceBroker"/>.</param>
	/// <returns><see langword="true"/> if the filter represents a local consumer; <see langword="false"/> if a remote one.</returns>
	private static bool IsLocalConsumer(ServiceAudience filter) => (filter & ~ServiceAudience.Local) == ServiceAudience.None;

	/// <summary>
	/// Gets a JSON object that describes the current state of the container, including all registered services, proffer services, brokers, connections, etc.
	/// </summary>
	/// <param name="serviceAudience">Specifies what perspective of the container should be used for data that indicates whether a service is available.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A JSON object.</returns>
	/// <remarks>
	/// The contents of this JSON blob are meant for human diagnostic purposes and are subject to change.
	/// </remarks>
	private async Task<JObject> GetDiagnosticsAsync(ServiceAudience serviceAudience, CancellationToken cancellationToken = default)
	{
		var json = new JObject();

		IServiceBroker serviceBroker = this.GetFullAccessServiceBroker();
		var remoteServices = new HashSet<ServiceMoniker>();
		IBrokeredServiceManifest? manifest = await serviceBroker.GetProxyAsync<IBrokeredServiceManifest>(FrameworkServices.RemoteBrokeredServiceManifest, cancellationToken).ConfigureAwait(false);
		try
		{
			if (manifest != null)
			{
				remoteServices.UnionWith(await manifest.GetAvailableServicesAsync(cancellationToken).ConfigureAwait(false));
			}
		}
		finally
		{
			(manifest as IDisposable)?.Dispose();
		}

		json["perspectiveAudience"] = serviceAudience.ToString();
		json["activeRemoteSources"] = new JArray(this.remoteSources.Keys.Select(s => s.ToString()));
		json["localServicesBlockedDueToExclusiveClient"] = this.isClientOfExclusiveServer;
		json["brokeredServices"] = new JArray(
			from serviceRegistration in this.registeredServices
			select new JObject(
				new JProperty("name", serviceRegistration.Key.Name),
				new JProperty("version", serviceRegistration.Key.Version?.ToString()),
				new JProperty("audience", serviceRegistration.Value.Audience.ToString()),
				new JProperty("allowGuestClients", serviceRegistration.Value.AllowGuestClients),
				new JProperty("profferingPackage", serviceRegistration.Value.ProfferingPackageId),
				new JProperty("profferedLocally", IsProfferedLocally(serviceRegistration.Key)),
				new JProperty("activeSource", GetActiveSource(serviceRegistration.Key)?.ToString()),
				new JProperty("localSourceBlockedByExclusiveClient", this.IsLocalProfferedServiceBlockedOnExclusiveClient(serviceRegistration.Value, serviceAudience)),
				new JProperty("includedByRemoteSourceManifest", remoteServices?.Contains(serviceRegistration.Key))));

		return json;

		bool IsProfferedLocally(ServiceMoniker serviceMoniker)
		{
			return (this.profferedServiceIndex.TryGetValue(ServiceSource.SameProcess, out ImmutableDictionary<ServiceMoniker, IProffered>? proffers) && proffers.ContainsKey(serviceMoniker))
				|| (this.profferedServiceIndex.TryGetValue(ServiceSource.OtherProcessOnSameMachine, out proffers) && proffers.ContainsKey(serviceMoniker));
		}

		ServiceSource? GetActiveSource(ServiceMoniker serviceMoniker)
		{
			if (this.TryGetProfferingSource(serviceMoniker, serviceAudience, out IProffered? proffered, out _))
			{
				return proffered.Source;
			}

			return null;
		}
	}

	/// <summary>
	/// Filters the registered service monikers to those that should be visible to our process from a given <paramref name="source"/>,
	/// then optionally intersects that set with another set.
	/// </summary>
	/// <param name="source">The source of services that we should filter our registered list of services to.</param>
	/// <param name="serviceMonikers">The set of monikers to optionally intersect the filtered registered services with.</param>
	/// <returns>The filtered, intersected set of monikers.</returns>
	private ImmutableHashSet<ServiceMoniker> GetAllowedMonikers(ServiceSource source, ImmutableHashSet<ServiceMoniker>? serviceMonikers)
	{
		// Determine what service audience would be required on any service registration
		// in order for us to be willing to consume the proffered service, considering our position
		// relative to the service source.
		// For example if the services are coming from a Codespace Server,
		// then we would only consume services from it that we know are exposed to a Codespace Client.
		ServiceAudience ourAudienceRelativeToSource = ConvertRemoteSourceToLocalAudience(source);

		// We need to look up the monikers for all the services that are allowed to come from this source.
		ImmutableHashSet<ServiceMoniker>.Builder allowedMonikers = ImmutableHashSet.CreateBuilder<ServiceMoniker>();
		foreach ((ServiceMoniker moniker, ServiceRegistration registration) in this.registeredServices)
		{
			// We consider the service consumable from the remote if and only if
			// we would expose it over the same kind of connection if we were on the proffering side.
			// When "remote" is just another process on this same machine (e.g. ServiceHub),
			// we index the service even if this process can't consume it so we can expose it remotely.
			// TryGetProfferingSource will prevent *this* process from consuming when Local isn't an audience.
			if ((registration.Audience & ourAudienceRelativeToSource) == ourAudienceRelativeToSource || source == ServiceSource.OtherProcessOnSameMachine)
			{
				if (serviceMonikers == null || serviceMonikers.Contains(moniker))
				{
					allowedMonikers.Add(moniker);
				}
			}
		}

		return allowedMonikers.ToImmutable();
	}

	private bool IsPackageLoaded(object packageId)
	{
		lock (this.loadedPackageIds)
		{
			return this.loadedPackageIds.Contains(packageId);
		}
	}

	private void RecordPackageLoaded(object packageId)
	{
		lock (this.loadedPackageIds)
		{
			this.loadedPackageIds.Add(packageId);
		}
	}

	/// <summary>
	/// Removes proffered services from the index.
	/// </summary>
	/// <param name="proffered">The proffered element to remove.</param>
	private void RemoveRegistrations(IProffered proffered)
	{
		Requires.NotNull(proffered, nameof(proffered));

		ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>> oldIndex;
		lock (this.syncObject)
		{
			oldIndex = this.profferedServiceIndex;

			switch (proffered.Source)
			{
				case ServiceSource.SameProcess:
				case ServiceSource.OtherProcessOnSameMachine:
					if (this.profferedServiceIndex.TryGetValue(proffered.Source, out ImmutableDictionary<ServiceMoniker, IProffered>? profferedServices))
					{
						profferedServices = profferedServices.RemoveRange(proffered.Monikers);
						this.profferedServiceIndex = this.profferedServiceIndex.SetItem(proffered.Source, profferedServices);
					}

					break;
				default:
					// Non-local sources are only allowed to proffer one thing, so remove that.
					this.profferedServiceIndex = this.profferedServiceIndex.Remove(proffered.Source);
					this.remoteSources = this.remoteSources.Remove(proffered.Source);
					break;
			}
		}

		if (this.traceSource.Switch.ShouldTrace(TraceEventType.Information))
		{
			this.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Proffered, "Brokered service(s) UNproffered from {0}: {1}.", proffered.Source, ServiceBrokerUtilities.DeferredFormatting(() => string.Join(", ", proffered.Monikers)));
		}

		this.OnAvailabilityChanged(oldIndex, proffered);
	}

	/// <summary>
	/// Instructs each applicable <see cref="View"/> to raise its <see cref="IServiceBroker.AvailabilityChanged"/> event.
	/// </summary>
	/// <param name="oldIndex">The index of available services before the change. Null if no proffered index was changed, but an underlying service broker says a change was made.</param>
	/// <param name="proffered">The service proffering entity that has changed the set of services available to us.</param>
	/// <param name="impactedServices">A subset of services that are impacted by the change. If null, all services associated with the proffering party are impacted.</param>
	private void OnAvailabilityChanged(ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>>? oldIndex, IProffered proffered, IImmutableSet<ServiceMoniker>? impactedServices = null)
	{
		Requires.NotNull(proffered, nameof(proffered));
		Assumes.False(Monitor.IsEntered(this.syncObject)); // We should not hold a private lock while invoking event handlers.

		EventHandler<(ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>>?, ImmutableHashSet<ServiceMoniker>)>? availabilityChanged = this.AvailabilityChanged;
		if (availabilityChanged is null)
		{
			return;
		}

		// We must Intersect using a hash-based collection. Newtonsoft.Json tends to create ImmutableSortedSet<T> when deserializing
		// an IImmutableSet<T>, which causes Intersect of a (non-comparable) ServiceMoniker type.
		ImmutableHashSet<ServiceMoniker> intersectedServices = impactedServices != null ? proffered.Monikers.Intersect(impactedServices) : proffered.Monikers;

		// Raise the event asynchronously so as to not block the caller that triggered this change.
		// Some handlers are more expensive than others, and as we make no guarantee as to the thread this event is raised on,
		// it should never be raised on the main thread.
		// If handlers end up malfunctioning due to concurrent events, or if some handlers are so slow that they slow down others,
		// we could have one ReentrantSemaphore per subscriber and thus ensure each subscriber never is invoked concurrently, nor can slow down other handlers.
		if (this.joinableTaskFactory is not null)
		{
			_ = this.joinableTaskFactory.RunAsync(RaiseEventOnThreadpoolAsync);
		}
		else
		{
			RaiseEventOnThreadpoolAsync().Forget();
		}

		async Task RaiseEventOnThreadpoolAsync()
		{
			await TaskScheduler.Default.SwitchTo(alwaysYield: true);
			try
			{
				availabilityChanged?.Invoke(this, (oldIndex, intersectedServices));
			}
			catch (Exception ex)
			{
				this.traceSource.TraceData(TraceEventType.Error, (int)TraceEvents.EventHandlerFaulted, $"An exception occurred while raising the {nameof(this.AvailabilityChanged)} event: {0}", ex);
			}
		}
	}

	/// <summary>
	/// Gets the proffering broker for a given service, taking both remote and local services into account.
	/// </summary>
	/// <param name="serviceMoniker">The sought service.</param>
	/// <param name="consumingAudience">The audience filter that applies to the <see cref="IServiceBroker"/> that has received the request.</param>
	/// <param name="proffered">Receives the proffering wrapper if the service was found and exposed to the <paramref name="consumingAudience"/>.</param>
	/// <param name="errorCode">Receives the error code that describes why we failed to get a proffering source for the service, if applicable.</param>
	/// <returns><see langword="true"/> if the service broker wrapper was found; <see langword="false"/> otherwise.</returns>
	private bool TryGetProfferingSource(ServiceMoniker serviceMoniker, ServiceAudience consumingAudience, [NotNullWhen(true)] out IProffered? proffered, out MissingBrokeredServiceErrorCode errorCode)
	{
		return this.TryGetProfferingSource(this.profferedServiceIndex, serviceMoniker, consumingAudience, out proffered, out errorCode);
	}

	/// <summary>
	/// Gets the proffering broker for a given service, taking both remote and local services into account.
	/// </summary>
	/// <param name="profferedServiceIndex">The index to search for the proffering party.</param>
	/// <param name="serviceMoniker">The sought service.</param>
	/// <param name="consumingAudience">The audience filter that applies to the <see cref="IServiceBroker"/> that has received the request.</param>
	/// <param name="proffered">Receives the proffering wrapper if the service was found and exposed to the <paramref name="consumingAudience"/>.</param>
	/// <param name="errorCode">Receives the error code that describes why we failed to get a proffering source for the service, if applicable.</param>
	/// <returns><see langword="true"/> if the service broker wrapper was found; <see langword="false"/> otherwise.</returns>
	private bool TryGetProfferingSource(ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>> profferedServiceIndex, ServiceMoniker serviceMoniker, ServiceAudience consumingAudience, [NotNullWhen(true)] out IProffered? proffered, out MissingBrokeredServiceErrorCode errorCode)
	{
		ChaosBrokeredServiceAvailability availability =
			this.chaosMonkeyConfiguration?.BrokeredServices is { } chaos && chaos.TryGetValue(serviceMoniker, out ChaosBrokeredService? chaosConfig)
			? chaosConfig.Availability : ChaosBrokeredServiceAvailability.AllowAll;

		if (this.TryLookupServiceRegistration(serviceMoniker, out ServiceRegistration? serviceRegistration, out ServiceMoniker? matchingServiceMoniker))
		{
			// If the consumer is local, we're willing to provide services to them from remote sources.
			// Specifically: We don't provide remote consumers with remote services.
			// We do NOT check whether the consuming (local) audience should have visibility into the remotely acquired services
			// because the audience check was performed and services filtered when the remote service broker was originally proffered.
			bool anyRemoteSourceExists = false;
			if (IsLocalConsumer(consumingAudience))
			{
				foreach (ServiceSource source in PreferredSourceOrderForRemoteServices)
				{
					if (profferedServiceIndex.TryGetValue(source, out ImmutableDictionary<ServiceMoniker, IProffered>? proffersFromSource))
					{
						anyRemoteSourceExists = true;
						if (proffersFromSource.TryGetValue(matchingServiceMoniker, out proffered))
						{
							if (availability != ChaosBrokeredServiceAvailability.AllowAll)
							{
								if (this.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
								{
									this.traceSource.TraceEvent(TraceEventType.Warning, (int)TraceEvents.Request, "Request for \"{0}\" denied for remote request because of chaos configuration.", serviceMoniker);
								}

								errorCode = MissingBrokeredServiceErrorCode.ChaosConfigurationDeniedRequest;
								return false;
							}

							if (this.traceSource.Switch.ShouldTrace(TraceEventType.Information))
							{
								this.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Request, "Request for \"{0}\" will be fulfilled by {1}", serviceMoniker, proffered.Source);
							}

							errorCode = MissingBrokeredServiceErrorCode.NoExplanation;
							return true;
						}
					}
				}
			}

			// For locally proffered services, we first check that the consuming audience is allowed to see it.
			// We only reach this far if the requested service was not expected to come from a remote source that we checked above.
			if (serviceRegistration.IsExposedTo(consumingAudience))
			{
				if (this.IsLocalProfferedServiceBlockedOnExclusiveClient(serviceRegistration, consumingAudience) ||
					(anyRemoteSourceExists && serviceRegistration.IsExposedLocally && serviceRegistration.IsExposedRemotely))
				{
					// We are connected to some remote host (or expect to be soon), and this service is designed to come from at least one kind of remote host.
					// We therefore block the service from being accessed because we don't want a locally proffered service in this case.
					errorCode = MissingBrokeredServiceErrorCode.LocalServiceHiddenOnRemoteClient;
					proffered = default;
					return false;
				}

				foreach (ServiceSource source in PreferredSourceOrderForLocalServices)
				{
					if (profferedServiceIndex.TryGetValue(source, out ImmutableDictionary<ServiceMoniker, IProffered>? proffersFromSource))
					{
						if (proffersFromSource.TryGetValue(matchingServiceMoniker, out proffered))
						{
							if (availability == ChaosBrokeredServiceAvailability.DenyAll)
							{
								if (this.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
								{
									this.traceSource.TraceEvent(TraceEventType.Warning, (int)TraceEvents.Request, $"Request for {serviceMoniker} denied because of chaos configuration.");
								}

								errorCode = MissingBrokeredServiceErrorCode.ChaosConfigurationDeniedRequest;
								return false;
							}

							if (this.traceSource.Switch.ShouldTrace(TraceEventType.Information))
							{
								this.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Request, "Request for \"{0}\" will be fulfilled by {1}", serviceMoniker, proffered.Source);
							}

							errorCode = MissingBrokeredServiceErrorCode.NoExplanation;
							return true;
						}
					}
				}

				errorCode = MissingBrokeredServiceErrorCode.ServiceFactoryNotProffered;
			}
			else
			{
				if (this.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
				{
					this.traceSource.TraceEvent(TraceEventType.Warning, (int)TraceEvents.Request, "Request for \"{0}\" from {1} denied because the service is only exposed {2}.", serviceMoniker, consumingAudience, serviceRegistration.Audience);
				}

				errorCode = MissingBrokeredServiceErrorCode.ServiceAudienceMismatch;
			}
		}
		else
		{
			errorCode = MissingBrokeredServiceErrorCode.NotLocallyRegistered;
		}

		proffered = default;
		return false;
	}

	/// <summary>
	/// Checks whether the given service should be denied from a local source on the basis that it should always come from a Codespace Server.
	/// </summary>
	/// <param name="serviceRegistration">The service that might be denied.</param>
	/// <param name="consumingAudience">The consuming audience.</param>
	/// <returns><see langword="true"/> if the locally proffered service should *not* be activated; <see langword="false"/> otherwise.</returns>
	private bool IsLocalProfferedServiceBlockedOnExclusiveClient(ServiceRegistration serviceRegistration, ServiceAudience consumingAudience) =>
		this.isClientOfExclusiveServer && IsLocalConsumer(consumingAudience) && serviceRegistration.IsExposedTo(ServiceAudience.RemoteExclusiveClient);

	/// <summary>
	/// Checks the in-memory index of registered services for the registration of a named service.
	/// </summary>
	/// <param name="serviceMoniker">The moniker for the service. If this includes a version, and no registration for that version exists, a registration without a version may be matched.</param>
	/// <param name="serviceRegistration">The discovered service registration, if any.</param>
	/// <param name="matchingServiceMoniker">The <paramref name="serviceMoniker"/> if a match was found, or a copy with the version removed if only a version-less service was registered, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> if registration was found for the given <paramref name="serviceMoniker"/>; <see langword="false"/> otherwise.</returns>
	private bool TryLookupServiceRegistration(ServiceMoniker serviceMoniker, [NotNullWhen(true)] out ServiceRegistration? serviceRegistration, [NotNullWhen(true)] out ServiceMoniker? matchingServiceMoniker)
	{
		// Take a snapshot of the registeredServices in case it changes in the middle of this method.
		ImmutableDictionary<ServiceMoniker, ServiceRegistration> services = this.registeredServices;

		// Try looking up the service registration, first by exact match and second without the version.
		if (services.TryGetValue(serviceMoniker, out serviceRegistration))
		{
			matchingServiceMoniker = serviceMoniker;
			return true;
		}

		if (serviceMoniker.Version is not null)
		{
			var versionlessMoniker = new ServiceMoniker(serviceMoniker.Name);
			return this.TryLookupServiceRegistration(versionlessMoniker, out serviceRegistration, out matchingServiceMoniker);
		}

		matchingServiceMoniker = null;
		return false;
	}
}
