// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Xunit;

public class ServiceRpcDescriptorTests
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("Some name");
	private static readonly ServiceMoniker SomeOtherMoniker = new ServiceMoniker("Some other name");

	[Fact]
	public void Ctor_ValidatesInputs()
	{
		Assert.Throws<ArgumentNullException>(() => new MockServiceRpcDescriptor((ServiceMoniker)null!));
		Assert.Throws<ArgumentNullException>(() => new MockServiceRpcDescriptor((MockServiceRpcDescriptor)null!));
	}

	[Fact]
	public void Moniker_Getter()
	{
		var descriptor = new MockServiceRpcDescriptor(SomeMoniker);
		Assert.Same(SomeMoniker, descriptor.Moniker);
	}

	[Fact]
	public void TraceSource_NullByDefault()
	{
		var descriptor = new MockServiceRpcDescriptor(SomeMoniker);
		Assert.Null(descriptor.TraceSource);
	}

	[Fact]
	public void WithTraceSource_ImpactsOnlyNewInstance()
	{
		var descriptor = new MockServiceRpcDescriptor(SomeMoniker);

		var traceSource = new TraceSource("my");
		MockServiceRpcDescriptor descriptor2 = descriptor.WithTraceSource(traceSource);
		Assert.NotSame(descriptor, descriptor2);
		Assert.Same(traceSource, descriptor2.TraceSource);
		Assert.Null(descriptor.TraceSource);
	}

	[Fact]
	public void WithTraceSource_ClonePreservesProperties()
	{
		var descriptor = new MockServiceRpcDescriptor(SomeMoniker);
		MockServiceRpcDescriptor copy = descriptor.WithSomeOtherSetting(true);
		Assert.True(copy.SomeOtherSetting);
		Assert.False(descriptor.SomeOtherSetting);

		var traceSource = new TraceSource("my");
		MockServiceRpcDescriptor copy2 = copy.WithTraceSource(traceSource);
		Assert.Same(traceSource, copy2.TraceSource);

		Assert.True(copy2.SomeOtherSetting);
	}

	[Fact]
	public void WithServiceMoniker_ImpactsOnlyNewInstance()
	{
		var descriptor = new MockServiceRpcDescriptor(SomeMoniker);
		MockServiceRpcDescriptor descriptor2 = descriptor.WithServiceMoniker(SomeOtherMoniker);

		Assert.NotSame(descriptor, descriptor2);
		Assert.Same(SomeOtherMoniker, descriptor2.Moniker);
		Assert.Same(SomeMoniker, descriptor.Moniker);
	}

	[Fact]
	public void WithServiceMoniker_PreservesProperties()
	{
		var descriptor = new MockServiceRpcDescriptor(SomeMoniker);
		MockServiceRpcDescriptor copy = descriptor.WithSomeOtherSetting(true);
		Assert.True(copy.SomeOtherSetting);
		Assert.False(descriptor.SomeOtherSetting);

		MockServiceRpcDescriptor copy2 = copy.WithServiceMoniker(SomeOtherMoniker);
		Assert.Same(SomeOtherMoniker, copy2.Moniker);
		Assert.True(copy2.SomeOtherSetting);
	}

	private class MockServiceRpcDescriptor : ServiceRpcDescriptor
	{
		public MockServiceRpcDescriptor(ServiceMoniker serviceMoniker)
			: base(serviceMoniker, null)
		{
		}

		public MockServiceRpcDescriptor(MockServiceRpcDescriptor copyFrom)
			: base(copyFrom)
		{
			this.SomeOtherSetting = copyFrom.SomeOtherSetting;
		}

		public override string Protocol => throw new NotImplementedException();

		internal bool SomeOtherSetting { get; private set; }

		public override RpcConnection ConstructRpcConnection(IDuplexPipe pipe)
		{
			throw new NotImplementedException();
		}

		public new MockServiceRpcDescriptor WithTraceSource(TraceSource traceSource) => (MockServiceRpcDescriptor)base.WithTraceSource(traceSource);

		public new MockServiceRpcDescriptor WithServiceMoniker(ServiceMoniker moniker) => (MockServiceRpcDescriptor)base.WithServiceMoniker(moniker);

		internal MockServiceRpcDescriptor WithSomeOtherSetting(bool value)
		{
			if (this.SomeOtherSetting == value)
			{
				return this;
			}

			var clone = (MockServiceRpcDescriptor)this.Clone();
			clone.SomeOtherSetting = value;
			return clone;
		}

		protected override ServiceRpcDescriptor Clone() => new MockServiceRpcDescriptor(this);
	}
}
