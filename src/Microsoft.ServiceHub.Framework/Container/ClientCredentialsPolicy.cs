// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Policies that may apply to how to treat credentials.
/// </summary>
public enum ClientCredentialsPolicy
{
	/// <summary>
	/// If the service request carries client credentials with it, use that instead of what this filter would apply.
	/// </summary>
	RequestOverridesDefault,

	/// <summary>
	/// Always replace the client credentials on a request with the set specified on this filter.
	/// </summary>
	FilterOverridesRequest,
}
