// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Provides a means to proffer services into <see cref="IServiceBroker"/> and access to the global <see cref="IServiceBroker"/>.
/// </summary>
public interface IBrokeredServiceContainer
{
	/// <summary>
	/// Proffers a service for publication via an <see cref="IServiceBroker"/> associated with this container.
	/// </summary>
	/// <param name="serviceDescriptor">
	/// The descriptor for the service.
	/// The <see cref="ServiceRpcDescriptor.Moniker"/> is used to match service requests to the <paramref name="factory"/>.
	/// The <see cref="ServiceRpcDescriptor.ConstructRpcConnection(System.IO.Pipelines.IDuplexPipe)"/> method is used to convert the service returned by the <paramref name="factory"/> to a pipe when the client prefers that.
	/// </param>
	/// <param name="factory">The delegate that will create new instances of the service for each client.</param>
	/// <returns>A value that can be disposed to remove the proffered service from availability.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="serviceDescriptor"/> represents a <see cref="ServiceMoniker"/> that has already been proffered.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if no registration can be found for the proffered <see cref="ServiceRpcDescriptor.Moniker"/>.
	/// </exception>
	/// <remarks>
	/// The service identified by the <see cref="ServiceRpcDescriptor.Moniker"/> must have been pre-registered
	/// with a <see cref="ServiceAudience"/> indicating who should have access to it and whether it might be obtained from a remote machine or user.
	/// </remarks>
	IDisposable Proffer(ServiceRpcDescriptor serviceDescriptor, BrokeredServiceFactory factory);

	/// <inheritdoc cref="Proffer(ServiceRpcDescriptor, BrokeredServiceFactory)"/>
	IDisposable Proffer(ServiceRpcDescriptor serviceDescriptor, AuthorizingBrokeredServiceFactory factory);

	/// <summary>
	/// Gets an <see cref="IServiceBroker"/> with full access to all services available to this process with local credentials applied by default for all service requests.
	/// This should *not* be used within a brokered service, which should instead use the <see cref="IServiceBroker"/> that is given to its service factory.
	/// </summary>
	/// <returns>An <see cref="IServiceBroker"/> instance created for the caller.</returns>
	/// <remarks>
	/// <para>
	/// When a service request is made with an empty set of <see cref="ServiceActivationOptions.ClientCredentials"/>,
	/// local (full) permissions are applied.
	/// A service request that includes its own client credentials may effectively "reduce" permission levels for the requested service
	/// if the service contains authorization checks. It will not remove a service from availability entirely since the service audience
	/// is always to allow all services to be obtained.
	/// </para>
	/// <para>
	/// Callers should use the <see cref="IServiceBroker"/> they are provided via their <see cref="BrokeredServiceFactory"/> instead of using
	/// this method to get an <see cref="IServiceBroker"/> so that they are secure by default.
	/// An exception to this rule is when a service exposed to untrusted users has fully vetted the input for a specific incoming RPC call
	/// and wishes to request other services with full trust in order to accomplish something the user would otherwise not have permission to do.
	/// This should be done with great care.
	/// </para>
	/// </remarks>
	IServiceBroker GetFullAccessServiceBroker();
}
