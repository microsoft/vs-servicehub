// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

public class MockRemoteServiceBroker : IRemoteServiceBroker
{
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	internal Action<Guid>? CancelServiceRequestCallback { get; set; }

	internal Action<ServiceBrokerClientMetadata>? HandshakeCallback { get; set; }

	internal Func<ServiceMoniker, ServiceActivationOptions, RemoteServiceConnectionInfo>? RequestServiceChannelCallback { get; set; }

	public Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		this.CancelServiceRequestCallback?.Invoke(serviceRequestId);
		return Task.CompletedTask;
	}

	public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
	{
		this.HandshakeCallback?.Invoke(clientMetadata);
		return Task.CompletedTask;
	}

	public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(this.RequestServiceChannelCallback?.Invoke(serviceMoniker, options) ?? default);
	}

	internal void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
}
