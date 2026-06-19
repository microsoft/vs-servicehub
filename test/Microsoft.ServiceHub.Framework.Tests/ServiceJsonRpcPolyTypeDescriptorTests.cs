// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable ISB005 // Call WithRpcTargetMetadata on every descriptor.

using Microsoft.ServiceHub.Framework;
using PolyType;
using StreamJsonRpc;

public partial class ServiceJsonRpcPolyTypeDescriptorTests(ITestOutputHelper logger) : TestBase(logger)
{
	private static readonly ServiceMoniker SomeMoniker = new("Some name");

	[Fact]
	public void WithDisplayName()
	{
		ServiceJsonRpcPolyTypeDescriptor d = CreateDescriptor();
		Assert.Contains(d.GetType().FullName!, d.DisplayName);
		Assert.Contains(Witness.GeneratedTypeShapeProvider.GetType().FullName!, d.DisplayName);
		TestContext.Current.TestOutputHelper?.WriteLine(d.DisplayName);
		ServiceJsonRpcPolyTypeDescriptor d2 = d.WithExceptionStrategy(ExceptionProcessing.CommonErrorData);
		Assert.Equal(d.DisplayName, d2.DisplayName);
		ServiceJsonRpcPolyTypeDescriptor d3 = d.WithDisplayName("Custom name");
		Assert.Equal("Custom name", d3.DisplayName);
		ServiceJsonRpcPolyTypeDescriptor d4 = d3.WithExceptionStrategy(ExceptionProcessing.ISerializable);
		Assert.Equal("Custom name", d4.DisplayName);
	}

	[Fact]
	public void WithTypeShapeProvider_UpdatesDisplayName()
	{
		ServiceJsonRpcPolyTypeDescriptor descriptor = CreateDescriptor();
		ITypeShapeProvider typeShapeProvider = new DelegatingTypeShapeProvider(Witness.GeneratedTypeShapeProvider);

		ServiceJsonRpcPolyTypeDescriptor updated = descriptor.WithTypeShapeProvider(typeShapeProvider);

		Assert.NotSame(descriptor, updated);
		Assert.Same(typeShapeProvider, updated.TypeShapeProvider);
		Assert.Contains(typeShapeProvider.GetType().FullName!, updated.DisplayName);
	}

	[Fact]
	public void Ctor_SetsPropertiesWithClone()
	{
		TestServiceJsonRpcPolyTypeDescriptor descriptor = (TestServiceJsonRpcPolyTypeDescriptor)CreateTestDescriptor()
			.WithDisplayName("Custom name");

		TestServiceJsonRpcPolyTypeDescriptor clone = descriptor.CopyWithClone();

		Assert.NotSame(descriptor, clone);
		Assert.Equal("Custom name", clone.DisplayName);
	}

	[Fact]
	public async Task MultiplexingStreamOptionsWork_MessagePack()
	{
		ServiceRpcDescriptor descriptor = CreateDescriptor()
			.WithRpcTargetMetadata(RpcTargetMetadata.FromShape<ITestRpcContract>())
			.WithMultiplexingStream(new() { ProtocolMajorVersion = 3 });

		await this.AssertBackingMultiplexingStreamAsync(descriptor);
	}

	[Fact]
	public async Task MultiplexingStreamDoesNotWorkWithoutOptions()
	{
		ServiceJsonRpcPolyTypeDescriptor descriptor = CreateDescriptor()
			.WithRpcTargetMetadata(RpcTargetMetadata.FromShape<ITestRpcContract>());

		Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => this.AssertBackingMultiplexingStreamAsync(descriptor));

		Assert.IsType<NotSupportedException>(ex.GetBaseException());
		this.Logger.WriteLine(ex.GetBaseException().ToString());
	}

	private static ServiceJsonRpcPolyTypeDescriptor CreateDescriptor() => new(SomeMoniker, ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack, ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, Witness.GeneratedTypeShapeProvider);

	private static TestServiceJsonRpcPolyTypeDescriptor CreateTestDescriptor() => new(SomeMoniker, ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack, ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, Witness.GeneratedTypeShapeProvider);

	[GenerateShapeFor<bool>]
	private partial class Witness;

	private sealed class DelegatingTypeShapeProvider : ITypeShapeProvider
	{
		private readonly ITypeShapeProvider inner;

		internal DelegatingTypeShapeProvider(ITypeShapeProvider inner)
		{
			this.inner = inner;
		}

		public ITypeShape? GetTypeShape(Type type) => this.inner.GetTypeShape(type);
	}

	private sealed class TestServiceJsonRpcPolyTypeDescriptor : ServiceJsonRpcPolyTypeDescriptor
	{
		public TestServiceJsonRpcPolyTypeDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter, ITypeShapeProvider typeShapeProvider)
			: base(serviceMoniker, formatter, messageDelimiter, typeShapeProvider)
		{
		}

		private TestServiceJsonRpcPolyTypeDescriptor(TestServiceJsonRpcPolyTypeDescriptor copy)
			: base(copy)
		{
		}

		public TestServiceJsonRpcPolyTypeDescriptor CopyWithClone() => (TestServiceJsonRpcPolyTypeDescriptor)this.Clone();

		protected override ServiceRpcDescriptor Clone() => new TestServiceJsonRpcPolyTypeDescriptor(this);
	}
}
