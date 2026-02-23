// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using StreamJsonRpc;

public class ServiceJsonRpcPolyTypeDescriptor_ProxyTests : ServiceRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcPolyTypeDescriptor_ProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	protected override ServiceRpcDescriptor SomeDescriptor { get; } = new ServiceJsonRpcPolyTypeDescriptor(new ServiceMoniker("SomeMoniker"), clientInterface: null, ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack, ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null, PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests.Default);

	protected override T? CreateProxy<T>(T? target, ServiceRpcDescriptor descriptor)
		where T : class
	{
		return descriptor.ConstructLocalProxy(target);
	}

	protected override ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces)
		=> ((ServiceJsonRpcPolyTypeDescriptor)descriptor).WithAdditionalServiceInterfaces(additionalServiceInterfaces);

	protected override ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy)
		=> ((ServiceJsonRpcPolyTypeDescriptor)descriptor).WithExceptionStrategy(strategy);

	protected override string GetDisplayName(ServiceRpcDescriptor descriptor) => throw new NotSupportedException();
}
