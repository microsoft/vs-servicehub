// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Xunit.Abstractions;

public class RemoteServiceBrokerFrameworkDescriptorTests : RpcTestBase<IRemoteServiceBroker, MockRemoteServiceBroker>
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("Some", new Version(1, 0));

	public RemoteServiceBrokerFrameworkDescriptorTests(ITestOutputHelper logger)
		: base(logger, FrameworkServices.RemoteServiceBroker)
	{
	}

	/// <summary>
	/// Pins the wire protocol for the remote service broker since changes would be a breaking change.
	/// </summary>
	[Fact]
	public void FormatterAndDelimiter()
	{
		ServiceJsonRpcDescriptor descriptor = Assert.IsAssignableFrom<ServiceJsonRpcDescriptor>(FrameworkServices.RemoteServiceBroker);
		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.UTF8SystemTextJson, descriptor.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, descriptor.MessageDelimiter);
	}

	[Fact]
	public async Task CancelRequest()
	{
		var expected = Guid.NewGuid();
		var actualSource = new TaskCompletionSource<Guid>();
		this.Service.CancelServiceRequestCallback = arg => actualSource.SetResult(arg);
		await this.ClientProxy.CancelServiceRequestAsync(expected).WithCancellation(this.TimeoutToken);
		Guid actual = await actualSource.Task.WithCancellation(this.TimeoutToken);
		Assert.Equal(expected, actual);
	}

	[Fact]
	public async Task Handshake()
	{
		var expected = new ServiceBrokerClientMetadata
		{
			LocalServiceHost = new ServiceHostInformation
			{
				OperatingSystem = ServiceHostOperatingSystem.Linux,
				OperatingSystemVersion = new Version(1, 1),
				ProcessArchitecture = System.Runtime.InteropServices.Architecture.X64,
				Runtime = ServiceHostRuntime.NETCore,
				RuntimeVersion = new Version(2, 1),
			},
			SupportedConnections = RemoteServiceConnections.Multiplexing | RemoteServiceConnections.IpcPipe,
		};
		var actualSource = new TaskCompletionSource<ServiceBrokerClientMetadata>();
		this.Service.HandshakeCallback = arg => actualSource.SetResult(arg);
		await this.ClientProxy.HandshakeAsync(expected, this.TimeoutToken);
		ServiceBrokerClientMetadata actual = await actualSource.Task.WithCancellation(this.TimeoutToken);
		Assert.Equal(expected.SupportedConnections, actual.SupportedConnections);
		Assert.Equal(expected.LocalServiceHost.OperatingSystem, actual.LocalServiceHost.OperatingSystem);
		Assert.Equal(expected.LocalServiceHost.OperatingSystemVersion, actual.LocalServiceHost.OperatingSystemVersion);
		Assert.Equal(expected.LocalServiceHost.ProcessArchitecture, actual.LocalServiceHost.ProcessArchitecture);
		Assert.Equal(expected.LocalServiceHost.Runtime, actual.LocalServiceHost.Runtime);
		Assert.Equal(expected.LocalServiceHost.RuntimeVersion, actual.LocalServiceHost.RuntimeVersion);
	}

	[Fact]
	public async Task AvailabilityChanged()
	{
		var capturedArgsSource = new TaskCompletionSource<BrokeredServicesChangedEventArgs>();
		this.ClientProxy.AvailabilityChanged += (sender, args) => capturedArgsSource.SetResult(args);
		var expectedArgs = new BrokeredServicesChangedEventArgs(ImmutableHashSet.Create(SomeMoniker), otherServicesImpacted: true);
		this.Service.OnAvailabilityChanged(expectedArgs);
		BrokeredServicesChangedEventArgs capturedArgs = await capturedArgsSource.Task.WithCancellation(this.TimeoutToken);
		Assert.Equal<ServiceMoniker>(expectedArgs.ImpactedServices, capturedArgs.ImpactedServices);
		Assert.Equal(expectedArgs.OtherServicesImpacted, capturedArgs.OtherServicesImpacted);
	}
}
