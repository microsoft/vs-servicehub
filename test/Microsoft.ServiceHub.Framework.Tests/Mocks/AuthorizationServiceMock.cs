// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;

public class AuthorizationServiceMock : IAuthorizationService
{
	public event EventHandler? CredentialsChanged;

	public event EventHandler? AuthorizationChanged;

	internal ProtectedOperation? LastReceivedOperation { get; set; }

	internal IReadOnlyDictionary<string, string>? CredentialsToReturn { get; set; }

	public ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default)
	{
		this.LastReceivedOperation = operation;
		return new(true);
	}

	public ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default)
	{
		return new(this.CredentialsToReturn ?? new Dictionary<string, string>());
	}

	public virtual void OnCredentialsChanged() => this.CredentialsChanged?.Invoke(this, EventArgs.Empty);

	public virtual void OnAuthorizationChanged() => this.AuthorizationChanged?.Invoke(this, EventArgs.Empty);
}
