// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft;

internal static class Utilities
{
	internal static Task WaitForReaderCompletionAsync(this PipeWriter writer)
	{
		Requires.NotNull(writer, nameof(writer));

		var readerDone = new TaskCompletionSource<object?>();
#pragma warning disable CS0618 // Type or member is obsolete
		writer.OnReaderCompleted(
			(ex, tcsObject) =>
			{
				var tcs = (TaskCompletionSource<object?>)tcsObject!;
				if (ex != null)
				{
					tcs.SetException(ex);
				}
				else
				{
					tcs.SetResult(null);
				}
			},
			readerDone);
#pragma warning restore CS0618 // Type or member is obsolete
		return readerDone.Task;
	}

	internal static Task WaitForWriterCompletionAsync(this PipeReader reader)
	{
		Requires.NotNull(reader, nameof(reader));

		var writerDone = new TaskCompletionSource<object?>();
#pragma warning disable CS0618 // Type or member is obsolete
		reader.OnWriterCompleted(
			(ex, wdObject) =>
			{
				var wd = (TaskCompletionSource<object?>)wdObject!;
				if (ex != null)
				{
					wd.SetException(ex);
				}
				else
				{
					wd.SetResult(null);
				}
			},
			writerDone);
#pragma warning restore CS0618 // Type or member is obsolete
		return writerDone.Task;
	}
}
