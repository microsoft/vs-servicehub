// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Testing;
using Xunit;

public class MockBrokeredServiceContainerTests
{
	private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcDescriptor(
		new ServiceMoniker("TestService"),
		ServiceJsonRpcDescriptor.Formatters.MessagePack,
		ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);

	private interface IMockService
	{
	}

	[Fact]
	public async Task ProfferedServiceCanBeObtained()
	{
		var container = new MockBrokeredServiceContainer();
		using IDisposable proffer = container.Proffer(Descriptor, (mk, options, sb, ct) => new(new MockService()));
		IServiceBroker sb = container.GetFullAccessServiceBroker();
		IMockService? proxy = await sb.GetProxyAsync<IMockService>(Descriptor, TestContext.Current.CancellationToken);
		try
		{
			Assert.NotNull(proxy);
		}
		finally
		{
			(proxy as IDisposable)?.Dispose();
		}
	}

	private class MockService : IMockService
	{
	}
}
