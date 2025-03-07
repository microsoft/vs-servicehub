// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Sdk.TestFramework;

public class BrokeredServiceManifestTests : BrokeredServiceContractTestBase<IBrokeredServiceManifest, BrokeredServiceManifestMock>
{
	public BrokeredServiceManifestTests(ITestOutputHelper logger)
		: base(logger, FrameworkServices.RemoteBrokeredServiceManifest)
	{
	}

	[Fact]
	public async Task GetAvailableServicesAsync()
	{
		IReadOnlyCollection<ServiceMoniker> result = await this.ClientProxy.GetAvailableServicesAsync(this.TimeoutToken);
		Assert.Equal(BrokeredServiceManifestMock.GetAvailableServicesExpectedResult, result);
	}

	[Fact]
	public async Task GetAvailableVersionsAsync()
	{
		ImmutableSortedSet<Version?> result = await this.ClientProxy.GetAvailableVersionsAsync("someService", this.TimeoutToken);
		Assert.Equal(BrokeredServiceManifestMock.GetAvailableVersionsExpectedResult, result);
		Assert.Contains(null, result);
	}
}
