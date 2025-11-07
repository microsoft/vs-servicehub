// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using PolyType;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes a remotable service broker.
/// </summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IRemoteServiceBroker
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
	event EventHandler<BrokeredServicesChangedEventArgs> AvailabilityChanged;

	/// <summary>
	/// Introduces the client to the server to detail the client's capabilities.
	/// </summary>
	/// <param name="clientMetadata">The environment, capabilities and attributes of a client of the <see cref="IRemoteServiceBroker"/>.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task representing this async call.</returns>
	/// <exception cref="NotSupportedException">
	/// Thrown when this service broker does not support any of the supported service connection kinds that the client offered
	/// in <see cref="ServiceBrokerClientMetadata.SupportedConnections"/>.
	/// </exception>
	Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a pipe to a service.
	/// </summary>
	/// <param name="serviceMoniker">The moniker for the service.</param>
	/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>Instructions for how the client may connect to the service.</returns>
	/// <remarks>
	/// Upon successful completion, resources may have already been allocated for the anticipated connection.
	/// If the connection will not be made (either because the client lost interest or cannot follow the instructions),
	/// the client should call <see cref="CancelServiceRequestAsync(Guid)"/> with the value of
	/// <see cref="RemoteServiceConnectionInfo.RequestId"/> to release the allocated resources.
	/// </remarks>
	Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default);

	/// <summary>
	/// Releases resources allocated as a result of a prior call to <see cref="RequestServiceChannelAsync"/>
	/// when the client cannot or will not complete the connection to the requested service.
	/// </summary>
	/// <param name="serviceRequestId">The value of <see cref="RemoteServiceConnectionInfo.RequestId"/> from the connection instructions that will not be followed.</param>
	/// <returns>A task representing the request to cancel.</returns>
	Task CancelServiceRequestAsync(Guid serviceRequestId);
}
