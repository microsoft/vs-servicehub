// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.IO.Pipelines;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

public class ServiceBrokerAggregatorTests : TestBase
{
	private static readonly ServiceRpcDescriptor Descriptor1 = new ServiceJsonRpcDescriptor(new ServiceMoniker("Some service 1"), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
	private readonly IReadOnlyList<InternalMockServiceBroker> mockBrokers = new[]
	{
		new InternalMockServiceBroker(),
		new InternalMockServiceBroker()
		{
			PipeResults = { { Descriptor1.Moniker, new MockPipe() } },
			ProxyResults = { { Descriptor1.Moniker, new MockProxy() } },
		},
		new InternalMockServiceBroker()
		{
			PipeResults = { { Descriptor1.Moniker, new MockPipe() } },
			ProxyResults = { { Descriptor1.Moniker, new MockProxy() } },
		},
	};

	public ServiceBrokerAggregatorTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public interface ICalculator
	{
		Task<int> AddAsync(int a, int b);
	}

	public interface ICalculatorCopy
	{
		Task<int> AddAsync(int a, int b);
	}

	[Fact]
	public void Sequential_Null()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerAggregator.Sequential(null!));
	}

	[Fact]
	public async Task Sequential_Empty()
	{
		IServiceBroker empty = ServiceBrokerAggregator.Sequential(Array.Empty<IServiceBroker>());
		Assert.Null(await empty.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken));
	}

	[Fact]
	public async Task Sequential_GetPipeAsync_StopsOnFirstHit()
	{
		IServiceBroker sequential = ServiceBrokerAggregator.Sequential(this.mockBrokers);
		IDuplexPipe? result = await sequential.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken);
		Assert.Same(this.mockBrokers[1].PipeResults[Descriptor1.Moniker], result);
		Assert.Equal(1, this.mockBrokers[0].QueryCounter);
		Assert.Equal(1, this.mockBrokers[1].QueryCounter);
		Assert.Equal(0, this.mockBrokers[2].QueryCounter);
	}

	[Fact]
	public async Task Sequential_GetProxyAsync_StopsOnFirstHit()
	{
		IServiceBroker sequential = ServiceBrokerAggregator.Sequential(this.mockBrokers);
		MockProxy? result = await sequential.GetProxyAsync<MockProxy>(Descriptor1, this.TimeoutToken);
		Assert.Same(this.mockBrokers[1].ProxyResults[Descriptor1.Moniker], result);
		Assert.Equal(1, this.mockBrokers[0].QueryCounter);
		Assert.Equal(1, this.mockBrokers[1].QueryCounter);
		Assert.Equal(0, this.mockBrokers[2].QueryCounter);
	}

	[Fact]
	public async Task Parallel_GetPipeAsync_AcquiresSingleHit()
	{
		IServiceBroker parallel = ServiceBrokerAggregator.Parallel(this.mockBrokers.Take(2).ToArray());
		IDuplexPipe? result = await parallel.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken);
		Assert.Same(this.mockBrokers[1].PipeResults[Descriptor1.Moniker], result);
		Assert.Equal(1, this.mockBrokers[0].QueryCounter);
		Assert.Equal(1, this.mockBrokers[1].QueryCounter);
	}

	[Fact]
	public async Task Parallel_GetProxyAsync_AcquiresSingleHit()
	{
		IServiceBroker parallel = ServiceBrokerAggregator.Parallel(this.mockBrokers.Take(2).ToArray());
		MockProxy? result = await parallel.GetProxyAsync<MockProxy>(Descriptor1, this.TimeoutToken);
		Assert.Same(this.mockBrokers[1].ProxyResults[Descriptor1.Moniker], result);
		Assert.Equal(1, this.mockBrokers[0].QueryCounter);
		Assert.Equal(1, this.mockBrokers[1].QueryCounter);
	}

	[Fact]
	public async Task Parallel_GetPipeAsync_ThrowsOnMoreThanOneHit()
	{
		IServiceBroker parallel = ServiceBrokerAggregator.Parallel(this.mockBrokers);
		await Assert.ThrowsAsync<ServiceCompositionException>(() => parallel.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken).AsTask());
		Assert.Equal(1, this.mockBrokers[0].QueryCounter);
		Assert.Equal(1, this.mockBrokers[1].QueryCounter);
		Assert.Equal(1, this.mockBrokers[2].QueryCounter);

		// Assert that the rejected results are properly completed so as to not leak server resources.
		await Assert.ThrowsAsync<ServiceCompositionException>(() => ((MockPipe)this.mockBrokers[1].PipeResults[Descriptor1.Moniker]).Other.Input.WaitForWriterCompletionAsync()).WithCancellation(this.TimeoutToken);
		await Assert.ThrowsAsync<ServiceCompositionException>(() => ((MockPipe)this.mockBrokers[1].PipeResults[Descriptor1.Moniker]).Other.Output.WaitForReaderCompletionAsync()).WithCancellation(this.TimeoutToken);
		await Assert.ThrowsAsync<ServiceCompositionException>(() => ((MockPipe)this.mockBrokers[2].PipeResults[Descriptor1.Moniker]).Other.Input.WaitForWriterCompletionAsync()).WithCancellation(this.TimeoutToken);
		await Assert.ThrowsAsync<ServiceCompositionException>(() => ((MockPipe)this.mockBrokers[2].PipeResults[Descriptor1.Moniker]).Other.Output.WaitForReaderCompletionAsync()).WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task Parallel_GetProxyAsync_ThrowsOnMoreThanOneHit()
	{
		IServiceBroker parallel = ServiceBrokerAggregator.Parallel(this.mockBrokers);
		await Assert.ThrowsAsync<ServiceCompositionException>(() => parallel.GetProxyAsync<MockProxy>(Descriptor1, this.TimeoutToken).AsTask());
		Assert.Equal(1, this.mockBrokers[0].QueryCounter);
		Assert.Equal(1, this.mockBrokers[1].QueryCounter);
		Assert.Equal(1, this.mockBrokers[2].QueryCounter);

		// Assert that the rejected results are properly disposed of so as to not leak server resources.
		Assert.True(((MockProxy)this.mockBrokers[1].ProxyResults[Descriptor1.Moniker]).IsDisposed);
		Assert.True(((MockProxy)this.mockBrokers[2].ProxyResults[Descriptor1.Moniker]).IsDisposed);
	}

	[Fact]
	public async Task Sequential_GetPipeAsync_NoHit()
	{
		var mock1 = new InternalMockServiceBroker();
		IServiceBroker sequential = ServiceBrokerAggregator.Sequential(new[] { mock1 });
		IDuplexPipe? result = await sequential.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken);
		Assert.Null(result);
	}

	[Fact]
	public async Task Sequential_GetProxyAsync_NoHit()
	{
		var mock1 = new InternalMockServiceBroker();
		IServiceBroker sequential = ServiceBrokerAggregator.Sequential(new[] { mock1 });
		MockProxy? result = await sequential.GetProxyAsync<MockProxy>(Descriptor1, this.TimeoutToken);
		Assert.Null(result);
	}

	[Fact]
	public async Task Parallel_GetPipeAsync_NoHit()
	{
		var mock1 = new InternalMockServiceBroker();
		IServiceBroker parallel = ServiceBrokerAggregator.Parallel(new[] { mock1 });
		IDuplexPipe? result = await parallel.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken);
		Assert.Null(result);
	}

	[Fact]
	public async Task Parallel_GetProxyAsync_NoHit()
	{
		var mock1 = new InternalMockServiceBroker();
		IServiceBroker parallel = ServiceBrokerAggregator.Parallel(new[] { mock1 });
		MockProxy? result = await parallel.GetProxyAsync<MockProxy>(Descriptor1, this.TimeoutToken);
		Assert.Null(result);
	}

	[Fact]
	public void Parallel_Null()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerAggregator.Parallel(null!));
	}

	[Fact]
	public async Task Parallel_Empty()
	{
		IServiceBroker empty = ServiceBrokerAggregator.Parallel(Array.Empty<IServiceBroker>());
		Assert.Null(await empty.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken));
	}

	[Fact]
	public void ForceMarshal_Null()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerAggregator.ForceMarshal(null!));
	}

	[Fact]
	public async Task ForceMarshal_GetPipeAsync()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(this.mockBrokers[1]);
		IDuplexPipe? result = await aggregator.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken);
		Assert.Same(result, this.mockBrokers[1].PipeResults[Descriptor1.Moniker]);
	}

	[Fact]
	public async Task ForceMarshal_GetProxyAsync()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(this.mockBrokers[1]);
		ICalculator? result = await aggregator.GetProxyAsync<ICalculator>(Descriptor1, this.TimeoutToken);
		Assumes.NotNull(result);
		Assert.NotSame(result, this.mockBrokers[1].PipeResults[Descriptor1.Moniker]);
		Assert.IsAssignableFrom<IJsonRpcClientProxy>(result);
	}

	[Fact]
	public async Task ForceMarshal_CanInvokeMethodOnTypeMatchingProxy()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(new MockServiceBroker());
		ICalculator? result = await aggregator.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
		Assumes.NotNull(result);
		Assert.IsAssignableFrom<IJsonRpcClientProxy>(result);
		Assert.Equal(8, await result.AddAsync(3, 5));
	}

	[Fact]
	public async Task ForceMarshal_CanInvokeMethodOnDifferentTypeProxy()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(new MockServiceBroker());
		ICalculatorCopy? result = await aggregator.GetProxyAsync<ICalculatorCopy>(TestServices.Calculator, this.TimeoutToken);
		Assumes.NotNull(result);
		Assert.IsAssignableFrom<IJsonRpcClientProxy>(result);
		Assert.Equal(8, await result.AddAsync(3, 5));
	}

	[Fact]
	public async Task ForceMarshal_EnsureNoRuntimeTypeChecksOccur()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(this.mockBrokers[1]);

		// Brokered service implements ICalculator while we ask for a proxy for ICalculatorCopy.
		// The copy interface has the same method but it is a different type which ForceMarshal broker should support.
		ICalculatorCopy? result = await aggregator.GetProxyAsync<ICalculatorCopy>(Descriptor1, this.TimeoutToken);
		Assumes.NotNull(result);
		Assert.NotSame(result, this.mockBrokers[1].PipeResults[Descriptor1.Moniker]);
		Assert.IsAssignableFrom<IJsonRpcClientProxy>(result);
	}

	[Fact]
	public async Task ForceMarshal_GetPipeAsync_NoMatch()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(this.mockBrokers[0]);
		Assert.Null(await aggregator.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken));
	}

	[Fact]
	public async Task ForceMarshal_GetProxyAsync_NoMatch()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.ForceMarshal(this.mockBrokers[0]);
		Assert.Null(await aggregator.GetProxyAsync<ICalculator>(Descriptor1, this.TimeoutToken));
	}

	[Fact]
	public void ForceMarshal_Events()
	{
		VerifyAvailabilityChangedEvent(ServiceBrokerAggregator.ForceMarshal);
	}

	[Fact]
	public void NonDisposable()
	{
		Assert.IsNotAssignableFrom<IDisposable>(ServiceBrokerAggregator.NonDisposable(new DisposableServiceBroker()));
	}

	[Fact]
	public void NonDisposable_Null()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerAggregator.NonDisposable(null!));
	}

	[Fact]
	public void NonDisposable_Events()
	{
		VerifyAvailabilityChangedEvent(ServiceBrokerAggregator.NonDisposable);
	}

	[Fact]
	public async Task NonDisposable_GetPipeAsync()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.NonDisposable(this.mockBrokers[1]);
		IDuplexPipe? result = await aggregator.GetPipeAsync(Descriptor1.Moniker, this.TimeoutToken);
		Assert.Same(result, this.mockBrokers[1].PipeResults[Descriptor1.Moniker]);
	}

	[Fact]
	public async Task NonDisposable_GetProxyAsync()
	{
		IServiceBroker aggregator = ServiceBrokerAggregator.NonDisposable(this.mockBrokers[1]);
		ICalculator? result = await aggregator.GetProxyAsync<ICalculator>(Descriptor1, this.TimeoutToken);
		Assumes.NotNull(result);
		Assert.Same(this.mockBrokers[1].ProxyResults[Descriptor1.Moniker], result);
	}

	private static void VerifyAvailabilityChangedEvent(Func<IServiceBroker, IServiceBroker> aggregator)
	{
		InternalMockServiceBroker testBroker = new();
		IServiceBroker wrapper = aggregator(testBroker);
		VerifyAvailabilityChangedEvent(testBroker, wrapper);
	}

	private static void VerifyAvailabilityChangedEvent(InternalMockServiceBroker innerBroker, IServiceBroker wrapper)
	{
		(object? Sender, BrokeredServicesChangedEventArgs Args)? handled = null;
		wrapper.AvailabilityChanged += Handler;
		try
		{
			BrokeredServicesChangedEventArgs args = new(ImmutableHashSet.Create(Descriptor1.Moniker), otherServicesImpacted: true);
			innerBroker.RaiseAvailabilityChanged(args);
			Assert.Equal((wrapper, args), handled);
		}
		finally
		{
			wrapper.AvailabilityChanged -= Handler;
		}

		void Handler(object? sender, BrokeredServicesChangedEventArgs args)
		{
			handled = (sender, args);
		}
	}

	private class InternalMockServiceBroker : IServiceBroker
	{
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		public int QueryCounter { get; set; }

		public Dictionary<ServiceMoniker, IDuplexPipe> PipeResults { get; } = new Dictionary<ServiceMoniker, IDuplexPipe>();

		public Dictionary<ServiceMoniker, object> ProxyResults { get; } = new Dictionary<ServiceMoniker, object>();

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceRpcDescriptor serviceDescriptor, CancellationToken cancellationToken)
		{
			this.QueryCounter++;
			this.PipeResults.TryGetValue(serviceDescriptor.Moniker, out IDuplexPipe? result);
			return new ValueTask<IDuplexPipe?>(result);
		}

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			this.QueryCounter++;
			this.PipeResults.TryGetValue(serviceMoniker, out IDuplexPipe? result);
			return new ValueTask<IDuplexPipe?>(result);
		}

		public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			this.QueryCounter++;
			this.ProxyResults.TryGetValue(serviceDescriptor.Moniker, out object? result);
			return new ValueTask<T?>((T?)result);
		}

		internal void RaiseAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.OnAvailabilityChanged(args);

		protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class DisposableServiceBroker : IServiceBroker, IDisposable
	{
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		internal bool IsDisposed { get; private set; }

		public void Dispose()
		{
			this.IsDisposed = true;
		}

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		{
			throw new NotImplementedException();
		}

		public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			throw new NotImplementedException();
		}

		protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class MockPipe : IDuplexPipe
	{
		private readonly IDuplexPipe oneSide;

		internal MockPipe()
		{
			(IDuplexPipe, IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
			this.oneSide = pair.Item1;
			this.Other = pair.Item2;
		}

		public PipeReader Input => this.oneSide.Input;

		public PipeWriter Output => this.oneSide.Output;

		internal IDuplexPipe Other { get; }
	}

	private class MockProxy : ICalculator, IDisposable
	{
		public bool IsDisposed { get; private set; }

		public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);

		public void Dispose()
		{
			this.IsDisposed = true;
		}
	}
}
