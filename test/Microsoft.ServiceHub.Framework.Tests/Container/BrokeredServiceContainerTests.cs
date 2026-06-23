// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Testing;
using Microsoft.VisualStudio.Shell.ServiceBroker;
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
}
