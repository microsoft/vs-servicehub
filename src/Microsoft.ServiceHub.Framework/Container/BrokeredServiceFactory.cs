// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// A delegate that creates new instances of a service to be exposed by an <see cref="IServiceBroker" />.
/// </summary>
/// <param name="moniker">The identifier for the service that is requested.</param>
/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor.</param>
/// <param name="serviceBroker">The service broker that the service returned from this delegate should use to obtain any of its own dependencies.</param>
/// <param name="cancellationToken">A token to indicate that the caller has lost interest in the result.</param>
/// <returns>A unique instance of the service. If the value implements <see cref="IDisposable" />, the value will be disposed when the client disconnects.</returns>
/// <seealso cref="IBrokeredServiceContainer.Proffer(ServiceRpcDescriptor, BrokeredServiceFactory)"/>
public delegate ValueTask<object?> BrokeredServiceFactory(ServiceMoniker moniker, ServiceActivationOptions options, IServiceBroker serviceBroker, CancellationToken cancellationToken);

/// <summary>
/// A delegate that creates new instances of a service to be exposed by an <see cref="IServiceBroker" />.
/// </summary>
/// <param name="moniker">The identifier for the service that is requested.</param>
/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor.</param>
/// <param name="serviceBroker">The service broker that the service returned from this delegate should use to obtain any of its own dependencies.</param>
/// <param name="authorizationServiceClient">
/// The authorization service for this brokered service to use.
/// Must be disposed of by the service or the service factory, unless the service factory itself throws an exception.
/// </param>
/// <param name="cancellationToken">A token to indicate that the caller has lost interest in the result.</param>
/// <returns>A unique instance of the service. If the value implements <see cref="IDisposable" />, the value will be disposed when the client disconnects.</returns>
/// <seealso cref="IBrokeredServiceContainer.Proffer(ServiceRpcDescriptor, AuthorizingBrokeredServiceFactory)"/>
public delegate ValueTask<object?> AuthorizingBrokeredServiceFactory(ServiceMoniker moniker, ServiceActivationOptions options, IServiceBroker serviceBroker, AuthorizationServiceClient authorizationServiceClient, CancellationToken cancellationToken);
