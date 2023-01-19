// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;
using Microsoft.ServiceHub.Framework.Services;

internal class MockAuthorizationService : IAuthorizationService, IDisposableObservable
{
	private IReadOnlyDictionary<string, string> credentials;

	internal MockAuthorizationService(IReadOnlyDictionary<string, string> credentials)
	{
		this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
	}

	public event EventHandler? CredentialsChanged;

	public event EventHandler? AuthorizationChanged;

	public bool IsDisposed { get; private set; }

	internal int GetCredentialsAsyncInvocationCount { get; private set; }

	internal int CheckAuthorizationAsyncInvocationCount { get; private set; }

	internal List<ProtectedOperation> ApprovedOperations { get; } = new List<ProtectedOperation>();

	public ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken)
	{
		this.CheckAuthorizationAsyncInvocationCount++;
		return new ValueTask<bool>(this.ApprovedOperations.Contains(operation));
	}

	public ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken)
	{
		this.GetCredentialsAsyncInvocationCount++;
		return new ValueTask<IReadOnlyDictionary<string, string>>(this.credentials);
	}

	public void Dispose() => this.IsDisposed = true;

	internal void UpdateCredentials(IReadOnlyDictionary<string, string> credentials)
	{
		this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
		this.CredentialsChanged?.Invoke(this, EventArgs.Empty);
	}

	internal void OnAuthorizationChanged() => this.AuthorizationChanged?.Invoke(this, EventArgs.Empty);
}
