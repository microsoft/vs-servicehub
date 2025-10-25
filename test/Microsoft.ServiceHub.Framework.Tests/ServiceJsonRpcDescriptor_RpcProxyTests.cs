// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;
using StreamJsonRpc;

public class ServiceJsonRpcDescriptor_RpcProxyTests : ServiceRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcDescriptor_RpcProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	protected override ServiceRpcDescriptor SomeDescriptor { get; } = new ServiceJsonRpcDescriptor(new ServiceMoniker("SomeMoniker"), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);

	protected override T? CreateProxy<T>(T? target, ServiceRpcDescriptor descriptor)
		where T : class
	{
		if (target is null)
		{
			return null;
		}

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
		descriptor.ConstructRpc(target, pipePair.Item1);
		return descriptor.ConstructRpc<T>(null, pipePair.Item2);
	}

	protected override ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces)
		=> ((ServiceJsonRpcDescriptor)descriptor).WithAdditionalServiceInterfaces(additionalServiceInterfaces);

	protected override ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy)
		=> ((ServiceJsonRpcDescriptor)descriptor).WithExceptionStrategy(strategy);
}
