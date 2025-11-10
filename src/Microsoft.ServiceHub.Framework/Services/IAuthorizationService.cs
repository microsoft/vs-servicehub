// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using PolyType;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework.Services;

/// <summary>
/// The service contract for the <see cref="FrameworkServices.Authorization"/> service.
/// </summary>
/// <remarks>
/// For improved performance, clients may pass an instance of this interface to <see cref="AuthorizationServiceClient"/> and use that
/// so that queries are locally cached.
/// </remarks>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IAuthorizationService
{
	/// <summary>
	/// Occurs when the credentials previously supplied to this service are at or near expiry.
	/// </summary>
	/// <remarks>
	/// Handlers should request a fresh set of credentials with <see cref="GetCredentialsAsync(CancellationToken)"/>
	/// to keep this service current and to include in future requests for other services.
	/// </remarks>
	event EventHandler CredentialsChanged;

	/// <summary>
	/// Occurs when the client's set of authorized activities has changed.
	/// Clients that have cached previous authorization responses should invalidate the cache.
	/// </summary>
	event EventHandler AuthorizationChanged;

	/// <summary>
	/// Gets the data to include in the <see cref="ServiceActivationOptions.ClientCredentials"/> property of a service request.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A set of credentials.</returns>
	/// <exception cref="UnauthorizedAccessException">Thrown when credentials are not available, are expired beyond recovery, or revoked.</exception>
	/// <remarks>
	/// If this service was created with credentials in <see cref="ServiceActivationOptions.ClientCredentials"/>,
	/// this method will return that same set or perhaps a refreshed set representing the same client.
	/// If this service was created without credentials, credentials are obtained from the identity running the process hosting this service.
	/// </remarks>
	ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Checks whether a previously authenticated user is authorized to perform some operation.
	/// </summary>
	/// <param name="operation">The operation to be performed.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns><see langword="true"/> if the client is authorized to perform the <paramref name="operation"/>; <see langword="false"/> otherwise.</returns>
	ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default);
}
