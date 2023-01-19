// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using IPC = System.IO.Pipes;

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
			result.PipeName = Guid.NewGuid().ToString("N");
			var serverStream = new IPC.NamedPipeServerStream(result.PipeName, IPC.PipeDirection.InOut, -1, IPC.PipeTransmissionMode.Byte, IPC.PipeOptions.Asynchronous);
			Task.Run(async delegate
			{
				await serverStream.WaitForConnectionAsync();
				TestServices.Calculator.ConstructRpc(new Calculator(), serverStream.UsePipe());
			}).Forget();
		}

		return Task.FromResult(result);
	}

	public Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		throw new NotImplementedException();
	}

	protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
}
