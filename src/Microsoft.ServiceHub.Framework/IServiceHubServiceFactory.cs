// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes a class that can create a ServiceHub service.
/// </summary>
public interface IServiceHubServiceFactory
{
	/// <summary>
	/// Creates an instance of a ServiceHub service asynchronously.
	/// </summary>
	/// <param name="stream">The <see cref="Stream"/> that will be used to communicate with the service.</param>
	/// <param name="hostProvidedServices">Provides other services to the service.</param>
	/// <param name="serviceActivationOptions">The activation options used to start the service.</param>
	/// <param name="serviceBroker">The <see cref="IServiceBroker"/> that can be used to request additional services.</param>
	/// <param name="authorizationServiceClient">The <see cref="AuthorizationServiceClient"/> retrieved from the <see cref="IServiceBroker"/>.</param>
	/// <returns>An instance of a ServiceHub service.</returns>
	Task<object> CreateAsync(
		Stream stream,
		IServiceProvider hostProvidedServices,
		ServiceActivationOptions serviceActivationOptions,
		IServiceBroker serviceBroker,
		AuthorizationServiceClient authorizationServiceClient);
}
