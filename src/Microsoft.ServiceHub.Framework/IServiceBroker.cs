// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Provides access to arbitrary services by activating them if necessary and returning an object that may be used to communicate with it.
/// </summary>
public interface IServiceBroker
{
	/// <summary>
	/// Occurs when a service previously queried for since the last <see cref="AvailabilityChanged"/> event may have changed availability.
	/// </summary>
	/// <remarks>
	/// Not all service availability changes result in raising this event.
	/// Only those changes that impact services queried for on this <see cref="IServiceBroker"/> instance
	/// will result in an event being raised. Changes already broadcast in a prior event are not included in a subsequent event.
	/// The data included in this event may be a superset of the minimum described here.
	/// </remarks>
	event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	/// <summary>
	/// Requests access to some service through a client proxy.
	/// </summary>
	/// <typeparam name="T">The type of client proxy to create.</typeparam>
	/// <param name="serviceDescriptor">An descriptor of the service.</param>
	/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>
	/// The client proxy that may be used to communicate with the service; or <see langword="null"/> if no matching service could be found.
	/// This should be disposed when no longer required if the instance returned implements <see cref="IDisposable"/>.
	/// </returns>
	/// <exception cref="ServiceCompositionException">Thrown when a service discovery or activation error occurs.</exception>
	ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		where T : class;

	/// <summary>
	/// Requests access to some service through an <see cref="IDuplexPipe"/>.
	/// </summary>
	/// <param name="serviceMoniker">The moniker for the service.</param>
	/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor.</param>
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
	ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default);
}
