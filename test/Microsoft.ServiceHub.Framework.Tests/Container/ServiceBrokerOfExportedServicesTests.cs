// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Xunit;

/// <summary>
/// Tests for <see cref="ServiceBrokerOfExportedServices"/>.
/// </summary>
public class ServiceBrokerOfExportedServicesTests : TestBase
{
	private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcDescriptor(
		new ServiceMoniker("TestService"),
		ServiceJsonRpcDescriptor.Formatters.MessagePack,
		ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);

	public ServiceBrokerOfExportedServicesTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	private interface IMockService
	{
	}

	/// <summary>
	/// Verifies that a cancellation requested via the caller's own token surfaces as
	/// <see cref="OperationCanceledException"/> rather than being wrapped in a
	/// <see cref="ServiceActivationFailedException"/>. Callers (e.g. Roslyn's Edit-and-Continue
	/// brokered service proxy) suppress their own cancellations, so a wrapped activation failure
	/// would be misreported as a non-fatal error.
	/// </summary>
	[Fact]
	public async Task GetProxyAsync_CallerCanceledToken_ThrowsOperationCanceled()
	{
		IServiceBroker broker = new MockServiceBrokerOfExportedServices();
		using CancellationTokenSource cts = new();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await broker.GetProxyAsync<IMockService>(Descriptor, cts.Token));
	}

	/// <inheritdoc cref="GetProxyAsync_CallerCanceledToken_ThrowsOperationCanceled"/>
	[Fact]
	public async Task GetPipeAsync_CallerCanceledToken_ThrowsOperationCanceled()
	{
		IServiceBroker broker = new MockServiceBrokerOfExportedServices();
		using CancellationTokenSource cts = new();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await broker.GetPipeAsync(Descriptor.Moniker, cts.Token));
	}
}
