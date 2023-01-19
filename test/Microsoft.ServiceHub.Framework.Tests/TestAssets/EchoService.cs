// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

internal class EchoService : IEchoService
{
	internal EchoService(ServiceActivationOptions options)
	{
		this.Options = options;
	}

	public ServiceActivationOptions Options { get; }

	public Task<Dictionary<string, string>> DictionaryAsync(Dictionary<string, string> arg, CancellationToken cancellationToken) => Task.FromResult(arg);

	public Task<List<string>> ListAsync(List<string> arg, CancellationToken cancellationToken) => Task.FromResult(arg);

	public async Task<byte[]> ReadAndReturnAsync(IDuplexPipe pipe, CancellationToken cancellationToken)
	{
		pipe.Output.Complete();

		using (var sequence = new Sequence<byte>())
		{
			ReadResult readResult = await pipe.Input.ReadAsync(cancellationToken);
			foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
			{
				sequence.Write(segment.Span);
			}

			pipe.Input.AdvanceTo(readResult.Buffer.End);
			return sequence.AsReadOnlySequence.ToArray();
		}
	}
}
