// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An authorization service that waits until it is used before it is created. This is useful because most ServiceHub services do not use their AuthorizationService and so resources are wasted acquiring one.
/// </summary>
internal class LazyAuthorizationServiceProxy : IAuthorizationService, IDisposable
{
	private readonly CancellationTokenSource disposalToken = new();
	private readonly AsyncLazy<IAuthorizationService> authorizationService;

	/// <summary>
	/// Initializes a new instance of the <see cref="LazyAuthorizationServiceProxy"/> class.
	/// </summary>
	/// <param name="serviceBroker">A service broker used to acquire the activation service.</param>
	/// <param name="joinableTaskFactory">An optional <see cref="JoinableTaskFactory"/> to use when scheduling async work, to avoid deadlocks in an application with a main thread.</param>
#if SHIPPING
	[RequiresUnreferencedCode(Reasons.Formatters)]
	[RequiresDynamicCode(Reasons.Formatters)]
#endif
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

#if SHIPPING
	[RequiresUnreferencedCode(Reasons.Formatters)]
	[RequiresDynamicCode(Reasons.Formatters)]
#endif
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
