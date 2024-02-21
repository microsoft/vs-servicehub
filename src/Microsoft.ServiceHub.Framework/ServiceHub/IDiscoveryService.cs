// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Interface that all ServiceHub discovery services should implement.
/// </summary>
public interface IDiscoveryService
{
	/// <summary>
	/// Find the location of the configuration file for the given service.
	/// </summary>
	/// <param name="serviceName">The name of the service.</param>
	/// <param name="cancellationToken">A token to signal cancellation.</param>
	/// <returns>The full path to the service's configuration file or null if the service was not found.</returns>
	Task<string> DiscoverServiceAsync(string serviceName, CancellationToken cancellationToken);

	/// <summary>
	/// Find all configuration files for all services provided by this discovery service.
	/// </summary>
	/// <param name="cancellationToken">A token to signal cancellation.</param>
	/// <returns>A collection of tuples containing the name of the service and the full path to the service's configuration file.</returns>
	Task<IEnumerable<(string ServiceName, string FilePath)>> DiscoverAllServicesAsync(CancellationToken cancellationToken);
}
