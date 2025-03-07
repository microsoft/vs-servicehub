// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

public class ServiceJsonRpcDescriptor_RpcProxyTests : ServiceJsonRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcDescriptor_RpcProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	protected override T? CreateProxy<T>(T? target, ServiceJsonRpcDescriptor descriptor)
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
}
