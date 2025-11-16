// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using PolyType;
using StreamJsonRpc;

public class ServiceJsonRpcPolyTypeDescriptor_ProxyTests : ServiceRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcPolyTypeDescriptor_ProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	internal static PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests SourceGenShapes => PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework_Tests.Default;

	protected override ServiceRpcDescriptor SomeDescriptor { get; } = ServiceJsonRpcPolyTypeDescriptor.Create<ISomeService>(
		new ServiceMoniker("SomeMoniker"))
		.WithOptionalServiceRpcContracts([SourceGenShapes.ISomeService2, SourceGenShapes.ISomeServiceDisposable]);

	protected override T? CreateProxy<T>(T? target, ServiceRpcDescriptor descriptor)
		where T : class
	{
		return descriptor.ConstructLocalProxy(target);
	}

	protected override ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces)
	{
		var typedDescriptor = (ServiceJsonRpcPolyTypeDescriptor)descriptor;
		ITypeShape primaryContract = typedDescriptor.ServiceRpcContracts[0];
		if (additionalServiceInterfaces is { } addl)
		{
			ImmutableArray<ITypeShape> newContracts = [primaryContract, .. addl.Select(primaryContract.Provider.GetTypeShapeOrThrow)];
			return typedDescriptor.WithServiceRpcContracts(newContracts);
		}

		return typedDescriptor.WithServiceRpcContracts([primaryContract]);
	}

	protected override ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy)
		=> ((ServiceJsonRpcPolyTypeDescriptor)descriptor).WithExceptionStrategy(strategy);
}
