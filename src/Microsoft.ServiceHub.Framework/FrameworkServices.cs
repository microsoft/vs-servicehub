// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable PolyTypeJson

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.ServiceHub.Framework.Services;
using PolyType;
using StreamJsonRpc;

[assembly: ExportRpcContractProxies]

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Services and service contracts that provide core infrastructure.
/// </summary>
public static partial class FrameworkServices
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
		ServiceJsonRpcPolyTypeDescriptor.Formatters.UTF8,
		ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.HttpLikeHeaders,
		Witness.GeneratedTypeShapeProvider).WithRpcTargetMetadata(RpcTargetMetadata.FromShape(SourceGenProvider.IRemoteServiceBroker));

	/// <summary>
	/// The descriptor for the authorization service.
	/// </summary>
	/// <remarks>
	/// This descriptor defines the default protocol used to communicate with an <see cref="IAuthorizationService"/>.
	/// Requests for this service should include client credentials to impersonate a client other than the local process hosting the authorization service.
	/// </remarks>
	public static readonly ServiceRpcDescriptor Authorization = new CamelCaseTransformingDescriptor(
		new ServiceMoniker("Microsoft.ServiceHub.Framework.AuthorizationService"),
		ServiceJsonRpcPolyTypeDescriptor.Formatters.UTF8,
		ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.HttpLikeHeaders,
		Witness.GeneratedTypeShapeProvider).WithRpcTargetMetadata(RpcTargetMetadata.FromShape(SourceGenProvider.IAuthorizationService));

	/// <summary>
	/// The <see cref="ServiceRpcDescriptor"/> for the manifest service which discloses information about services available at a remote source.
	/// </summary>
	/// <remarks>
	/// This descriptor defines the default protocol used to communicate with an <see cref="IBrokeredServiceManifest"/>.
	/// </remarks>
	public static readonly ServiceRpcDescriptor RemoteBrokeredServiceManifest = new CamelCaseTransformingDescriptor(
		new ServiceMoniker("Microsoft.VisualStudio.RemoteBrokeredServiceManifest", new Version(0, 2)),
		ServiceJsonRpcPolyTypeDescriptor.Formatters.UTF8,
		ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.HttpLikeHeaders,
		Witness.GeneratedTypeShapeProvider).WithRpcTargetMetadata(RpcTargetMetadata.FromShape(SourceGenProvider.IBrokeredServiceManifest));

	private static PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework SourceGenProvider => PolyType.SourceGenerator.TypeShapeProvider_Microsoft_ServiceHub_Framework.Default;

	/// <summary>
	/// A <see cref="ServiceJsonRpcPolyTypeDescriptor"/> derived type that applies camelCase naming transforms to method and event names
	/// and trims off any trailing "Async" suffix.
	/// </summary>
	private class CamelCaseTransformingDescriptor : ServiceJsonRpcPolyTypeDescriptor
	{
		private const string AsyncSuffix = "Async";
		private static readonly Func<string, string> NameNormalize = name => CommonMethodNameTransforms.CamelCase(name.EndsWith(AsyncSuffix, StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - AsyncSuffix.Length) : name);

		/// <summary>
		/// Initializes a new instance of the <see cref="CamelCaseTransformingDescriptor"/> class.
		/// </summary>
		/// <inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, Formatters, MessageDelimiters, ITypeShapeProvider)" />
		public CamelCaseTransformingDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter, ITypeShapeProvider typeShapeProvider)
			: base(serviceMoniker, formatter, messageDelimiter, typeShapeProvider)
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
			var formatter = (PolyTypeJsonFormatter)base.CreateFormatter();

			formatter.JsonSerializerOptions.TypeInfoResolver = SourceGenerationContext.Default;

			return formatter;
		}

		protected override ServiceRpcDescriptor Clone() => new CamelCaseTransformingDescriptor(this);
	}

	[GenerateShapeFor<string>]
	private partial class Witness;

	[JsonSerializable(typeof(IReadOnlyCollection<ServiceMoniker>))]
	[JsonSerializable(typeof(ImmutableSortedSet<Version>))]
	[JsonSerializable(typeof(RemoteServiceConnectionInfo))]
	[JsonSerializable(typeof(ServiceActivationOptions))]
	[JsonSerializable(typeof(ServiceBrokerClientMetadata))]
	[JsonSerializable(typeof(BrokeredServicesChangedEventArgs))]
	[JsonSerializable(typeof(Guid))]
	private partial class SourceGenerationContext : JsonSerializerContext;
}
