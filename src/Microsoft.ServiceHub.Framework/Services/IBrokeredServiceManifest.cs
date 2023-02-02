// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;

namespace Microsoft.ServiceHub.Framework.Services;

/// <summary>
/// Exposes details about availability of services proffered to the client.
/// Obtainable from the <see cref="FrameworkServices.RemoteBrokeredServiceManifest"/> service.
/// </summary>
/// <remarks>
/// The results are based on the caller.
/// For example if an instance of this service is obtained by a Live Share guest
/// the results from method calls may vary from an instance of this service obtained by a Codespaces client.
/// </remarks>
public interface IBrokeredServiceManifest
{
	/// <summary>
	/// Gets the list of services that are available from an <see cref="IServiceBroker"/>.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A collection of service monikers.</returns>
	ValueTask<IReadOnlyCollection<ServiceMoniker>> GetAvailableServicesAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Gets the collection of versions available for the specified service from an <see cref="IServiceBroker"/>.
	/// </summary>
	/// <param name="serviceName">The <see cref="ServiceMoniker.Name"/> from the <see cref="ServiceMoniker"/> for the service to get information about.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>
	/// A collection of versions available for the named service.
	/// A null element may be in the collection if the server may consider a service request without regard to the requested version.
	/// </returns>
	ValueTask<ImmutableSortedSet<Version?>> GetAvailableVersionsAsync(string serviceName, CancellationToken cancellationToken);
}
