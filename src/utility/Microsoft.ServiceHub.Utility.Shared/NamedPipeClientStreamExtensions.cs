// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.IO.Pipes;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Extension methods for the <see cref="NamedPipeClientStream"/> class.
/// </summary>
internal static class NamedPipeClientStreamExtensions
{
#pragma warning disable SA1310 // Field names must not contain underscore
	/// <summary>
	/// Does not wait for the named pipe. If the named pipe is not available, the function returns an error.
	/// </summary>
	internal const int NMPWAIT_NOWAIT = 1;

	/// <summary>
	/// An HRESULT indicating a timeout after which we might retry.
	/// </summary>
	internal const int ERROR_SEM_TIMEOUT_HRESULT = unchecked((int)0x80070079);
#pragma warning restore SA1310 // Field names must not contain underscore
	private const int ConnectRetryIntervalMs = 20;
	private const int MaxRetryAttemptsForFileNotFoundException = 3;

	/// <summary>
	/// Connects to a named pipe without spinning the CPU as <see cref="NamedPipeClientStream.Connect(int)"/> or <see cref="NamedPipeClientStream.ConnectAsync(CancellationToken)"/> would do.
	/// </summary>
	/// <param name="npcs">The named pipe client stream to connect.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <param name="maxRetries">The maximum number of retries to attempt.</param>
	/// <param name="withSpinningWait">Whether or not the connect should be attempted with a spinning wait.
	/// If the pipe being connected to is known to exist, it is safe to use a spinning wait to avoid potentially throwing exceptions for retries.</param>
	/// <returns>A <see cref="Task"/> that tracks the asynchronous connection attempt.</returns>
	internal static async Task ConnectWithRetryAsync(this NamedPipeClientStream npcs, CancellationToken cancellationToken, int maxRetries = int.MaxValue, bool withSpinningWait = false)
	{
		Requires.NotNull(npcs, nameof(npcs));

		ConcurrentDictionary<string, int> retryExceptions = new ConcurrentDictionary<string, int>();
		int fileNotFoundRetryCount = 0;
		int totalRetries = 0;

		while (true)
		{
			try
			{
				if (withSpinningWait)
				{
					await npcs.ConnectAsync(cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Try connecting without wait.
					// Connecting with anything else will consume CPU causing a spin wait.
					await npcs.ConnectAsync(NMPWAIT_NOWAIT).ConfigureAwait(false);
				}

				return;
			}
			catch (Exception ex)
			{
				string exceptionType = ex.GetType().ToString();
				retryExceptions.AddOrUpdate(exceptionType, 1, (type, count) => count++);

				if (ex is ObjectDisposedException)
				{
					// Prefer to throw OperationCanceledException if the caller requested cancellation.
					cancellationToken.ThrowIfCancellationRequested();
					throw;
				}
				else if (((ex is IOException && ex.HResult == ERROR_SEM_TIMEOUT_HRESULT) || ex is TimeoutException) && totalRetries < maxRetries)
				{
					// Ignore and retry.
					totalRetries++;
				}
				else if (ex is FileNotFoundException && fileNotFoundRetryCount < MaxRetryAttemptsForFileNotFoundException && totalRetries < maxRetries)
				{
					// Ignore and retry.
					totalRetries++;
					fileNotFoundRetryCount++;
				}
				else
				{
					throw;
				}
			}

			try
			{
				// Throws OperationCanceledException
				cancellationToken.ThrowIfCancellationRequested();

				// Throws TaskCanceledException
				await Task.Delay(ConnectRetryIntervalMs, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
			{
				cancellationToken.ThrowIfCancellationRequested();
				throw;
			}
		}
	}
}
