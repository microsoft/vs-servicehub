// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework.Reflection;

/// <summary>
/// Settings that influence the behavior of a local proxy.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct ProxyInputs
{
	/// <summary>
	/// Gets the primary/main interface that describes the functions available on the remote end.
	/// </summary>
	public required Type ContractInterface { get; init; }

	/// <summary>
	/// Gets a list of additional interfaces that the client proxy should implement.
	/// </summary>
	public ReadOnlyMemory<Type> AdditionalContractInterfaces { get; init; }

	/// <summary>
	/// Gets the exception strategy for the proxy.
	/// </summary>
	internal ExceptionProcessing ExceptionStrategy { get; init; }

	/// <summary>
	/// Gets a description of the requirements on the proxy to be used.
	/// </summary>
	internal string Requirements => $"Implementing interface(s): {string.Join(", ", [this.ContractInterface, .. this.AdditionalContractInterfaces.Span])}";
}
