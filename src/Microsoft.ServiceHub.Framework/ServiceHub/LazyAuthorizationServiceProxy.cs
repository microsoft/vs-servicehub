// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An authorization service that waits until it is used before it is created. This is useful because most ServiceHub services do not use their AuthorizationService and so resources are wasted acquiring one.
/// </summary>
public class LazyAuthorizationServiceProxy : IAuthorizationService, IDisposable
{
	private readonly CancellationTokenSource disposalToken = new();
	private readonly AsyncLazy<IAuthorizationService> authorizationService;

	private bool disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="LazyAuthorizationServiceProxy"/> class.
	/// </summary>
	/// <param name="serviceBroker">A service broker used to acquire the activation service.</param>
	/// <param name="joinableTaskFactory">An optional <see cref="JoinableTaskFactory"/> to use when scheduling async work, to avoid deadlocks in an application with a main thread.</param>
	public LazyAuthorizationServiceProxy(IServiceBroker serviceBroker, JoinableTaskFactory? joinableTaskFactory)
	{
		Requires.NotNull(serviceBroker);
		this.authorizationService = new(() => this.ActivateAsync(serviceBroker), joinableTaskFactory ?? JoinableTaskContext.CreateNoOpContext().Factory);
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
		if (!this.disposed)
		{
			this.disposed = true;
			this.disposalToken.Cancel();

			// If this value hasn't been created yet, the `DisposeToken` will throw right away because it was cancelled
			// and a default authorization service will be returned without going through the service broker to request the actual service proxy.
			IAuthorizationService service = this.authorizationService.GetValue();

			service.CredentialsChanged -= this.AuthService_CredentialsChanged;
			service.AuthorizationChanged -= this.AuthService_AuthorizationChanged;

			(service as IDisposable)?.Dispose();
			this.disposalToken.Dispose();
		}
	}

	/// <inheritdoc/>
	public async ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default)
	{
		IAuthorizationService service = await this.authorizationService.GetValueAsync(cancellationToken).ConfigureAwait(false);
		return await service.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<IAuthorizationService> ActivateAsync(IServiceBroker serviceBroker)
	{
		IAuthorizationService? authService = null;

		try
		{
			this.DisposeToken.ThrowIfCancellationRequested();
			authService = await serviceBroker.GetProxyAsync<IAuthorizationService>(FrameworkServices.Authorization, this.DisposeToken).ConfigureAwait(false);
		}
		catch (Exception e) when (e is ServiceActivationFailedException || e is OperationCanceledException || e is TaskCanceledException)
		{
		}

		authService ??= new DefaultAuthorizationService();
		authService.CredentialsChanged += this.AuthService_CredentialsChanged;
		authService.AuthorizationChanged += this.AuthService_AuthorizationChanged;

		return authService;
	}

	private void AuthService_AuthorizationChanged(object? sender, EventArgs e) => this.AuthorizationChanged?.Invoke(sender, e);

	private void AuthService_CredentialsChanged(object? sender, EventArgs e) => this.CredentialsChanged?.Invoke(sender, e);
}
