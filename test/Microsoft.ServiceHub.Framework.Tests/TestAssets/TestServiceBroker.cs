// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

internal class TestServiceBroker : IServiceBroker
{
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		ServiceRpcDescriptor? descriptor = null;
		object? server = null;

		if (serviceMoniker.Name == TestServices.Calculator.Moniker.Name)
		{
			descriptor = TestServices.Calculator;
			server = new Calculator();
		}
		else if (serviceMoniker.Name == TestServices.Echo.Moniker.Name)
		{
			descriptor = TestServices.Echo;
			server = new EchoService(options);
		}
		else if (serviceMoniker.Name == TestServices.Throws.Moniker.Name)
		{
			throw new ServiceActivationFailedException(serviceMoniker, new Exception("Service factory throws."));
		}

		if (descriptor != null)
		{
			Assumes.NotNull(server);
			(IDuplexPipe, IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
#pragma warning disable CS0618 // Type or member is obsolete
			descriptor
				.WithMultiplexingStream(options.MultiplexingStream)
#pragma warning restore CS0618 // Type or member is obsolete
				.ConstructRpc(server, pair.Item1);
			return new ValueTask<IDuplexPipe?>(pair.Item2);
		}

		return default;
	}

	public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		where T : class
	{
		if (serviceDescriptor.Moniker.Name == TestServices.Calculator.Moniker.Name)
		{
			return new ValueTask<T?>((T)(object)new Calculator());
		}
		else if (serviceDescriptor.Moniker.Name == TestServices.Echo.Moniker.Name)
		{
			return new ValueTask<T?>((T)(object)new EchoService(options));
		}
		else if (serviceDescriptor.Moniker.Name == TestServices.Throws.Moniker.Name)
		{
			throw new ServiceActivationFailedException(serviceDescriptor.Moniker, new Exception("Service factory throws."));
		}

		return default;
	}

	internal void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
}
