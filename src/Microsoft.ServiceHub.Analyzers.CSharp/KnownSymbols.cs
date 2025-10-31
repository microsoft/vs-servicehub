// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers;

internal class KnownSymbols(Compilation compilation)
{
	private bool? hasRequiredReferences;

	private Option<INamedTypeSymbol> idisposable;
	private Option<INamedTypeSymbol> jsonRpcContractAttribute;
	private Option<INamedTypeSymbol?> rpcMarshalableOptionalInterface;
	private Option<INamedTypeSymbol?> jsonRpcProxyInterfaceGroupAttribute;
	private Option<INamedTypeSymbol?> jsonRpcProxyAttribute;
	private Option<INamedTypeSymbol> task;
	private Option<INamedTypeSymbol> taskOfT;
	private Option<INamedTypeSymbol> valueTask;
	private Option<INamedTypeSymbol> valueTaskOfT;
	private Option<INamedTypeSymbol> iasyncEnumerableOfT;
	private Option<INamedTypeSymbol> cancellationToken;

	public bool HasRequiredReferences => this.hasRequiredReferences ??= compilation.References.Any(r => r is PortableExecutableReference { FilePath: string path } && string.Equals(Path.GetFileNameWithoutExtension(path), "StreamJsonRpc", StringComparison.OrdinalIgnoreCase));

	public INamedTypeSymbol JsonRpcContractAttribute => this.Required(ref this.jsonRpcContractAttribute, Types.JsonRpcContractAttribute.FullName);

	public INamedTypeSymbol? RpcMarshalableOptionalInterface => this.Optional(ref this.rpcMarshalableOptionalInterface, Types.RpcMarshalableOptionalInterfaceAttribute.FullName);

	public INamedTypeSymbol? JsonRpcProxyInterfaceGroupAttribute => this.Optional(ref this.jsonRpcProxyInterfaceGroupAttribute, Types.JsonRpcProxyInterfaceGroupAttribute.FullName);

	public INamedTypeSymbol? JsonRpcProxyAttribute => this.Optional(ref this.jsonRpcProxyAttribute, Types.JsonRpcProxyAttribute.FullName);

	public INamedTypeSymbol IDisposable => this.Required(ref this.idisposable, "System.IDisposable");

	public INamedTypeSymbol Task => this.Required(ref this.task, "System.Threading.Tasks.Task");

	public INamedTypeSymbol TaskOfT => this.Required(ref this.taskOfT, "System.Threading.Tasks.Task`1");

	public INamedTypeSymbol ValueTask => this.Required(ref this.valueTask, "System.Threading.Tasks.ValueTask");

	public INamedTypeSymbol ValueTaskOfT => this.Required(ref this.valueTaskOfT, "System.Threading.Tasks.ValueTask`1");

	public INamedTypeSymbol IAsyncEnumerableOfT => this.Required(ref this.iasyncEnumerableOfT, "System.Collections.Generic.IAsyncEnumerable`1");

	public INamedTypeSymbol CancellationToken => this.Required(ref this.cancellationToken, "System.Threading.CancellationToken");

	private INamedTypeSymbol? Optional(ref Option<INamedTypeSymbol?> field, string metadataName)
	{
		if (!field.HasValue)
		{
			field = Option.Some(compilation.GetTypeByMetadataName(metadataName));
		}

		return field.Value;
	}

	private INamedTypeSymbol Required(ref Option<INamedTypeSymbol> field, string metadataName)
	{
		if (!field.HasValue)
		{
			field = Option.Some(compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException($"Could not find required type '{metadataName}'"));
		}

		return field.Value;
	}
}
