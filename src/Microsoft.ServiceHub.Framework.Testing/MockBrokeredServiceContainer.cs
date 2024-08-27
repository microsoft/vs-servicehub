// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

namespace Microsoft.ServiceHub.Framework.Testing;

/// <summary>
/// A mock implementation of <see cref="IBrokeredServiceContainer"/> suitable for unit tests.
/// </summary>
/// <remarks>
/// This container does not require advance service registration.
/// When a service is proffered, registration is automatically synthesized if necessary,
/// exposing the service with <see cref="ServiceAudience.Local"/>.
/// </remarks>
public class MockBrokeredServiceContainer : GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MockBrokeredServiceContainer"/> class
	/// with no <see cref="JoinableTaskFactory"/>.
	/// A mock authorization service is installed that approves every request.
	/// </summary>
	/// <param name="traceSource">An optional <see cref="TraceSource"/> to log to.</param>
	public MockBrokeredServiceContainer(TraceSource? traceSource = null)
		: base(ImmutableDictionary<ServiceMoniker, ServiceRegistration>.Empty, isClientOfExclusiveServer: false, joinableTaskFactory: null, traceSource: traceSource ?? CreateNullTraceSource())
	{
		this.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
			{
				{ FrameworkServices.Authorization.Moniker, new ServiceRegistration(ServiceAudience.Local, null, allowGuestClients: true) },
			});
		this.Proffer(FrameworkServices.Authorization, (mk, options, sb, ct) => new(new MockAuthorizationService()));
	}

	/// <inheritdoc />
	public override IReadOnlyDictionary<string, string> LocalUserCredentials => ImmutableDictionary<string, string>.Empty;

	/// <inheritdoc cref="GlobalBrokeredServiceContainer.RegisterServices(IReadOnlyDictionary{ServiceMoniker, ServiceRegistration})"/>
	public new void RegisterServices(IReadOnlyDictionary<ServiceMoniker, ServiceRegistration> services) => base.RegisterServices(services);

	/// <inheritdoc />
	protected override IDisposable Proffer(IProffered proffered)
	{
		Requires.NotNull(proffered);

		this.RegisterServicesIfNecessary(proffered);
		return base.Proffer(proffered);
	}

	private static TraceSource CreateNullTraceSource()
	{
		var traceSource = new TraceSource("Mocks", SourceLevels.Off);
		traceSource.Listeners.Clear(); // remove default listeners
		return traceSource;
	}

	private void RegisterServicesIfNecessary(IProffered proffered)
	{
		var missingRegistrations = new Dictionary<ServiceMoniker, ServiceRegistration>();
		foreach (ServiceMoniker moniker in proffered.Monikers)
		{
			if (!this.RegisteredServices.ContainsKey(moniker))
			{
				missingRegistrations.Add(moniker, new ServiceRegistration(ServiceAudience.Local, null, allowGuestClients: false));
			}
		}

		this.RegisterServices(missingRegistrations);
	}

	private class MockAuthorizationService : IAuthorizationService
	{
		public event EventHandler? CredentialsChanged;

		public event EventHandler? AuthorizationChanged;

		public ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => new(true);

		public ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default) => new(ImmutableDictionary<string, string>.Empty);

		protected virtual void OnCredentialsChanged(EventArgs args) => this.CredentialsChanged?.Invoke(this, args);

		protected virtual void OnAuthorizationChanged(EventArgs args) => this.AuthorizationChanged?.Invoke(this, args);
	}
}
