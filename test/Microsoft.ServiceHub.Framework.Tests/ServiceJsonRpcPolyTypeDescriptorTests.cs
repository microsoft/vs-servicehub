// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using PolyType;

public partial class ServiceJsonRpcPolyTypeDescriptorTests
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("Some name");
	private static readonly ServiceMoniker SomeOtherMoniker = new ServiceMoniker("Some other name");
	private static readonly TraceSource SomeTraceSource = new("test");
	private static readonly JoinableTaskFactory SomeJoinableTaskFactory = new JoinableTaskContext().Factory;

	[Fact]
	public void WithDisplayName()
	{
		ServiceJsonRpcPolyTypeDescriptor d = new(SomeMoniker, ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack, ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, Witness.GeneratedTypeShapeProvider);
		Assert.Contains(d.GetType().FullName!, d.DisplayName);
		Assert.Contains(Witness.GeneratedTypeShapeProvider.GetType().FullName!, d.DisplayName);
		TestContext.Current.TestOutputHelper?.WriteLine(d.DisplayName);
		ServiceJsonRpcPolyTypeDescriptor d2 = d.WithExceptionStrategy(StreamJsonRpc.ExceptionProcessing.CommonErrorData);
		Assert.Equal(d.DisplayName, d2.DisplayName);
		ServiceJsonRpcPolyTypeDescriptor d3 = d.WithDisplayName("Custom name");
		Assert.Equal("Custom name", d3.DisplayName);
		ServiceJsonRpcPolyTypeDescriptor d4 = d3.WithExceptionStrategy(StreamJsonRpc.ExceptionProcessing.ISerializable);
		Assert.Equal("Custom name", d4.DisplayName);
	}

	[GenerateShapeFor<bool>]
	private partial class Witness;
}
