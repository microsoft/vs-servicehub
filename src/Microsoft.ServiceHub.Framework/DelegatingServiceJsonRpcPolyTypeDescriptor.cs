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
/// <para>
/// Default implementation of this class only delegates protected override methods and <see cref="ConstructRpcConnection(IDuplexPipe)"/> is not delegated
/// intentionally. This allows derived classes to pick which protected methods they want to override and which ones to delegate while
/// keeping default ConstructRpcConnection implementation.
/// </para>
/// <para>
/// This implementation will also ensure inner descriptor is updated with any changes settings such as multiplexing options, before
/// <see cref="ConstructRpcConnection(IDuplexPipe)"/> is executed.
/// </para>
/// </remarks>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
[RequiresDynamicCode(Reasons.Formatters)]
[RequiresUnreferencedCode(Reasons.Formatters)]
public abstract class DelegatingServiceJsonRpcPolyTypeDescriptor : ServiceJsonRpcPolyTypeDescriptor
{
	private ServiceJsonRpcPolyTypeDescriptor innerDescriptor;

	/// <summary>
	/// Initializes a new instance of the <see cref="DelegatingServiceJsonRpcPolyTypeDescriptor"/> class.
	/// </summary>
	/// <param name="innerDescriptor">The descriptor to delegate operations to.</param>
	public DelegatingServiceJsonRpcPolyTypeDescriptor(ServiceJsonRpcPolyTypeDescriptor innerDescriptor)
		: base(innerDescriptor)
	{
		this.innerDescriptor = innerDescriptor;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="DelegatingServiceJsonRpcPolyTypeDescriptor"/> class.
	/// </summary>
	/// <param name="descriptor">Delegating descriptor to copy from.</param>
	public DelegatingServiceJsonRpcPolyTypeDescriptor(DelegatingServiceJsonRpcPolyTypeDescriptor descriptor)
		: base(descriptor)
	{
		Requires.NotNull(descriptor, nameof(descriptor));
		this.innerDescriptor = descriptor.innerDescriptor;
	}

	/// <inheritdoc />
	public override RpcConnection ConstructRpcConnection(IDuplexPipe pipe)
	{
		this.ApplySettingsToInnerDescriptor();
		return base.ConstructRpcConnection(pipe);
	}

	/// <inheritdoc />
	protected internal override JsonRpcConnection CreateConnection(JsonRpc jsonRpc) => this.innerDescriptor.CreateConnection(jsonRpc);

	/// <inheritdoc />
	protected internal override IJsonRpcMessageFormatter CreateFormatter() => this.innerDescriptor.CreateFormatter();

	/// <inheritdoc />
	protected internal override IJsonRpcMessageHandler CreateHandler(IDuplexPipe pipe, IJsonRpcMessageFormatter formatter) => this.innerDescriptor.CreateHandler(pipe, formatter);

	/// <inheritdoc />
	protected internal override JsonRpc CreateJsonRpc(IJsonRpcMessageHandler handler) => this.innerDescriptor.CreateJsonRpc(handler);

	/// <summary>
	/// Ensures that settings from the current instance are applied to delegated descriptor.
	/// </summary>
	/// <remarks>
	/// This is called by <see cref="ConstructRpcConnection(IDuplexPipe)" /> by default but if derived class overrides that method
	/// it should call this method to ensure settings are applied properly to the delegated descriptor.
	/// </remarks>
	protected void ApplySettingsToInnerDescriptor() => this.innerDescriptor = this.innerDescriptor.WithSettingsFrom(this);
}
