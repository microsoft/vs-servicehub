// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

internal class PipeRemoteServiceBroker : IRemoteServiceBroker
{
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		RemoteServiceConnectionInfo result = default;
		if (serviceMoniker.Name == TestServices.Calculator.Moniker.Name)
		{
			IIpcServer server = ServerFactory.Create(stream =>
			{
				TestServices.Calculator.ConstructRpc(new Calculator(), stream.UsePipe());
				return Task.CompletedTask;
			});
			result.PipeName = server.Name;
		}

		return Task.FromResult(result);
	}

	public Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		throw new NotImplementedException();
	}

	protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
}
