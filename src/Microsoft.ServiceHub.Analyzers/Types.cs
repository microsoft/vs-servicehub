// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers;

public static class Types
{
	public static class IServiceBroker
	{
		public const string FullName = "Microsoft.ServiceHub.Framework.IServiceBroker";

		public const string GetProxyAsync = "GetProxyAsync";
	}

	public static class ServiceBrokerExtensions
	{
		public const string FullName = "Microsoft.ServiceHub.Framework.ServiceBrokerExtensions";
	}

	public static class RpcMarshalableOptionalInterfaceAttribute
	{
		public const string FullName = "StreamJsonRpc.RpcMarshalableOptionalInterfaceAttribute";
	}

	public static class JsonRpcProxyAttribute
	{
		public const string FullName = "StreamJsonRpc.JsonRpcProxyAttribute`1";
	}

	public static class JsonRpcProxyInterfaceGroupAttribute
	{
		public const string FullName = "StreamJsonRpc.JsonRpcProxyInterfaceGroupAttribute";
	}

	public static class ServiceBrokerClient
	{
		public const string FullName = "Microsoft.ServiceHub.Framework.ServiceBrokerClient";

		public static class Rental
		{
			public const string MetadataName = ServiceBrokerClient.FullName + "+Rental`1";
		}
	}

	public static class JsonRpcContractAttribute
	{
		public const string FullName = "StreamJsonRpc.JsonRpcContractAttribute";
	}

	public static class ExportRpcContractProxiesAttribute
	{
		public const string Name = "ExportRpcContractProxiesAttribute";

		public const string FullName = $"StreamJsonRpc.{Name}";
	}
}
