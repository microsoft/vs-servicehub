// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Extension methods for the <see cref="IServiceBroker"/> interface and related types.
/// </summary>
public static class ServiceBrokerExtensions
{
	/// <summary>
	/// Requests access to some service through a client proxy.
	/// </summary>
	/// <typeparam name="T">The type of client proxy to create.</typeparam>
	/// <param name="serviceBroker">The service broker.</param>
	/// <param name="serviceDescriptor">An descriptor of the service.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>
	/// The client proxy that may be used to communicate with the service; or <see langword="null"/> if no matching service could be found.
	/// This should be disposed when no longer required if the instance returned implements <see cref="IDisposable"/>.
	/// </returns>
	/// <exception cref="ServiceCompositionException">Thrown when a service discovery or activation error occurs.</exception>
	public static ValueTask<T?> GetProxyAsync<T>(this IServiceBroker serviceBroker, ServiceRpcDescriptor serviceDescriptor, CancellationToken cancellationToken = default)
		where T : class
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		GC.KeepAlive(typeof(ValueTask<T?>)); // workaround CLR bug https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1358442
		return serviceBroker.GetProxyAsync<T>(serviceDescriptor, default(ServiceActivationOptions), cancellationToken);
	}

	/// <inheritdoc cref="GetProxyAsync{T}(IServiceBroker, ServiceRpcDescriptor, CancellationToken)"/>
	public static ValueTask<T?> GetProxyAsync<T>(this IServiceBroker serviceBroker, ServiceJsonRpcDescriptor<T> serviceDescriptor, CancellationToken cancellationToken = default)
		where T : class
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		return serviceBroker.GetProxyAsync<T>(serviceDescriptor, default(ServiceActivationOptions), cancellationToken);
	}

	/// <summary>
	/// Requests access to some service through an <see cref="IDuplexPipe"/>.
	/// </summary>
	/// <param name="serviceBroker">The service broker.</param>
	/// <param name="serviceMoniker">The moniker for the service.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>
	/// The duplex pipe that may be used to communicate with the service; or <see langword="null"/> if no matching service could be found.
	/// This should be disposed when no longer required.
	/// </returns>
	/// <exception cref="ServiceCompositionException">
	/// Thrown when a service discovery or activation error occurs,
	/// or when the only service activation option is local service host activation since this overload
	/// does not accept a <see cref="ServiceRpcDescriptor"/> parameter.
	/// </exception>
	public static ValueTask<IDuplexPipe?> GetPipeAsync(this IServiceBroker serviceBroker, ServiceMoniker serviceMoniker, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		return serviceBroker.GetPipeAsync(serviceMoniker, default(ServiceActivationOptions), cancellationToken);
	}
}
