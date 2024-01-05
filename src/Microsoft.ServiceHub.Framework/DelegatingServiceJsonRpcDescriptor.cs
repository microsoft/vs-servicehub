// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A delegating RPC descriptor for services that support JSON-RPC. By default this descriptor will delegate operations to the wrapped descriptor.
/// </summary>
/// <remarks>
/// This implementation will also ensure inner descriptor is updated with any changes to multiplexing stream options or exception strategy.
/// </remarks>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public abstract class DelegatingServiceJsonRpcDescriptor : ServiceJsonRpcDescriptor
{
	private ServiceJsonRpcDescriptor innerDescriptor;

	/// <summary>
	/// Initializes a new instance of the <see cref="DelegatingServiceJsonRpcDescriptor"/> class.
	/// </summary>
	/// <param name="innerDescriptor">The descriptor to delegate operations to.</param>
	public DelegatingServiceJsonRpcDescriptor(ServiceJsonRpcDescriptor innerDescriptor)
		: base(innerDescriptor)
	{
		this.innerDescriptor = innerDescriptor;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="DelegatingServiceJsonRpcDescriptor"/> class.
	/// </summary>
	/// <param name="descriptor">Delegating descriptor to copy from.</param>
	public DelegatingServiceJsonRpcDescriptor(DelegatingServiceJsonRpcDescriptor descriptor)
		: base(descriptor)
	{
		Requires.NotNull(descriptor, nameof(descriptor));
		this.innerDescriptor = descriptor.innerDescriptor;
	}

	/// <inheritdoc />
	public override RpcConnection ConstructRpcConnection(IDuplexPipe pipe)
	{
		this.innerDescriptor = this.innerDescriptor.WithSettingsFrom(this);
		return this.innerDescriptor.ConstructRpcConnection(pipe);
	}

	/// <inheritdoc />
	protected internal override JsonRpcConnection CreateConnection(JsonRpc jsonRpc)
	{
		return this.innerDescriptor.CreateConnection(jsonRpc);
	}

	/// <inheritdoc />
	protected internal override IJsonRpcMessageFormatter CreateFormatter()
	{
		return this.innerDescriptor.CreateFormatter();
	}

	/// <inheritdoc />
	protected internal override IJsonRpcMessageHandler CreateHandler(IDuplexPipe pipe, IJsonRpcMessageFormatter formatter)
	{
		return this.innerDescriptor.CreateHandler(pipe, formatter);
	}

	/// <inheritdoc />
	protected internal override JsonRpc CreateJsonRpc(IJsonRpcMessageHandler handler)
	{
		return this.innerDescriptor.CreateJsonRpc(handler);
	}
}
