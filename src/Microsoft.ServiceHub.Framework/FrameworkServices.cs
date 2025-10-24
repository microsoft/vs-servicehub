// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.ServiceHub.Framework.Services;
using Newtonsoft.Json.Converters;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Services and service contracts that provide core infrastructure.
/// </summary>
[RequiresUnreferencedCode(Reasons.Formatters)]
[RequiresDynamicCode(Reasons.Formatters)]
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

	/// <summary>
	/// A <see cref="ServiceJsonRpcDescriptor"/> derived type that applies camelCase naming transforms to method and event names
	/// and trims off any trailing "Async" suffix.
	/// </summary>
	[RequiresUnreferencedCode(Reasons.Formatters)]
	[RequiresDynamicCode(Reasons.Formatters)]
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
		protected internal override JsonRpcConnection CreateConnection(JsonRpc jsonRpc)
		{
			JsonRpcConnection connection = base.CreateConnection(jsonRpc);
			connection.LocalRpcTargetOptions.MethodNameTransform = NameNormalize;
			connection.LocalRpcTargetOptions.EventNameTransform = NameNormalize;
			connection.LocalRpcProxyOptions.MethodNameTransform = NameNormalize;
			connection.LocalRpcProxyOptions.EventNameTransform = NameNormalize;
			return connection;
		}

		protected internal override IJsonRpcMessageFormatter CreateFormatter()
		{
			IJsonRpcMessageFormatter formatter = base.CreateFormatter();

			// Avoid referencing any MessagePack or Newtonsoft.Json types in this method except when actually taking this code path
			// by pushing such type references to another method. This defers loading assemblies till they're already in use.
			switch (formatter)
			{
				case JsonMessageFormatter jsonFormatter:
					ConfigureJsonFormatter(jsonFormatter);
					break;
				case SystemTextJsonFormatter stjFormatter:
					ConfigureJsonFormatter(stjFormatter);
					break;
				default:
					throw new NotSupportedException("Unsupported formatter type: " + formatter.GetType().FullName);
			}

			return formatter;
		}

		protected override ServiceRpcDescriptor Clone() => new CamelCaseTransformingDescriptor(this);

		private static void ConfigureJsonFormatter(JsonMessageFormatter jsonFormatter)
		{
			jsonFormatter.JsonSerializer.Converters.Add(new VersionConverter());
		}

		private static void ConfigureJsonFormatter(SystemTextJsonFormatter jsonFormatter)
		{
		}
	}
}
