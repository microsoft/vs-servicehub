// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Microsoft.ServiceHub.Framework;

/// <inheritdoc cref="ServiceJsonRpcDescriptor"/>
/// <typeparam name="T">The RPC interface used to call the service.</typeparam>
/// <devremarks>
/// This class was a fine idea, except that its benefits only comes by exposing this derived type in the public API of the service contract,
/// which is generally discouraged and folks should expose the base <see cref="ServiceRpcDescriptor"/> class for flexibility.
/// If <see cref="IServiceBroker"/> methods took an interface for the descriptor instead of a class, we could have recovered from this.
/// Oh well.
/// </devremarks>
[RequiresDynamicCode(Reasons.Formatters)]
[RequiresUnreferencedCode(Reasons.Formatters)]
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public class ServiceJsonRpcDescriptor<T> : ServiceJsonRpcDescriptor
	where T : class
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcDescriptor{T}"/> class.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="formatter">The formatter to use for the JSON-RPC message.</param>
	/// <param name="messageDelimiter">The message delimiter scheme to use.</param>
	public ServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter)
		: base(serviceMoniker, formatter, messageDelimiter)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcDescriptor{T}"/> class.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="clientInterface">The interface type that the client's "callback" RPC target is expected to implement. May be null if the service does not invoke methods on the client.</param>
	/// <param name="formatter">The formatter to use for the JSON-RPC message.</param>
	/// <param name="messageDelimiter">The message delimiter scheme to use.</param>
	public ServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface, Formatters formatter, MessageDelimiters messageDelimiter)
		: base(serviceMoniker, clientInterface, formatter, messageDelimiter)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcDescriptor{T}"/> class
	/// and initializes all fields based on a template instance.
	/// </summary>
	/// <param name="copyFrom">The instance to copy all fields from.</param>
	protected ServiceJsonRpcDescriptor(ServiceJsonRpcDescriptor<T> copyFrom)
		: base(copyFrom)
	{
	}

	/// <inheritdoc />
	protected override ServiceRpcDescriptor Clone() => new ServiceJsonRpcDescriptor<T>(this);
}
