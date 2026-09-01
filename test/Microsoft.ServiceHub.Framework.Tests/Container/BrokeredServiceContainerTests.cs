// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Testing;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.ServiceBroker;
using Nerdbank.Streams;
using StreamJsonRpc;

public class BrokeredServiceContainerTests : TestBase
{
	private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcDescriptor(new ServiceMoniker("TestService"), ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
	private static readonly ServiceRpcDescriptor PolyTypeDescriptorWithMultiplexing = new ServiceJsonRpcPolyTypeDescriptor(new ServiceMoniker("TestPolyTypeService"), ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack, ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests.Default)
		.WithRpcTargetMetadata(RpcTargetMetadata.FromShape<ITestRpcContract>())
		.WithMultiplexingStream(new() { ProtocolMajorVersion = 3 });

#pragma warning disable SA1310 // Field names should not contain underscore
	private static readonly ServiceRpcDescriptor Descriptor1_0 = new ServiceJsonRpcDescriptor(new ServiceMoniker("TestService", new(1, 0)), ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
	private static readonly ServiceRpcDescriptor Descriptor1_1 = new ServiceJsonRpcDescriptor(new ServiceMoniker("TestService", new(1, 1)), ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
#pragma warning restore SA1310 // Field names should not contain underscore
	private readonly MockBrokeredServiceContainer container;
	private IServiceBroker serviceBroker;

	private ServiceMoniker? lastRequestedMoniker;

	public BrokeredServiceContainerTests(ITestOutputHelper logger)
		: base(logger)
	{
		this.container = new MockBrokeredServiceContainer(new TraceSource("Test") { Switch = { Level = SourceLevels.All }, Listeners = { new XunitTraceListener(logger) } });
		this.serviceBroker = this.container.GetFullAccessServiceBroker();
	}

	private interface IMockService : IDisposable
	{
	}

	private interface IMockService2
	{
	}

	private interface IMockService3
	{
	}

	[Fact]
	public async Task UnversionedRequestMatchesUnversionedService()
	{
		this.ProfferUnversionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken))
		{
			Assert.NotNull(proxy);
			Assert.Equal(Descriptor.Moniker, this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task VersionedRequestMatchesUnversionedService()
	{
		this.ProfferUnversionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor1_0, this.TimeoutToken))
		{
			Assert.NotNull(proxy);
			Assert.Equal(Descriptor1_0.Moniker, this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task VersionedRequestMatchesVersionedService()
	{
		this.ProfferVersionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor1_0, this.TimeoutToken))
		{
			Assert.NotNull(proxy);
			Assert.Equal(Descriptor1_0.Moniker, this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task VersionedRequestDoesNotMatchMisversionedService()
	{
		this.ProfferVersionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor1_1, this.TimeoutToken))
		{
			Assert.Null(proxy);
			Assert.Null(this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task ApplyDescriptorOverrideIsCalled()
	{
		ServiceRpcDescriptor? callbackDescriptor = null;

		bool serverRoleCalled = false;
		bool clientRoleCalled = false;

		var internalContainer = new MockBrokeredServiceContainerWithDescriptorCallback();

		internalContainer.Proffer(Descriptor1_0, (mk, options, sb, ct) =>
		{
			return new(new MockService());
		});

		internalContainer.ApplyDescriptorCallback = (descriptor, clientRole) =>
		{
			// Only check the test service moniker, ignore others like authorization service.
			if (descriptor.Moniker != Descriptor1_0.Moniker)
			{
				return descriptor;
			}

			if (clientRole)
			{
				Assert.False(clientRoleCalled); // we only expect one call.
				clientRoleCalled = true;
			}
			else
			{
				Assert.False(serverRoleCalled); // we only expect one call.
				serverRoleCalled = true;
			}

			return callbackDescriptor = descriptor;
		};

		using (IMockService? proxy = await internalContainer.GetFullAccessServiceBroker().GetProxyAsync<IMockService>(Descriptor1_0, this.TimeoutToken))
		{
			Assert.NotNull(proxy);
		}

		Assert.Equal(Descriptor1_0, callbackDescriptor);
		Assert.True(clientRoleCalled);
		Assert.True(serverRoleCalled);
	}

	[Fact]
	[Trait("CWE", "610")]
	public async Task UntrustedRemoteBrokerWithMultiplexingDoesNotAdvertiseIpcPipe()
	{
		ServiceBrokerClientMetadata? receivedMetadata = null;
		var remoteBroker = new MockRemoteServiceBroker
		{
			HandshakeCallback = metadata => receivedMetadata = metadata,
		};

		this.container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{ Descriptor.Moniker, new ServiceRegistration(ServiceAudience.LiveShareGuest, null, allowGuestClients: true) },
		});

		(MultiplexingStream localMultiplexingStream, MultiplexingStream remoteMultiplexingStream) = await this.CreateMultiplexingStreamPairAsync();
		using (localMultiplexingStream)
		using (remoteMultiplexingStream)
		using (this.container.ProfferRemoteBroker(remoteBroker, localMultiplexingStream, ServiceSource.UntrustedServer, [Descriptor.Moniker]))
		{
			Assert.Null(await this.serviceBroker.GetPipeAsync(Descriptor.Moniker, this.TimeoutToken));
		}

		Assert.NotNull(receivedMetadata);
		RemoteServiceConnections advertisedRemoteConnections = receivedMetadata.Value.SupportedConnections & (RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing);
		Assert.Equal(RemoteServiceConnections.Multiplexing, advertisedRemoteConnections);
	}

	[Fact]
	public async Task ServiceRegistrationSpecifiesAdditionalProxyInterfaces()
	{
		this.container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{
				Descriptor.Moniker,
				new ServiceRegistration(ServiceAudience.Process, null, false)
				{
					AdditionalServiceInterfaceTypeNames = [typeof(IMockService2).AssemblyQualifiedName!],
				}
			},
		});

		this.ProfferUnversionedService();
		IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
		Assert.IsAssignableFrom<IMockService2>(proxy);
	}

	[Fact]
	public async Task ServiceRegistrationSpecifiesAdditionalProxyInterfaces_OverrideInterfaces()
	{
		this.container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{
				Descriptor.Moniker,
				new ServiceRegistration(ServiceAudience.Process, null, false)
				{
					AdditionalServiceInterfaceTypeNames = [typeof(IMockService2).AssemblyQualifiedName!],
				}
			},
		});

		this.ProfferUnversionedService();
		IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(
			((ServiceJsonRpcDescriptor)Descriptor).WithAdditionalServiceInterfaces([typeof(IMockService3)]), this.TimeoutToken);
		Assert.NotNull(proxy);
		Assert.IsNotAssignableFrom<IMockService2>(proxy);
		Assert.IsAssignableFrom<IMockService3>(proxy);
	}

	[Fact]
	public async Task ServiceRegistrationSpecifiesAdditionalProxyInterfaces_SuppressesInterfaces()
	{
		this.container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{
				Descriptor.Moniker,
				new ServiceRegistration(ServiceAudience.Process, null, false)
				{
					AdditionalServiceInterfaceTypeNames = [typeof(IMockService2).AssemblyQualifiedName!],
				}
			},
		});

		this.ProfferUnversionedService();
		IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(
			((ServiceJsonRpcDescriptor)Descriptor).WithAdditionalServiceInterfaces([]), this.TimeoutToken);
		Assert.NotNull(proxy);
		Assert.IsNotAssignableFrom<IMockService2>(proxy);
		Assert.IsNotAssignableFrom<IMockService3>(proxy);
	}

	[Fact]
	public async Task ServiceRegistrationSpecifiesAdditionalProxyInterfaces_BadTypeNames()
	{
		this.container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{
				Descriptor.Moniker,
				new ServiceRegistration(ServiceAudience.Process, null, false)
				{
					AdditionalServiceInterfaceTypeNames = ["BadTypeName, SomeAssembly"],
				}
			},
		});

		this.ProfferUnversionedService();
		IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	[Fact]
	public async Task ProfferedPolyTypeDescriptorPreservesMultiplexingOptions()
	{
		this.container.Proffer(PolyTypeDescriptorWithMultiplexing, (mk, options, sb, ct) => new(new TestRpcService()));

		IDuplexPipe? pipe = await this.serviceBroker.GetPipeAsync(PolyTypeDescriptorWithMultiplexing.Moniker, this.TimeoutToken);
		Assert.NotNull(pipe);

		ITestRpcContract proxy = PolyTypeDescriptorWithMultiplexing.ConstructRpc<ITestRpcContract>(pipe);
		using (proxy as IDisposable)
		{
			MemoryStream stream = new([1, 2, 3]);
			byte[] result = await proxy.ReadStreamAsync(stream, this.TimeoutToken);

			Assert.Equal(stream.ToArray(), result);
		}
	}

	[Fact, Obsolete]
	public void GetServicesThatMayBeExpected_LoadsSynchronousRegistrationsOnce()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		container.SynchronousRegistrations.Add(Descriptor.Moniker, new ServiceRegistration(ServiceAudience.LiveShareGuest, null, allowGuestClients: false));

		List<ServiceMoniker> firstEnumeration = [.. container.GetServicesThatMayBeExpected(ServiceSource.TrustedServer)];
		List<ServiceMoniker> secondEnumeration = [.. container.GetServicesThatMayBeExpected(ServiceSource.TrustedServer)];

		Assert.Contains(Descriptor.Moniker, firstEnumeration);
		Assert.Equal(firstEnumeration, secondEnumeration);
		Assert.Equal(1, container.LoadAllRegistrationsCallCount);
	}

	[Fact]
	public async Task GetServicesThatMayBeExpectedAsync_LoadsSynchronousRegistrationsOnce()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		container.SynchronousRegistrations.Add(Descriptor.Moniker, new ServiceRegistration(ServiceAudience.LiveShareGuest, null, allowGuestClients: false));

		List<ServiceMoniker> firstEnumeration = [.. await container.GetServicesThatMayBeExpectedAsync(ServiceSource.TrustedServer, this.TimeoutToken)];
		List<ServiceMoniker> secondEnumeration = [.. await container.GetServicesThatMayBeExpectedAsync(ServiceSource.TrustedServer, this.TimeoutToken)];

		Assert.Contains(Descriptor.Moniker, firstEnumeration);
		Assert.Equal(firstEnumeration, secondEnumeration);
		Assert.Equal(1, container.LoadAllRegistrationsCallCount);
	}

	[Fact]
	public async Task LoadAllRegistrationsAsync_LoadsSynchronousAndAsynchronousRegistrationsOnce()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		container.SynchronousRegistrations.Add(Descriptor.Moniker, new ServiceRegistration(ServiceAudience.LiveShareGuest, null, allowGuestClients: false));
		container.AsynchronousRegistrations.Add(Descriptor1_0.Moniker, new ServiceRegistration(ServiceAudience.LiveShareGuest, null, allowGuestClients: false));

		await container.LoadAllRegistrationsForTestingAsync(this.TimeoutToken);
		await container.LoadAllRegistrationsForTestingAsync(this.TimeoutToken);

		Assert.Equal(1, container.LoadAllRegistrationsCallCount);
		Assert.Equal(1, container.LoadAllRegistrationsAsyncCallCount);
		Assert.Contains(Descriptor.Moniker, container.RegisteredMonikers);
		Assert.Contains(Descriptor1_0.Moniker, container.RegisteredMonikers);
	}

	[Fact]
	public void RegisterServices_AllowsIdenticalDuplicateRegistration()
	{
		MockBrokeredServiceContainer container = new();
		ServiceRegistration registration = new(ServiceAudience.Process, null, allowGuestClients: false);

		container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration> { { Descriptor.Moniker, registration } });
		container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration> { { Descriptor.Moniker, registration } });
	}

	[Fact]
	public void Proffer_IgnoresConflictingLazySynchronousRegistration()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		ServiceRegistration registered = new(ServiceAudience.Process, null, allowGuestClients: false);
		container.RegisterServicesForTesting(new Dictionary<ServiceMoniker, ServiceRegistration> { { Descriptor.Moniker, registered } });
		container.SynchronousPerServiceRegistrations.Add(Descriptor.Moniker, new ServiceRegistration(ServiceAudience.Local, null, allowGuestClients: true));

		container.Proffer(Descriptor, (mk, options, sb, ct) => new(new MockService()));

		Assert.Same(registered, container.GetRegisteredServiceRegistration(Descriptor.Moniker));
	}

	[Fact]
	public async Task GetProxyAsync_IgnoresConflictingLazyAsynchronousRegistration()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		ServiceRegistration registered = new(ServiceAudience.Process, null, allowGuestClients: false);
		container.RegisterServicesForTesting(new Dictionary<ServiceMoniker, ServiceRegistration> { { Descriptor.Moniker, registered } });
		container.AsynchronousPerServiceRegistrations.Add(Descriptor.Moniker, new ServiceRegistration(ServiceAudience.Local, null, allowGuestClients: true));

		Assert.Null(await container.GetFullAccessServiceBroker().GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken));
		Assert.Same(registered, container.GetRegisteredServiceRegistration(Descriptor.Moniker));
	}

	[Fact]
	public async Task LazyRegistrationDuringRequest_DoesNotRaiseAvailabilityChanged()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		IServiceBroker serviceBroker = container.GetFullAccessServiceBroker();
		TaskCompletionSource<BrokeredServicesChangedEventArgs> availabilityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
		serviceBroker.AvailabilityChanged += (_, args) => availabilityChanged.TrySetResult(args);
		container.AsynchronousPerServiceRegistrations.Add(
			Descriptor.Moniker,
			new ProfferingServiceRegistration(
				ServiceAudience.Process,
				new object(),
				() => container.Proffer(Descriptor, (mk, options, sb, ct) => new(new MockService()))));

		using IMockService? proxy = await serviceBroker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);

		Assert.NotNull(proxy);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => availabilityChanged.Task.WithCancellation(ExpectedTimeoutToken));
	}

	[Fact]
	public async Task RegisterServicesAfterMissingRequest_RaisesAvailabilityChangedToInterestedBrokers()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		IServiceBroker usedBroker = container.GetFullAccessServiceBroker();
		IServiceBroker unusedBroker = container.GetFullAccessServiceBroker();
		TaskCompletionSource<BrokeredServicesChangedEventArgs> availabilityChangedArgs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<BrokeredServicesChangedEventArgs> unexpectedAvailabilityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
		usedBroker.AvailabilityChanged += (_, args) => availabilityChangedArgs.TrySetResult(args);
		unusedBroker.AvailabilityChanged += (_, args) => unexpectedAvailabilityChanged.TrySetResult(args);

		Assert.Null(await usedBroker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken));

		container.RegisterServicesForTesting(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{ Descriptor.Moniker, new ServiceRegistration(ServiceAudience.Process, null, allowGuestClients: false) },
		});

		BrokeredServicesChangedEventArgs args = await availabilityChangedArgs.Task.WithCancellation(this.TimeoutToken);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unexpectedAvailabilityChanged.Task.WithCancellation(ExpectedTimeoutToken));

		Assert.Contains(Descriptor.Moniker, args.ImpactedServices);
		Assert.False(args.OtherServicesImpacted);
	}

	[Fact]
	public void ProffererId_WeakReferenceEqualityUsesMatchingHashCode()
	{
		static object CreateProffererId(object proffered)
		{
			Type proffererIdType = typeof(GlobalBrokeredServiceContainer).GetNestedType("ProffererId", BindingFlags.NonPublic)!;
			Type iProfferedType = typeof(GlobalBrokeredServiceContainer).GetNestedType("IProffered", BindingFlags.NonPublic)!;
			ConstructorInfo constructor = proffererIdType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [iProfferedType], modifiers: null)!;
			return constructor.Invoke([proffered]);
		}

		object proffered = new WeakReferenceProfferedContainer().CreateWeakReferenceProffered();

		object first = CreateProffererId(proffered);
		object second = CreateProffererId(proffered);

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public async Task RegisterVersionlessServiceAfterMissingVersionedRequest_RaisesAvailabilityChangedToInterestedBrokers()
	{
		LazyRegistrationBrokeredServiceContainer container = new(new TraceSource("Test"));
		IServiceBroker usedBroker = container.GetFullAccessServiceBroker();
		IServiceBroker unusedBroker = container.GetFullAccessServiceBroker();
		TaskCompletionSource<BrokeredServicesChangedEventArgs> availabilityChangedArgs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<BrokeredServicesChangedEventArgs> unexpectedAvailabilityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
		usedBroker.AvailabilityChanged += (_, args) => availabilityChangedArgs.TrySetResult(args);
		unusedBroker.AvailabilityChanged += (_, args) => unexpectedAvailabilityChanged.TrySetResult(args);

		Assert.Null(await usedBroker.GetProxyAsync<IMockService>(Descriptor1_0, this.TimeoutToken));

		container.RegisterServicesForTesting(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{ Descriptor.Moniker, new ServiceRegistration(ServiceAudience.Process, null, allowGuestClients: false) },
		});

		BrokeredServicesChangedEventArgs args = await availabilityChangedArgs.Task.WithCancellation(this.TimeoutToken);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unexpectedAvailabilityChanged.Task.WithCancellation(ExpectedTimeoutToken));

		Assert.Equal(Descriptor1_0.Moniker, Assert.Single(args.ImpactedServices));
		Assert.False(args.OtherServicesImpacted);
	}

	private void ProfferUnversionedService()
	{
		this.container.Proffer(Descriptor, (mk, options, sb, ct) =>
		{
			this.lastRequestedMoniker = mk;
			return new(new MockService());
		});
	}

	private void ProfferVersionedService()
	{
		this.container.Proffer(Descriptor1_0, (mk, options, sb, ct) =>
		{
			this.lastRequestedMoniker = mk;
			return new(new MockService());
		});
	}

	private async Task<(MultiplexingStream Local, MultiplexingStream Remote)> CreateMultiplexingStreamPairAsync()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<MultiplexingStream> localMultiplexingStream = MultiplexingStream.CreateAsync(pair.Item1, this.CreateTestMXStreamOptions(), this.TimeoutToken);
		Task<MultiplexingStream> remoteMultiplexingStream = MultiplexingStream.CreateAsync(pair.Item2, this.CreateTestMXStreamOptions(isServer: true), this.TimeoutToken);
		return (await localMultiplexingStream, await remoteMultiplexingStream);
	}

	private class MockService : IMockService, IMockService2, IMockService3
	{
		public void Dispose()
		{
		}
	}

	private sealed class WeakReferenceProfferedContainer : GlobalBrokeredServiceContainer
	{
		internal WeakReferenceProfferedContainer()
			: base(ImmutableDictionary<ServiceMoniker, ServiceRegistration>.Empty, isClientOfExclusiveServer: false, joinableTaskFactory: null, new TraceSource("Test"))
		{
		}

		public override IReadOnlyDictionary<string, string> LocalUserCredentials => ImmutableDictionary<string, string>.Empty;

		internal object CreateWeakReferenceProffered() => new WeakReferenceProffered();

		private sealed class WeakReferenceProffered : IProffered
		{
			public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
			{
				add { }
				remove { }
			}

			public ServiceSource Source => ServiceSource.SameProcess;

			public ImmutableHashSet<ServiceMoniker> Monikers { get; } = ImmutableHashSet<ServiceMoniker>.Empty;

			public void Dispose()
			{
			}

			public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
				=> throw new NotSupportedException();

			public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
				=> throw new NotSupportedException();

			public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
				where T : class
				=> throw new NotSupportedException();

			public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
				=> throw new NotSupportedException();

			public Task CancelServiceRequestAsync(Guid serviceRequestId)
				=> throw new NotSupportedException();
		}
	}

	private sealed class LazyRegistrationBrokeredServiceContainer : GlobalBrokeredServiceContainer
	{
		internal LazyRegistrationBrokeredServiceContainer(TraceSource traceSource)
			: base(ImmutableDictionary<ServiceMoniker, ServiceRegistration>.Empty, isClientOfExclusiveServer: false, joinableTaskFactory: null, traceSource)
		{
		}

		public override IReadOnlyDictionary<string, string> LocalUserCredentials => ImmutableDictionary<string, string>.Empty;

		internal Dictionary<ServiceMoniker, ServiceRegistration> SynchronousRegistrations { get; } = new();

		internal Dictionary<ServiceMoniker, ServiceRegistration> SynchronousPerServiceRegistrations { get; } = new();

		internal Dictionary<ServiceMoniker, ServiceRegistration> AsynchronousRegistrations { get; } = new();

		internal Dictionary<ServiceMoniker, ServiceRegistration> AsynchronousPerServiceRegistrations { get; } = new();

		internal int LoadAllRegistrationsCallCount { get; private set; }

		internal int LoadAllRegistrationsAsyncCallCount { get; private set; }

		internal ImmutableArray<ServiceMoniker> RegisteredMonikers => this.RegisteredServices.Keys.ToImmutableArray();

		internal Task LoadAllRegistrationsForTestingAsync(CancellationToken cancellationToken) => this.LoadAllRegistrationsAsync(cancellationToken).AsTask();

		internal void RegisterServicesForTesting(IReadOnlyDictionary<ServiceMoniker, ServiceRegistration> services) => this.RegisterServices(services);

		internal ServiceRegistration GetRegisteredServiceRegistration(ServiceMoniker serviceMoniker) => this.RegisteredServices[serviceMoniker];

		protected override void LoadAllRegistrationsCore()
		{
			this.LoadAllRegistrationsCallCount++;
			this.RegisterServices(this.SynchronousRegistrations);
		}

		protected override bool TryGetServiceRegistrationCore(ServiceMoniker serviceMoniker, out ServiceRegistration? registration)
			=> this.SynchronousPerServiceRegistrations.TryGetValue(serviceMoniker, out registration);

		protected override ValueTask LoadAllRegistrationsCoreAsync()
		{
			this.LoadAllRegistrationsAsyncCallCount++;
			this.RegisterServices(this.AsynchronousRegistrations);
			return default;
		}

		protected override ValueTask<ServiceRegistration?> TryGetServiceRegistrationCoreAsync(ServiceMoniker serviceMoniker, CancellationToken cancellationToken)
			=> new(this.AsynchronousPerServiceRegistrations.TryGetValue(serviceMoniker, out ServiceRegistration? registration) ? registration : null);
	}

	private sealed class ProfferingServiceRegistration : ServiceRegistration
	{
		private readonly Action loadProfferingPackage;

		internal ProfferingServiceRegistration(ServiceAudience audience, object profferingPackageId, Action loadProfferingPackage)
			: base(audience, profferingPackageId, allowGuestClients: true)
		{
			this.loadProfferingPackage = loadProfferingPackage;
		}

		protected override Task LoadProfferingPackageAsync(CancellationToken cancellationToken)
		{
			this.loadProfferingPackage();
			return Task.CompletedTask;
		}
	}
}
