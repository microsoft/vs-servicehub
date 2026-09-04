// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using PolyType;
using StreamJsonRpc;

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IResilientTestService : IAsyncDisposable
{
	event EventHandler EarlierChanged;

	event EventHandler Changed;

	Task<int> GetGenerationAsync(CancellationToken cancellationToken);

	IAsyncEnumerable<int> StreamAsync(CancellationToken cancellationToken);

	void Notify(int value);

	Task<IDisposable> ObserveAsync(IObserver<int> observer, CancellationToken cancellationToken);
}

[JsonRpcContract]
[JsonRpcProxyInterfaceGroup(typeof(IResilientTestServiceMetadata))]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IResilientTestServiceWithMetadata : IResilientTestService
{
}

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IResilientTestServiceMetadata
{
	Task<string> GetNameAsync(CancellationToken cancellationToken);
}
