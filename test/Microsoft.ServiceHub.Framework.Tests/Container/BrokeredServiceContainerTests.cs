// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Testing;
using Xunit;

public class BrokeredServiceContainerTests
{
	private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcDescriptor(new ServiceMoniker("TestService"), ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
#pragma warning disable SA1310 // Field names should not contain underscore
	private static readonly ServiceRpcDescriptor Descriptor1_0 = new ServiceJsonRpcDescriptor(new ServiceMoniker("TestService", new(1, 0)), ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
	private static readonly ServiceRpcDescriptor Descriptor1_1 = new ServiceJsonRpcDescriptor(new ServiceMoniker("TestService", new(1, 1)), ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
#pragma warning restore SA1310 // Field names should not contain underscore
	private readonly MockBrokeredServiceContainer container = new MockBrokeredServiceContainer();
	private IServiceBroker serviceBroker;

	private ServiceMoniker? lastRequestedMoniker;

	public BrokeredServiceContainerTests()
	{
		this.serviceBroker = this.container.GetFullAccessServiceBroker();
	}

	private interface IMockService : IDisposable
	{
	}

	[Fact]
	public async Task UnversionedRequestMatchesUnversionedService()
	{
		this.ProfferUnversionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor))
		{
			Assert.NotNull(proxy);
			Assert.Equal(Descriptor.Moniker, this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task VersionedRequestMatchesUnversionedService()
	{
		this.ProfferUnversionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor1_0))
		{
			Assert.NotNull(proxy);
			Assert.Equal(Descriptor1_0.Moniker, this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task VersionedRequestMatchesVersionedService()
	{
		this.ProfferVersionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor1_0))
		{
			Assert.NotNull(proxy);
			Assert.Equal(Descriptor1_0.Moniker, this.lastRequestedMoniker);
		}
	}

	[Fact]
	public async Task VersionedRequestDoesNotMatchMisversionedService()
	{
		this.ProfferVersionedService();
		using (IMockService? proxy = await this.serviceBroker.GetProxyAsync<IMockService>(Descriptor1_1))
		{
			Assert.Null(proxy);
			Assert.Null(this.lastRequestedMoniker);
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

	private class MockService : IMockService
	{
		public void Dispose()
		{
		}
	}
}
