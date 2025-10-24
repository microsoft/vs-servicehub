// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;
using Microsoft.ServiceHub.Framework;
using PolyType;
using StreamJsonRpc;

/// <summary>
/// Runtime tests for local proxy discovery and usage.
/// </summary>
public partial class SourceGeneratedLocalProxyTests
{
	private static readonly ServiceMoniker CalculatorMoniker = new("Calc");
	private static readonly ServiceJsonRpcPolyTypeDescriptor CalculatorDescriptor = new(
		CalculatorMoniker,
		ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
		ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
		PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests.Default);

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	public partial interface ICalculator
	{
		ValueTask<int> AddAsync(int a, int b, CancellationToken cancellationToken);

		Task<int> SubtractAsync(int a, int b, CancellationToken cancellationToken);
	}

	[JsonRpcContract]
	[JsonRpcProxyInterfaceGroup(typeof(IScientificCalculator))]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	public partial interface IMultifunctionCalculator : ICalculator
	{
	}

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	public partial interface IScientificCalculator : IMultifunctionCalculator
	{
		ValueTask<double> GetPiAsync();
	}

	[Fact]
	public void Dispose_OnDisposableTarget()
	{
		GoodCalculator target = new();
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(target);
		Assert.False(((IDisposableObservable)calculator).IsDisposed);

		((IDisposable)calculator).Dispose();
		Assert.True(((IDisposableObservable)calculator).IsDisposed);
		Assert.Equal(1, target.DisposedCounter);

		// Dispose again to ensure that subsequent disposal calls are *not* forwarded.
		((IDisposable)calculator).Dispose();
		Assert.Equal(1, target.DisposedCounter);
	}

	[Fact]
	public void Dispose_OnNonDisposableTarget()
	{
		ThrowingCalculator target = new();
		Assert.IsNotAssignableFrom<IDisposable>(target); // sanity check for the test

		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(target);
		Assert.False(((IDisposableObservable)calculator).IsDisposed);

		((IDisposable)calculator).Dispose();
		Assert.True(((IDisposableObservable)calculator).IsDisposed);
	}

	[Fact]
	public void Dispose_RaisesEvent()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new GoodCalculator());
		int disposed = 0;
		((INotifyDisposable)calculator).Disposed += (sender, e) =>
		{
			Assert.Same(calculator, sender);
			disposed++;
		};

		((IDisposable)calculator).Dispose();
		Assert.Equal(1, disposed);
	}

	[Fact]
	public async Task ValueTaskOfTResult()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new GoodCalculator());
		Assert.Equal(5, await calculator.AddAsync(2, 3, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ValueTaskOfT_ThrowsRemoteInvocationException()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new ThrowingCalculator());
		RemoteInvocationException ex = await Assert.ThrowsAsync<RemoteInvocationException>(
			async () => await calculator.AddAsync(2, 3, TestContext.Current.CancellationToken));
		TestContext.Current.TestOutputHelper?.WriteLine(ex.ToString());
		Assert.Null(ex.InnerException);
	}

	[Fact]
	public async Task ValueTaskOfT_ThrowsRemoteInvocationExceptionWithInnerException()
	{
		ICalculator calculator = CalculatorDescriptor
			.WithExceptionStrategy(ExceptionProcessing.ISerializable)
			.ConstructLocalProxy<ICalculator>(new ThrowingCalculator());
		RemoteInvocationException ex = await Assert.ThrowsAsync<RemoteInvocationException>(
			async () => await calculator.AddAsync(2, 3, TestContext.Current.CancellationToken));
		TestContext.Current.TestOutputHelper?.WriteLine(ex.ToString());
		Assert.IsType<InvalidOperationException>(ex.GetBaseException());
	}

	[Fact]
	public async Task TaskOfTResult()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new GoodCalculator());
		Assert.Equal(-1, await calculator.SubtractAsync(2, 3, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task TaskOfT_ThrowsRemoteInvocationException()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new ThrowingCalculator());
		RemoteInvocationException ex = await Assert.ThrowsAsync<RemoteInvocationException>(
			async () => await calculator.SubtractAsync(2, 3, TestContext.Current.CancellationToken));
		TestContext.Current.TestOutputHelper?.WriteLine(ex.ToString());
		Assert.Null(ex.InnerException);
	}

	[Fact]
	public async Task TaskOfT_ThrowsRemoteInvocationExceptionWithInnerException()
	{
		ICalculator calculator = CalculatorDescriptor
			.WithExceptionStrategy(ExceptionProcessing.ISerializable)
			.ConstructLocalProxy<ICalculator>(new ThrowingCalculator());
		RemoteInvocationException ex = await Assert.ThrowsAsync<RemoteInvocationException>(
			async () => await calculator.SubtractAsync(2, 3, TestContext.Current.CancellationToken));
		TestContext.Current.TestOutputHelper?.WriteLine(ex.ToString());
		Assert.IsType<InvalidOperationException>(ex.GetBaseException());
	}

	[Fact]
	public void Is_SimpleCalculator()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new GoodCalculator());
		IClientProxy proxy = (IClientProxy)calculator;

		Assert.True(proxy.Is(typeof(ICalculator)));
		Assert.True(proxy.Is(typeof(IDisposable)));
		Assert.True(proxy.Is(typeof(IClientProxy)));
		Assert.True(proxy.Is(typeof(IClientProxy)));

		Assert.False(proxy.Is(typeof(ICancellationStrategy)));
	}

	[Fact]
	public void Is_MultifunctionCalculator()
	{
		ICalculator calculator = CalculatorDescriptor
			.ConstructLocalProxy<IMultifunctionCalculator>(new GoodCalculator());
		IClientProxy proxy = (IClientProxy)calculator;

		Assert.True(proxy.Is(typeof(ICalculator)));
		Assert.True(proxy.Is(typeof(IMultifunctionCalculator)));
		Assert.False(proxy.Is(typeof(IScientificCalculator)));
	}

	[Fact]
	public void Is_ScientificCalculator()
	{
		ICalculator calculator = CalculatorDescriptor
			.WithAdditionalServiceInterfaces([typeof(IScientificCalculator)])
			.ConstructLocalProxy<IMultifunctionCalculator>(new GoodCalculator());
		IClientProxy proxy = (IClientProxy)calculator;

		Assert.True(proxy.Is(typeof(ICalculator)));
		Assert.True(proxy.Is(typeof(IMultifunctionCalculator)));
		Assert.True(proxy.Is(typeof(IScientificCalculator)));
	}

	[Fact]
	public void ConstructLocalProxy_ReturnsThisForCompatibleInterface()
	{
		ICalculator calculator = CalculatorDescriptor.ConstructLocalProxy<ICalculator>(new GoodCalculator());
		Assert.Same(calculator, ((IJsonRpcLocalProxy)calculator).ConstructLocalProxy<ICalculator>());
	}

	[Fact]
	public void TargetHasInsufficientInterfaces()
	{
		ICalculator calculator = new ThrowingCalculator();
		Assert.IsNotAssignableFrom<IScientificCalculator>(calculator); // assumption check for this test to be effective.
		Assert.Throws<InvalidCastException>(() => CalculatorDescriptor
			.WithAdditionalServiceInterfaces([typeof(IScientificCalculator)])
			.ConstructLocalProxy(calculator));
	}

	internal class GoodCalculator : ICalculator, IDisposable, IScientificCalculator
	{
		public int DisposedCounter { get; private set; }

		public ValueTask<int> AddAsync(int a, int b, CancellationToken cancellationToken) => new(a + b);

		public Task<int> SubtractAsync(int a, int b, CancellationToken cancellationToken) => Task.FromResult(a - b);

		public ValueTask<double> GetPiAsync() => new(3.14);

		public void Dispose() => this.DisposedCounter++;
	}

	internal class ThrowingCalculator : ICalculator
	{
		public ValueTask<int> AddAsync(int a, int b, CancellationToken cancellationToken) => new(Task.FromException<int>(new InvalidOperationException()));

		public Task<int> SubtractAsync(int a, int b, CancellationToken cancellationToken) => Task.FromException<int>(new InvalidOperationException());
	}
}
