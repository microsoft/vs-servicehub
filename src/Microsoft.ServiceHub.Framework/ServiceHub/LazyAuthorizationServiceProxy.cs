// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An authorization service that waits until it is used before it is created. This is useful because most ServiceHub services do not use their AuthorizationService and so resources are wasted acquiring one.
/// </summary>
/// <remarks>
/// When first used, this proxy attempts to acquire an <see cref="IAuthorizationService"/> from the provided <see cref="IServiceBroker"/> using <see cref="FrameworkServices.Authorization"/>.
/// If the service broker is <see langword="null"/> or the authorization service is unavailable, this instance falls back to <see cref="DefaultAuthorizationService"/>, which returns <see langword="false"/> from authorization checks and provides no credentials.
/// The selected implementation is cached and used for the lifetime of this proxy.
/// </remarks>
internal class LazyAuthorizationServiceProxy : IAuthorizationService, IDisposable
{
	private readonly CancellationTokenSource disposalToken = new();
	private readonly AsyncLazy<IAuthorizationService> authorizationService;

	/// <summary>
	/// Initializes a new instance of the <see cref="LazyAuthorizationServiceProxy"/> class.
	/// </summary>
	/// <param name="serviceBroker">An optional service broker used to acquire the <see cref="IAuthorizationService"/>.</param>
	/// <param name="joinableTaskFactory">An optional <see cref="JoinableTaskFactory"/> to use when scheduling async work, to avoid deadlocks in an application with a main thread.</param>
	public LazyAuthorizationServiceProxy(IServiceBroker? serviceBroker, JoinableTaskFactory? joinableTaskFactory)
	{
		this.authorizationService = new(() => this.ActivateAsync(serviceBroker), joinableTaskFactory);
	}

	/// <inheritdoc/>
	public event EventHandler? CredentialsChanged;

	/// <inheritdoc/>
	public event EventHandler? AuthorizationChanged;

	private CancellationToken DisposeToken => this.disposalToken.Token;

	/// <inheritdoc/>
	public async ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default)
	{
		IAuthorizationService service = await this.authorizationService.GetValueAsync(cancellationToken).ConfigureAwait(false);
		return await service.CheckAuthorizationAsync(operation, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (!this.DisposeToken.IsCancellationRequested)
		{
			this.disposalToken.Cancel();
			this.authorizationService.DisposeValue();
			this.disposalToken.Dispose();
		}
	}

	/// <inheritdoc/>
	public async ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default)
	{
		IAuthorizationService service = await this.authorizationService.GetValueAsync(cancellationToken).ConfigureAwait(false);
		return await service.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<IAuthorizationService> ActivateAsync(IServiceBroker? serviceBroker)
	{
		this.DisposeToken.ThrowIfCancellationRequested();

		IAuthorizationService? authService =
			serviceBroker is not null
				? await serviceBroker.GetProxyAsync<IAuthorizationService>(FrameworkServices.Authorization, this.DisposeToken).ConfigureAwait(false)
				: null;

		authService ??= new DefaultAuthorizationService();
		authService.CredentialsChanged += this.AuthService_CredentialsChanged;
		authService.AuthorizationChanged += this.AuthService_AuthorizationChanged;

		return authService;
	}

	private void AuthService_AuthorizationChanged(object? sender, EventArgs e) => this.AuthorizationChanged?.Invoke(sender, e);

	private void AuthService_CredentialsChanged(object? sender, EventArgs e) => this.CredentialsChanged?.Invoke(sender, e);
}
