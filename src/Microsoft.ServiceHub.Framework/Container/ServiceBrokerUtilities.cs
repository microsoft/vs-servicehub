// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

internal static class ServiceBrokerUtilities
{
	/// <summary>
	/// Creates an object whose <see cref="object.ToString"/> method defers to a given delegate.
	/// </summary>
	/// <param name="formatter">
	/// The delegate that will construct the string.
	/// This may be called concurrently or repeatedly.
	/// After returning a non-null value it will not be called again as its value will be cached.
	/// </param>
	/// <returns>An object whose <see cref="object.ToString"/> will invoke the <paramref name="formatter"/>.</returns>
	internal static object DeferredFormatting(Func<string?> formatter)
	{
		return new DeferredFormatter(formatter);
	}

	internal static async Task LinkAsync(PipeReader reader, PipeWriter writer, CancellationToken cancellationToken)
	{
		Requires.NotNull(reader, nameof(reader));
		Requires.NotNull(writer, nameof(writer));

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
				writer.WriteToPipe(result.Buffer);
				reader.AdvanceTo(result.Buffer.End);
				await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
				if (result.IsCompleted)
				{
					await writer.CompleteAsync().ConfigureAwait(false);
					break;
				}
			}

			await writer.CompleteAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await writer.CompleteAsync(ex).ConfigureAwait(false);
		}
	}

	internal static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
	{
		key = pair.Key;
		value = pair.Value;
	}

	/// <summary>
	/// Copies a sequence of bytes to a <see cref="PipeWriter"/>.
	/// </summary>
	/// <param name="writer">The writer to use.</param>
	/// <param name="sequence">The sequence to read.</param>
	private static void WriteToPipe(this PipeWriter writer, ReadOnlySequence<byte> sequence)
	{
		Requires.NotNull(writer, nameof(writer));

		foreach (ReadOnlyMemory<byte> sourceMemory in sequence)
		{
			writer.Write(sourceMemory.Span);
		}
	}

	private class DeferredFormatter
	{
		private readonly Func<string?> formatter;

		private string? realizedString;

		internal DeferredFormatter(Func<string?> formatter)
		{
			Requires.NotNull(formatter, nameof(formatter));
			this.formatter = formatter;
		}

		public override string? ToString() => this.realizedString ?? (this.realizedString = this.formatter());
	}
}
