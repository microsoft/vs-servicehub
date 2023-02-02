// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// An internal-interface that provides access to more of what <see cref="SVsBrokeredServiceContainer"/> offers.
/// </summary>
public interface IBrokeredServiceContainerInternal : IBrokeredServiceContainer
{
	/// <summary>
	/// Gets credentials to use to impersonate the local user.
	/// </summary>
	IReadOnlyDictionary<string, string> LocalUserCredentials { get; }

	/// <summary>
	/// Gets a service broker that targets an out of proc and/or less trusted consumer.
	/// </summary>
	/// <param name="audience">The architectural position of the consumer.</param>
	/// <param name="clientCredentials">The client credentials to associate with this consumer, if less trusted.</param>
	/// <param name="credentialPolicy">How to apply client credentials to individual service requests.</param>
	/// <returns>The custom <see cref="IServiceBroker"/>.</returns>
	IServiceBroker GetLimitedAccessServiceBroker(ServiceAudience audience, IReadOnlyDictionary<string, string> clientCredentials, ClientCredentialsPolicy credentialPolicy);
}
