// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

internal static class WellKnownProtectedOperations
{
	/// <summary>
	/// The moniker used to represent a check for whether the client is owned by the same user account that owns the host
	/// and therefore merits full owner trust permissions.
	/// </summary>
	internal const string ClientIsOwner = "ClientIsOwnerOfHost";

	/// <summary>
	/// Creates a new <see cref="ProtectedOperation"/> that represents a <see cref="ClientIsOwner"/> operation.
	/// </summary>
	/// <returns>An instance of <see cref="ProtectedOperation"/> that may be passed to <see cref="IAuthorizationService.CheckAuthorizationAsync(ProtectedOperation, System.Threading.CancellationToken)"/>.</returns>
	internal static ProtectedOperation CreateClientIsOwner() => new ProtectedOperation(ClientIsOwner);
}
