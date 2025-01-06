// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework.Services;

/// <summary>
/// A caching client of the <see cref="IAuthorizationService"/>.
/// </summary>
public class AuthorizationServiceClient : IDisposableObservable
{
	/// <summary>
	/// A value indicating whether <see cref="AuthorizationService"/> should be disposed when this instance is disposed.
	/// </summary>
	private readonly bool ownsAuthorizationService;

	/// <summary>
	/// The set of auth checks that we have responses for.
	/// </summary>
	private readonly Dictionary<string, List<(ProtectedOperation Operation, bool Approved)>> cachedAuthChecks = new Dictionary<string, List<(ProtectedOperation Operation, bool Approved)>>();

	/// <summary>
	/// The default value for <see cref="ServiceActivationOptions.ClientCredentials"/> for all service requests
	/// that do not explicitly provide it.
	/// </summary>
	private AsyncLazy<IReadOnlyDictionary<string, string>> clientCredentials;

	/// <summary>
	/// Initializes a new instance of the <see cref="AuthorizationServiceClient"/> class.
	/// </summary>
	/// <param name="authorizationService">The client proxy of the authorization service that this instance will wrap. This will be disposed (if it implements <see cref="IDisposable"/>) when this <see cref="AuthorizationServiceClient"/> is disposed.</param>
	/// <param name="ownsAuthorizationService"><see langword="true"/> to dispose of <paramref name="authorizationService"/> when this instance is disposed; otherwise <see langword="false"/>.</param>
	public AuthorizationServiceClient(IAuthorizationService authorizationService, bool ownsAuthorizationService = true)
	{
		this.AuthorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
		this.ownsAuthorizationService = ownsAuthorizationService;

		authorizationService.CredentialsChanged += this.AuthorizationService_CredentialsChanged;
		authorizationService.AuthorizationChanged += this.AuthorizationService_AuthorizationChanged;

		this.clientCredentials = this.NewClientCredentials();
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="AuthorizationServiceClient"/> class.
	/// </summary>
	/// <param name="authorizationService">The client proxy of the authorization service that this instance will wrap. This will be disposed (if it implements <see cref="IDisposable"/>) when this <see cref="AuthorizationServiceClient"/> is disposed.</param>
	/// <param name="joinableTaskFactory">A means to avoid deadlocks if the authorization service requires the main thread. May be null.</param>
	/// <param name="ownsAuthorizationService"><see langword="true"/> to dispose of <paramref name="authorizationService"/> when this instance is disposed; otherwise <see langword="false"/>.</param>
	[Obsolete("Use the overload that does not accept a JoinableTaskFactory instead. This overload will be removed in a future release.", error: true)]
	public AuthorizationServiceClient(IAuthorizationService authorizationService, JoinableTaskFactory? joinableTaskFactory, bool ownsAuthorizationService = true)
		: this(authorizationService, ownsAuthorizationService)
	{
	}

	/// <summary>
	/// Gets the authorization service client proxy.
	/// </summary>
	public IAuthorizationService AuthorizationService { get; }

	/// <summary>
	/// Gets a value indicating whether this instance has been disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

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
	public Task<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default)
	{
		Verify.NotDisposed(this);
		return this.clientCredentials.GetValueAsync(cancellationToken);
	}

	/// <summary>
	/// Checks whether a previously authenticated user is authorized to perform some operation.
	/// </summary>
	/// <param name="operation">The operation to be performed.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns><see langword="true"/> if the client is authorized to perform the <paramref name="operation"/>; <see langword="false"/> otherwise.</returns>
	public async Task<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(operation, nameof(operation));
		Verify.NotDisposed(this);

		lock (this.cachedAuthChecks)
		{
			if (this.cachedAuthChecks.TryGetValue(operation.OperationMoniker, out List<(ProtectedOperation Operation, bool Approved)>? results))
			{
				foreach ((ProtectedOperation Operation, bool Approved) result in results)
				{
					// If something greater than our operation is already approved...
					if (result.Approved && result.Operation.IsSupersetOf(operation))
					{
						return true;
					}

					// If something less than our operation is already denied...
					if (!result.Approved && operation.IsSupersetOf(result.Operation))
					{
						return false;
					}
				}
			}
		}

		bool approved = await this.AuthorizationService.CheckAuthorizationAsync(operation, cancellationToken).ConfigureAwait(false);

		lock (this.cachedAuthChecks)
		{
			if (!this.cachedAuthChecks.TryGetValue(operation.OperationMoniker, out List<(ProtectedOperation Operation, bool Approved)>? results))
			{
				this.cachedAuthChecks[operation.OperationMoniker] = results = new List<(ProtectedOperation Operation, bool Approved)>();
			}

			results.Add((operation, approved));
		}

		return approved;
	}

	/// <summary>
	/// Verifies that the previously authenticated user is authorized to perform some operation, or throws an exception.
	/// </summary>
	/// <param name="operation">The operation to check authorization for.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes successfully if the operation is authorized, or faults if not.</returns>
	/// <exception cref="UnauthorizedAccessException">Thrown if the client is not authorized to perform the <paramref name="operation"/>.</exception>
	public async Task AuthorizeOrThrowAsync(ProtectedOperation operation, CancellationToken cancellationToken = default)
	{
		bool authorized = await this.CheckAuthorizationAsync(operation, cancellationToken).ConfigureAwait(false);
		if (!authorized)
		{
			throw new UnauthorizedAccessException();
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (this.IsDisposed!)
		{
			this.AuthorizationService.CredentialsChanged -= this.AuthorizationService_CredentialsChanged;
			this.AuthorizationService.AuthorizationChanged -= this.AuthorizationService_AuthorizationChanged;
			if (this.ownsAuthorizationService)
			{
				(this.AuthorizationService as IDisposable)?.Dispose();
			}

			this.IsDisposed = true;
		}
	}

	private void AuthorizationService_CredentialsChanged(object? sender, EventArgs e) => this.clientCredentials = this.NewClientCredentials();

	private void AuthorizationService_AuthorizationChanged(object? sender, EventArgs e)
	{
		lock (this.cachedAuthChecks)
		{
			this.cachedAuthChecks.Clear();
		}
	}

	private AsyncLazy<IReadOnlyDictionary<string, string>> NewClientCredentials()
	{
		return new AsyncLazy<IReadOnlyDictionary<string, string>>(
			() => this.AuthorizationService.GetCredentialsAsync(CancellationToken.None).AsTask(),
			joinableTaskFactory: null);
	}
}
