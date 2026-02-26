// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Testing;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Utilities.ServiceBroker;
using Xunit;

public class ChaosMonkeyTests : TestBase
{
	private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcDescriptor(
		new ServiceMoniker("TestService", new Version(1, 0)),
		ServiceJsonRpcDescriptor.Formatters.MessagePack,
		ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);

	private readonly ChaosMonkeyMockContainer container;

	public ChaosMonkeyTests(ITestOutputHelper logger)
		: base(logger)
	{
		this.container = new ChaosMonkeyMockContainer(
			new TraceSource("Test") { Switch = { Level = SourceLevels.All }, Listeners = { new XunitTraceListener(logger) } });
	}

	private interface IMockService : IDisposable
	{
	}

	[Fact]
	public async Task AllowAll_LocalConsumer_ServiceIsAvailable()
	{
		this.RegisterAndProffer();
		await this.ApplyChaosConfigAsync("allowAll");

		IServiceBroker broker = this.container.GetFullAccessServiceBroker();
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	[Fact]
	public async Task DenyAll_LocalConsumer_ServiceIsDenied()
	{
		this.RegisterAndProffer();
		await this.ApplyChaosConfigAsync("denyAll");

		IServiceBroker broker = this.container.GetFullAccessServiceBroker();
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.Null(proxy);
	}

	[Fact]
	public async Task DenyRemote_LocalConsumer_LocalService_ServiceIsAvailable()
	{
		// DenyRemote only blocks services fulfilled by a remote provider, not locally proffered ones.
		this.RegisterAndProffer();
		await this.ApplyChaosConfigAsync("denyRemote");

		IServiceBroker broker = this.container.GetFullAccessServiceBroker();
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	[Fact]
	public async Task DenyFromRemote_LocalConsumer_ServiceIsAvailable()
	{
		// GetProxyAsync is a local code path (isRemoteRequest=false), so DenyFromRemote allows it.
		// AllClientsIncludingGuests ensures the audience isn't what's allowing the request.
		this.RegisterAndProffer(ServiceAudience.AllClientsIncludingGuests);
		await this.ApplyChaosConfigAsync("denyFromRemote");

		IServiceBroker broker = this.container.GetFullAccessServiceBroker();
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	[Fact]
	public async Task DenyFromRemote_RemoteConsumer_ServiceIsDenied()
	{
		// RequestServiceChannelAsync is the only code path that sets isRemoteRequest=true.
		this.RegisterAndProffer(ServiceAudience.AllClientsIncludingGuests);
		await this.ApplyChaosConfigAsync("denyFromRemote");

		IRemoteServiceBroker remoteBroker = this.container.GetLimitedAccessRemoteServiceBroker(
			ServiceAudience.LiveShareGuest,
			ImmutableDictionary<string, string>.Empty,
			ClientCredentialsPolicy.RequestOverridesDefault);
		await remoteBroker.HandshakeAsync(
			new ServiceBrokerClientMetadata { SupportedConnections = RemoteServiceConnections.IpcPipe },
			this.TimeoutToken);
		RemoteServiceConnectionInfo connectionInfo = await remoteBroker.RequestServiceChannelAsync(
			Descriptor.Moniker,
			default,
			this.TimeoutToken);
		Assert.True(connectionInfo.IsEmpty, "Remote request should have been denied by DenyFromRemote chaos config.");
	}

	[Fact]
	public async Task DenyFromRemote_LimitedAccessLocalConsumer_ServiceIsAvailable()
	{
		// Same LiveShareGuest audience as the remote test, but via GetProxyAsync (isRemoteRequest=false).
		// Proves it's the entry point, not the audience, that triggers DenyFromRemote.
		this.RegisterAndProffer(ServiceAudience.AllClientsIncludingGuests);
		await this.ApplyChaosConfigAsync("denyFromRemote");

		IServiceBroker broker = this.container.GetLimitedAccessServiceBroker(
			ServiceAudience.LiveShareGuest,
			ImmutableDictionary<string, string>.Empty,
			ClientCredentialsPolicy.RequestOverridesDefault);
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	[Fact]
	public async Task DenyAll_RemoteConsumer_ServiceIsDenied()
	{
		// Exercises the remote entry point to confirm DenyAll blocks it too.
		this.RegisterAndProffer(ServiceAudience.AllClientsIncludingGuests);
		await this.ApplyChaosConfigAsync("denyAll");

		IRemoteServiceBroker remoteBroker = this.container.GetLimitedAccessRemoteServiceBroker(
			ServiceAudience.LiveShareGuest,
			ImmutableDictionary<string, string>.Empty,
			ClientCredentialsPolicy.RequestOverridesDefault);
		await remoteBroker.HandshakeAsync(
			new ServiceBrokerClientMetadata { SupportedConnections = RemoteServiceConnections.IpcPipe },
			this.TimeoutToken);
		RemoteServiceConnectionInfo connectionInfo = await remoteBroker.RequestServiceChannelAsync(
			Descriptor.Moniker,
			default,
			this.TimeoutToken);
		Assert.True(connectionInfo.IsEmpty, "Remote request should have been denied by DenyAll chaos config.");
	}

	[Fact]
	public async Task NoChaosConfig_LocalConsumer_ServiceIsAvailable()
	{
		this.RegisterAndProffer();

		IServiceBroker broker = this.container.GetFullAccessServiceBroker();
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	[Fact]
	public async Task NoChaosConfig_RemoteConsumer_ServiceIsAvailable()
	{
		// Uses GetProxyAsync (not RequestServiceChannelAsync), so isRemoteRequest=false.
		this.RegisterAndProffer(ServiceAudience.AllClientsIncludingGuests);

		IServiceBroker broker = this.container.GetLimitedAccessServiceBroker(
			ServiceAudience.LiveShareGuest,
			ImmutableDictionary<string, string>.Empty,
			ClientCredentialsPolicy.RequestOverridesDefault);
		using IMockService? proxy = await broker.GetProxyAsync<IMockService>(Descriptor, this.TimeoutToken);
		Assert.NotNull(proxy);
	}

	private void RegisterAndProffer(ServiceAudience audience = ServiceAudience.Local)
	{
		this.container.RegisterServices(new Dictionary<ServiceMoniker, ServiceRegistration>
		{
			{ Descriptor.Moniker, new ServiceRegistration(audience, null, allowGuestClients: audience.HasFlag(ServiceAudience.LiveShareGuest)) },
		});

		this.container.Proffer(Descriptor, (mk, options, sb, ct) => new ValueTask<object?>(new MockService()));
	}

	private async Task ApplyChaosConfigAsync(string availability)
	{
		string json = $$"""
			{
			  "brokeredServices": {
			    "{{Descriptor.Moniker.Name}}/{{Descriptor.Moniker.Version}}": {
			      "availability": "{{availability}}"
			    }
			  }
			}
			""";
		string configPath = Path.GetTempFileName();
		File.WriteAllText(configPath, json);

		try
		{
			await this.container.ApplyChaosConfigAsync(configPath, this.TimeoutToken);
		}
		finally
		{
			File.Delete(configPath);
		}
	}

	private class MockService : IMockService
	{
		public void Dispose()
		{
		}
	}

	/// <summary>
	/// A test subclass that exposes <see cref="GlobalBrokeredServiceContainer.ApplyChaosMonkeyConfigurationAsync"/> for testing.
	/// </summary>
	private class ChaosMonkeyMockContainer : MockBrokeredServiceContainer
	{
		internal ChaosMonkeyMockContainer(TraceSource traceSource)
			: base(traceSource)
		{
		}

#pragma warning disable CS0618 // Type or member is obsolete
		internal Task ApplyChaosConfigAsync(string path, CancellationToken cancellationToken) =>
			this.ApplyChaosMonkeyConfigurationAsync(path, cancellationToken);
#pragma warning restore CS0618
	}
}
