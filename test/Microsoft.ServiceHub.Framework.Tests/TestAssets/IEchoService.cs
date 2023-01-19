// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;

public interface IEchoService
{
	Task<List<string>> ListAsync(List<string> arg, CancellationToken cancellationToken);

	Task<Dictionary<string, string>> DictionaryAsync(Dictionary<string, string> arg, CancellationToken cancellationToken);

	Task<byte[]> ReadAndReturnAsync(IDuplexPipe pipe, CancellationToken cancellationToken);
}
