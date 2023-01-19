// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework.Services;

/// <summary>
/// Extension methods for the <see cref="IAuthorizationService"/>.
/// </summary>
public static class AuthorizationServiceExtensions
{
	/// <summary>
	/// Verifies that the previously authenticated user is authorized to perform some operation, or throws an exception.
	/// </summary>
	/// <param name="authorizationService">The authorization service.</param>
	/// <param name="operation">The operation to check authorization for.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes successfully if the operation is authorized, or faults if not.</returns>
	/// <exception cref="UnauthorizedAccessException">Thrown if the client is not authorized to perform the <paramref name="operation"/>.</exception>
	public static async Task AuthorizeOrThrowAsync(this IAuthorizationService authorizationService, ProtectedOperation operation, CancellationToken cancellationToken)
	{
		Requires.NotNull(authorizationService, nameof(authorizationService));

		bool authorized = await authorizationService.CheckAuthorizationAsync(operation, cancellationToken).ConfigureAwait(false);
		if (!authorized)
		{
			throw new UnauthorizedAccessException();
		}
	}
}
