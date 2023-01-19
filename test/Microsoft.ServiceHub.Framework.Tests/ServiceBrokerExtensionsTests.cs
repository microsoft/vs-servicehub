// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Xunit;

public class ServiceBrokerExtensionsTests
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("some");
	private static readonly ServiceRpcDescriptor SomeDescriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
	private readonly CancellationToken cancelableToken = new CancellationTokenSource().Token;

	[Fact]
	public void GetPipeAsync_NullBroker()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerExtensions.GetPipeAsync(null!, SomeMoniker));
	}

	[Fact]
	public void GetProxyAsync_NullBroker()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerExtensions.GetProxyAsync<IDisposable>(null!, SomeDescriptor));
	}

	[Fact]
	public void GetPipeAsync_PropagatesArguments()
	{
		Assert.Same(MockPipe.Instance, ServiceBrokerExtensions.GetPipeAsync(MockServiceBroker.Instance, SomeMoniker, this.cancelableToken).Result);
	}

	[Fact]
	public void GetProxyAsync_PropagatesArguments()
	{
		Assert.NotNull(ServiceBrokerExtensions.GetProxyAsync<object>(MockServiceBroker.Instance, SomeDescriptor, this.cancelableToken).Result);
	}

	[Fact]
	public async Task GetProxyAsync_InferredGenericTypeArgumentSyntax()
	{
		var descriptor = new ServiceJsonRpcDescriptor<IDisposable>(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
		using (IDisposable? clientProxy = await MockServiceBroker.Instance.GetProxyAsync(descriptor, this.cancelableToken))
		{
		}
	}

	private class MockServiceBroker : IServiceBroker
	{
		internal static readonly MockServiceBroker Instance = new MockServiceBroker();

		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		{
			Assert.True(cancellationToken.CanBeCanceled);
			return new ValueTask<IDuplexPipe?>(SomeMoniker.Equals(serviceMoniker) ? MockPipe.Instance : null);
		}

		public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			Assert.True(cancellationToken.CanBeCanceled);
			return new ValueTask<T?>(SomeMoniker.Equals(serviceDescriptor?.Moniker) ? (T)(object)new MockService() : default(T));
		}

		protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class MockPipe : IDuplexPipe
	{
		internal static readonly IDuplexPipe Instance = new MockPipe();

		public PipeReader Input => throw new NotImplementedException();

		public PipeWriter Output => throw new NotImplementedException();
	}

	private class MockService : IDisposable
	{
		public void Dispose()
		{
		}
	}
}
