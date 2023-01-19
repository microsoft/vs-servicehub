// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("Microsoft.ServiceHub.Analyzers.Tests.AssemblyTestFixture", "Microsoft.ServiceHub.Analyzers.Tests")]

namespace Microsoft.ServiceHub.Analyzers.Tests
{
	public class AssemblyTestFixture : XunitTestFramework
	{
		public AssemblyTestFixture(IMessageSink messageSink)
			: base(messageSink)
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
		}
	}
}
