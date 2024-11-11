﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json.Converters;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A <see cref="ServiceJsonRpcDescriptor"/> derived type that applies camelCase naming transforms to method and event names
/// and trims off any trailing "Async" suffix.
/// </summary>
public class NodeToManagedServiceJsonRpcDescriptor : ServiceJsonRpcDescriptor
{
	private const string AsyncSuffix = "Async";
	private static readonly Func<string, string> NameNormalize = name => CommonMethodNameTransforms.CamelCase(name.EndsWith(AsyncSuffix, StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - AsyncSuffix.Length) : name);

	/// <summary>
	/// Initializes a new instance of the <see cref="NodeToManagedServiceJsonRpcDescriptor"/> class.
	/// </summary>
	/// <inheritdoc cref="ServiceJsonRpcDescriptor(ServiceMoniker, Formatters, MessageDelimiters)" />
	public NodeToManagedServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter)
		: base(serviceMoniker, formatter, messageDelimiter)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="NodeToManagedServiceJsonRpcDescriptor"/> class.
	/// </summary>
	/// <inheritdoc cref="ServiceJsonRpcDescriptor(ServiceJsonRpcDescriptor)"/>
	public NodeToManagedServiceJsonRpcDescriptor(NodeToManagedServiceJsonRpcDescriptor copyFrom)
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

	/// <inheritdoc/>
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

	/// <inheritdoc/>
	protected override ServiceRpcDescriptor Clone() => new NodeToManagedServiceJsonRpcDescriptor(this);

	private static void ConfigureJsonFormatter(JsonMessageFormatter jsonFormatter)
	{
		jsonFormatter.JsonSerializer.Converters.Add(new VersionConverter());
	}

	private static void ConfigureJsonFormatter(SystemTextJsonFormatter jsonFormatter)
	{
	}
}
