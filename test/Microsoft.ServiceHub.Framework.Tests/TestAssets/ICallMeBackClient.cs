// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using StreamJsonRpc;

public interface ICallMeBackClient
{
	[JsonRpcMethod("youPhoned")]
	Task YouPhonedAsync(string message, CancellationToken cancellationToken);
}
