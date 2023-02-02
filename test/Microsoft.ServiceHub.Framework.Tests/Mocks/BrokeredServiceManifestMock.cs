// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;

public class BrokeredServiceManifestMock : IBrokeredServiceManifest
{
	internal static readonly IReadOnlyCollection<ServiceMoniker> GetAvailableServicesExpectedResult = ImmutableList.Create(
		new ServiceMoniker("Calc", new Version(1, 0)),
		new ServiceMoniker("Calc", new Version(1, 1)),
		new ServiceMoniker("Prod"));

	internal static readonly ImmutableSortedSet<Version?> GetAvailableVersionsExpectedResult = ImmutableSortedSet.Create(
		new Version(1, 0),
		new Version(1, 2, 3, 4),
		null);

	public ValueTask<IReadOnlyCollection<ServiceMoniker>> GetAvailableServicesAsync(CancellationToken cancellationToken)
	{
		return new ValueTask<IReadOnlyCollection<ServiceMoniker>>(GetAvailableServicesExpectedResult);
	}

	public ValueTask<ImmutableSortedSet<Version?>> GetAvailableVersionsAsync(string serviceName, CancellationToken cancellationToken)
	{
		return new ValueTask<ImmutableSortedSet<Version?>>(GetAvailableVersionsExpectedResult);
	}
}
