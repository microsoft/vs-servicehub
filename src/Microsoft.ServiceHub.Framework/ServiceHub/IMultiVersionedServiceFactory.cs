// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes a class that can create a ServiceHub service.
/// </summary>
public interface IMultiVersionedServiceFactory
{
	/// <summary>
	/// Creates an instance of a ServiceHub service asynchronously.
	/// </summary>
	/// <param name="hostProvidedServices">Provides other services to the service.</param>
	/// <param name="serviceMoniker">An identifier for a service.</param>
	/// <param name="serviceActivationOptions">The activation options used to start the service.</param>
	/// <param name="serviceBroker">
	/// The <see cref="IServiceBroker"/> that can be used to request additional services.
	/// </param>
	/// <param name="authorizationServiceClient">
	/// The <see cref="AuthorizationServiceClient"/> retrieved from the <see cref="IServiceBroker"/>.
	/// </param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <remarks>
	/// <para>
	/// Uses <see cref="IMultiVersionedServiceFactory.GetServiceDescriptor"/> to establishes an RPC connection over an <see cref="System.IO.Pipelines.IDuplexPipe"/>.
	/// Adds ServiceHub service object to receive RPC calls and begins listening for incoming messages. The service will only be disposed if the <see cref="ServiceRpcDescriptor"/> does on disconnection.
	/// </para>
	/// </remarks>
	/// <returns>An instance of a ServiceHub service that implements.</returns>
	Task<object> CreateAsync(
		IServiceProvider hostProvidedServices,
		ServiceMoniker serviceMoniker,
		ServiceActivationOptions serviceActivationOptions,
		IServiceBroker serviceBroker,
		AuthorizationServiceClient authorizationServiceClient,
		CancellationToken cancellationToken);

	/// <summary>
	/// Gets the description of a service.
	/// </summary>
	/// <param name="serviceMoniker">An identifier for a service.</param>
	/// <returns>An instance of a <see cref="ServiceRpcDescriptor"/>.</returns>
	ServiceRpcDescriptor GetServiceDescriptor(ServiceMoniker serviceMoniker);
}
