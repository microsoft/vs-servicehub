// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework.Services;

/// <summary>
/// An authorization service that always returns false for authorization checks, and does not have access to any credentials.
/// </summary>
/// <remarks>
/// This is the service to be used when a service does not have access to an <see cref="IAuthorizationService"/>.
/// </remarks>
internal class DefaultAuthorizationService : IAuthorizationService
{
	/// <inheritdoc/>
	public event EventHandler CredentialsChanged
	{
		add { }
		remove { }
	}

	/// <inheritdoc/>
	public event EventHandler AuthorizationChanged
	{
		add { }
		remove { }
	}

	/// <inheritdoc/>
	public ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default)
	{
		return new ValueTask<bool>(false);
	}

	/// <inheritdoc/>
	public ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default)
	{
		return new ValueTask<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
	}
}
