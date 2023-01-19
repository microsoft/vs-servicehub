// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

public class MockServiceBroker : IServiceBroker
{
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
	{
		if (TestServices.Calculator.Moniker.Equals(serviceMoniker))
		{
			(IDuplexPipe, IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
			TestServices.Calculator.ConstructRpc(new Calculator(), pair.Item1);
			return new ValueTask<IDuplexPipe?>(pair.Item2);
		}

		if (TestServices.Throws.Moniker.Equals(serviceMoniker))
		{
			throw new ServiceActivationFailedException(serviceMoniker, new Exception("Service factory throws."));
		}

		return default;
	}

	public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
		where T : class
	{
		throw new NotImplementedException();
	}

	internal void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
}
