// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;
using PolyType;
using StreamJsonRpc;

public class ServiceJsonRpcPolyTypeDescriptor_RpcProxyTests : ServiceRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcPolyTypeDescriptor_RpcProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	internal static PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests SourceGenShapes => PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests.Default;

	protected override ServiceRpcDescriptor SomeDescriptor { get; } = new ServiceJsonRpcPolyTypeDescriptor(
		new ServiceMoniker("SomeMoniker"),
		[SourceGenShapes.ISomeService],
		[])
		.WithOptionalServiceRpcContracts(SourceGenShapes.ISomeService2, SourceGenShapes.ISomeServiceDisposable);

	protected override T? CreateProxy<T>(T? target, ServiceRpcDescriptor descriptor)
		where T : class
	{
		if (target is null)
		{
			return null;
		}

		TraceSource traceSource = new("test") { Switch = { Level = SourceLevels.Information } };
		traceSource.Listeners.Add(new XunitTraceListener(this.Logger));
		descriptor = descriptor.WithTraceSource(traceSource);

		var typedDescriptor = (ServiceJsonRpcPolyTypeDescriptor)descriptor;

		if (!typedDescriptor.ServiceRpcContracts.Any(s => s.Type == typeof(T)))
		{
			typedDescriptor = typedDescriptor.WithServiceRpcContracts([SourceGenShapes.GetTypeShapeOrThrow(typeof(T)), .. typedDescriptor.ServiceRpcContracts]);
		}

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
		typedDescriptor.ConstructRpc(target, pipePair.Item1);
		return typedDescriptor.ConstructRpc<T>(null, pipePair.Item2);
	}

	protected override ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces)
	{
		var typedDescriptor = (ServiceJsonRpcPolyTypeDescriptor)descriptor;
		return additionalServiceInterfaces is { } addl
			? typedDescriptor.WithServiceRpcContracts([typedDescriptor.ServiceRpcContracts[0], .. addl.Select(SourceGenShapes.GetTypeShapeOrThrow)])
			: typedDescriptor.WithServiceRpcContracts([typedDescriptor.ServiceRpcContracts[0]]);
	}

	protected override ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy)
		=> ((ServiceJsonRpcPolyTypeDescriptor)descriptor).WithExceptionStrategy(strategy);
}
