// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETFRAMEWORK
using System.Net;
#endif
using Microsoft.ServiceHub.Analyzers.Tests;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestFramework(typeof(AssemblyTestFixture))]

namespace Microsoft.ServiceHub.Analyzers.Tests
{
	public class AssemblyTestFixture : XunitTestFramework
	{
		public AssemblyTestFixture()
		{
#if NETFRAMEWORK
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#endif
		}
	}
}
