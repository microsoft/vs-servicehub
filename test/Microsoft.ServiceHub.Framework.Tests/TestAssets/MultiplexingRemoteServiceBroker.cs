// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;

internal class MultiplexingRemoteServiceBroker : IRemoteServiceBroker
{
	private readonly MultiplexingStream multiplexingStream;

	internal MultiplexingRemoteServiceBroker(MultiplexingStream multiplexingStream)
	{
		this.multiplexingStream = multiplexingStream ?? throw new ArgumentNullException(nameof(multiplexingStream));
	}

	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	internal IDuplexPipe? LastIssuedChannel { get; private set; }

	public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		RemoteServiceConnectionInfo result = default;
		ServiceRpcDescriptor? descriptor = null;
		object? server = null;

		if (serviceMoniker.Name == TestServices.Calculator.Moniker.Name)
		{
			descriptor = TestServices.Calculator;
		}
		else if (serviceMoniker.Name == TestServices.Echo.Moniker.Name)
		{
			descriptor = TestServices.Echo;
		}
		else if (serviceMoniker.Name == TestServices.CallMeBack.Moniker.Name)
		{
			descriptor = TestServices.CallMeBack;
		}

		if (descriptor != null)
		{
			MultiplexingStream.Channel channel = this.multiplexingStream.CreateChannel();
			this.LastIssuedChannel = channel;
			result.RequestId = Guid.NewGuid();
			result.MultiplexingChannelId = channel.QualifiedId.Id;

			// Awaiting for channel acceptance must happen out of band since the client must receive the connection info
			// before they'll ever try to connect to our channel.
			Task.Run(
				async delegate
				{
					await channel.Acceptance;
					ServiceRpcDescriptor.RpcConnection connection = descriptor.ConstructRpcConnection(channel);
					if (descriptor.ClientInterface != null)
					{
						options.ClientRpcTarget = connection.ConstructRpcClient(descriptor.ClientInterface);
					}

					if (serviceMoniker.Name == TestServices.Calculator.Moniker.Name)
					{
						server = new Calculator();
					}
					else if (serviceMoniker.Name == TestServices.Echo.Moniker.Name)
					{
						server = new EchoService(options);
					}
					else if (serviceMoniker.Name == TestServices.CallMeBack.Moniker.Name)
					{
						server = new CallMeBackService(options);
					}

					if (server is null)
					{
						throw new NotSupportedException(serviceMoniker.ToString());
					}

					connection.AddLocalRpcTarget(server);
					connection.StartListening();
				},
				CancellationToken.None).Forget();
		}

		return Task.FromResult(result);
	}

	public Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		throw new NotImplementedException();
	}

	protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
}
