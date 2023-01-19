// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

namespace ServiceBrokerTest;

internal class ServiceBroker : IServiceBroker
{
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	public Task FireAvailabilityChangedAsync(CancellationToken cancellationToken = default)
	{
		var eventArgs = new BrokeredServicesChangedEventArgs(ImmutableHashSet.Create(new ServiceMoniker("ChangedService")), false);
		this.AvailabilityChanged?.Invoke(this, eventArgs);
		return Task.CompletedTask;
	}

	public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		where T : class
	{
		if (serviceDescriptor.ClientInterface is object && options.ClientRpcTarget is null)
		{
			throw new Exception("Must provide a client for callback service");
		}

		if (serviceDescriptor.Moniker.Name == "Calculator")
		{
			return new ValueTask<T?>((T)(object)new Calculator());
		}
		else if (serviceDescriptor.Moniker.Name == "Callback")
		{
			return new ValueTask<T?>((T)(object)new CallMeBackService(options));
		}
		else if (serviceDescriptor.Moniker.Name == "ActivationService")
		{
			return new ValueTask<T?>((T)(object)new ActivationService(options));
		}
		else
		{
			throw new Exception($"Unknown moniker: {serviceDescriptor.Moniker.Name}");
		}
	}

	public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		ServiceRpcDescriptor descriptor;
		Func<object> factory;
		if (serviceMoniker.Name == "Calculator")
		{
			descriptor = new ServiceJsonRpcDescriptor(serviceMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
			factory = () => new Calculator();
		}
		else if (serviceMoniker.Name == "CalculatorUtf8BE32")
		{
			descriptor = new ServiceJsonRpcDescriptor(serviceMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null);
			factory = () => new Calculator();
		}
		else if (serviceMoniker.Name == "CalculatorMsgPackBE32")
		{
			descriptor = new ServiceJsonRpcDescriptor(serviceMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null);
			factory = () => new Calculator();
		}
		else if (serviceMoniker.Name == "Callback")
		{
			descriptor = new ServiceJsonRpcDescriptor(serviceMoniker, clientInterface: typeof(ICallMeBackClient), ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
			factory = () => new CallMeBackService(options);
		}
		else if (serviceMoniker.Name == "ActivationService")
		{
			descriptor = new ServiceJsonRpcDescriptor(serviceMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
			factory = () => new ActivationService(options);
		}
		else
		{
			throw new Exception($"Unknown moniker: {serviceMoniker.Name}");
		}

		(IDuplexPipe, IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		if (descriptor is ServiceJsonRpcDescriptor serviceJsonRpcDescriptor)
		{
#pragma warning disable CS0618 // Type or member is obsolete
			descriptor = serviceJsonRpcDescriptor.WithMultiplexingStream(options.MultiplexingStream);
#pragma warning restore CS0618 // Type or member is obsolete
		}

		ServiceRpcDescriptor.RpcConnection connection = descriptor.ConstructRpcConnection(pair.Item1);
		if (descriptor.ClientInterface is object)
		{
			options.ClientRpcTarget = connection.ConstructRpcClient(descriptor.ClientInterface);
		}

		connection.AddLocalRpcTarget(factory());
		connection.StartListening();

		return new ValueTask<IDuplexPipe?>(pair.Item2);
	}
}
