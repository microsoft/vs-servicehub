// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

public class MissingServiceDiagnosticsTests : RpcTestBase<IMissingServiceDiagnosticsService, MockMissingServiceDiagnosticsService>
{
	public MissingServiceDiagnosticsTests(ITestOutputHelper logger)
		: base(logger, GlobalBrokeredServiceContainer.MissingServiceDiagnostics)
	{
	}

	[Fact]
	public async Task RpcTest()
	{
		ServiceMoniker expectedMoniker = new("Test.MissingService", new Version(15, 0));

		MissingServiceAnalysis result = await this.ClientProxy.AnalyzeMissingServiceAsync(expectedMoniker, TestContext.Current.CancellationToken);

		// Verify result was serialized properly.
		Assert.Equal(ServiceSource.TrustedServer, result.ExpectedSource);
		Assert.Equal(MissingBrokeredServiceErrorCode.NotLocallyRegistered, result.ErrorCode);

		// Verify the parameter was serialized properly.
		Assert.Equal(expectedMoniker, this.Service.LastReceivedMoniker);
	}
}
