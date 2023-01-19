// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers
{
	internal static class Types
	{
		internal static class IServiceBroker
		{
			internal const string FullName = "Microsoft.ServiceHub.Framework.IServiceBroker";

			internal const string GetProxyAsync = "GetProxyAsync";
		}

		internal static class ServiceBrokerExtensions
		{
			internal const string FullName = "Microsoft.ServiceHub.Framework.ServiceBrokerExtensions";
		}

		internal static class ServiceBrokerClient
		{
			internal const string FullName = "Microsoft.ServiceHub.Framework.ServiceBrokerClient";

			internal static class Rental
			{
				internal const string MetadataName = ServiceBrokerClient.FullName + "+Rental`1";
			}
		}
	}
}
