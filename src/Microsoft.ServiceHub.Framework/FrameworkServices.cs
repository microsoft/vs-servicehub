// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;
using StreamJsonRpc;

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
		ServiceJsonRpcDescriptor.Formatters.UTF8,
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
		ServiceJsonRpcDescriptor.Formatters.UTF8,
		ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);

	/// <summary>
	/// A <see cref="ServiceJsonRpcDescriptor"/> derived type that applies camelCase naming transforms to method and event names
	/// and trims off any trailing "Async" suffix.
	/// </summary>
	private class CamelCaseTransformingDescriptor : ServiceJsonRpcDescriptor
	{
		private const string AsyncSuffix = "Async";
		private static readonly Func<string, string> NameNormalize = name => CommonMethodNameTransforms.CamelCase(name.EndsWith(AsyncSuffix, StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - AsyncSuffix.Length) : name);

		/// <summary>
		/// Initializes a new instance of the <see cref="CamelCaseTransformingDescriptor"/> class.
		/// </summary>
		/// <inheritdoc cref="ServiceJsonRpcDescriptor(ServiceMoniker, Formatters, MessageDelimiters)" />
		public CamelCaseTransformingDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter)
			: base(serviceMoniker, formatter, messageDelimiter)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="CamelCaseTransformingDescriptor"/> class.
		/// </summary>
		/// <inheritdoc cref="ServiceJsonRpcDescriptor(ServiceJsonRpcDescriptor)"/>
		public CamelCaseTransformingDescriptor(CamelCaseTransformingDescriptor copyFrom)
			: base(copyFrom)
		{
		}

		/// <inheritdoc />
		protected override JsonRpcConnection CreateConnection(JsonRpc jsonRpc)
		{
			JsonRpcConnection connection = base.CreateConnection(jsonRpc);
			connection.LocalRpcTargetOptions.MethodNameTransform = NameNormalize;
			connection.LocalRpcTargetOptions.EventNameTransform = NameNormalize;
			connection.LocalRpcProxyOptions.MethodNameTransform = NameNormalize;
			connection.LocalRpcProxyOptions.EventNameTransform = NameNormalize;
			return connection;
		}

		protected override ServiceRpcDescriptor Clone() => new CamelCaseTransformingDescriptor(this);
	}
}
