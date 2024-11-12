// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Services and service contracts that provide core infrastructure.
/// </summary>
public static class FrameworkServices
{
	/// <summary>
	/// The descriptor for a remote service broker.
	/// </summary>
	/// <remarks>
	/// This descriptor defines the default protocol used to communicate with an <see cref="IRemoteServiceBroker"/>.
	/// The moniker is irrelevant because this service is not queried for.
	/// </remarks>
	public static readonly ServiceRpcDescriptor RemoteServiceBroker = new CamelCaseTransformingDescriptor(
		new ServiceMoniker(nameof(RemoteServiceBroker)),
		ServiceJsonRpcDescriptor.Formatters.UTF8SystemTextJson,
		ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);

	/// <summary>
	/// The descriptor for the authorization service.
	/// </summary>
	/// <remarks>
	/// This descriptor defines the default protocol used to communicate with an <see cref="IAuthorizationService"/>.
	/// Requests for this service should include client credentials to impersonate a client other than the local process hosting the authorization service.
	/// </remarks>
	public static readonly ServiceRpcDescriptor Authorization = new CamelCaseTransformingDescriptor(
		new ServiceMoniker("Microsoft.ServiceHub.Framework.AuthorizationService"),
		ServiceJsonRpcDescriptor.Formatters.UTF8SystemTextJson,
		ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);

	/// <summary>
	/// The <see cref="ServiceRpcDescriptor"/> for the manifest service which discloses information about services available at a remote source.
	/// </summary>
	/// <remarks>
	/// This descriptor defines the default protocol used to communicate with an <see cref="IBrokeredServiceManifest"/>.
	/// </remarks>
	public static readonly ServiceRpcDescriptor RemoteBrokeredServiceManifest = new CamelCaseTransformingDescriptor(
		new ServiceMoniker("Microsoft.VisualStudio.RemoteBrokeredServiceManifest", new Version(0, 2)),
		ServiceJsonRpcDescriptor.Formatters.UTF8SystemTextJson,
		ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
}
