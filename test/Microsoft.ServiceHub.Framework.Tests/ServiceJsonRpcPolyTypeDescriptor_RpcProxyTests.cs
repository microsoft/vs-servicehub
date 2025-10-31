// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;
using PolyType;
using PolyType.Abstractions;
using StreamJsonRpc;

public class ServiceJsonRpcPolyTypeDescriptor_RpcProxyTests : ServiceRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcPolyTypeDescriptor_RpcProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	protected override ServiceRpcDescriptor SomeDescriptor { get; } = new ServiceJsonRpcPolyTypeDescriptor(
		new ServiceMoniker("SomeMoniker"),
		clientInterface: null,
		ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
		ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
		multiplexingStreamOptions: null,
		PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests.Default);

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

		ImmutableArray<RpcTargetMetadata>.Builder targetMetadata = ImmutableArray.CreateBuilder<RpcTargetMetadata>(1 + (typedDescriptor.AdditionalServiceInterfaces?.Length ?? 0));
		targetMetadata.Add(RpcTargetMetadata.FromShape(TypeShapeResolver.ResolveDynamicOrThrow<T>()));
		if (typedDescriptor.AdditionalServiceInterfaces is not null)
		{
			foreach (Type additionalInterface in typedDescriptor.AdditionalServiceInterfaces)
			{
				targetMetadata.Add(RpcTargetMetadata.FromShape(typedDescriptor.TypeShapeProvider.GetTypeShapeOrThrow(additionalInterface)));
			}
		}

		typedDescriptor = typedDescriptor.WithRpcTargetMetadata(targetMetadata.MoveToImmutable());

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
		typedDescriptor.ConstructRpc(target, pipePair.Item1);
		return typedDescriptor.ConstructRpc<T>(null, pipePair.Item2);
	}

	protected override ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces)
		=> ((ServiceJsonRpcPolyTypeDescriptor)descriptor).WithAdditionalServiceInterfaces(additionalServiceInterfaces);

	protected override ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy)
		=> ((ServiceJsonRpcPolyTypeDescriptor)descriptor).WithExceptionStrategy(strategy);
}
