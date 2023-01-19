// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <content>
/// Contains the <see cref="RpcOrderPreservingSynchronizationContext"/> nested class.
/// </content>
public partial class ServiceRpcDescriptor
{
	/// <summary>
	/// A <see cref="SynchronizationContext"/> that preserves message order.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Delegates will be invoked on the threadpool in the order they were posted with
	/// <see cref="SynchronizationContext.Post(SendOrPostCallback, object)"/>.
	/// No two delegates will ever be executed concurrently.
	/// Note that if the delegate invokes an async method, the delegate formally ends
	/// when the async method yields for the first time or returns, whichever comes first.
	/// Once that delegate returns the next delegate can be executed.
	/// </para>
	/// <para>
	/// This <see cref="SynchronizationContext"/> is not a fully functional one, and is intended
	/// only for use with <see cref="StreamJsonRpc.JsonRpc"/> to preserve RPC order.
	/// It should not be set as the <see cref="SynchronizationContext.Current"/> <see cref="SynchronizationContext"/>.
	/// </para>
	/// </remarks>
	[Obsolete("Use Microsoft.VisualStudio.Threading.NonConcurrentSynchronizationContext instead.")]
	protected class RpcOrderPreservingSynchronizationContext : SynchronizationContext, IDisposable
	{
		/// <summary>
		/// The queue of work to execute.
		/// </summary>
		private readonly AsyncQueue<(SendOrPostCallback, object?)> queue = new AsyncQueue<(SendOrPostCallback, object?)>();

		/// <summary>
		/// Initializes a new instance of the <see cref="RpcOrderPreservingSynchronizationContext"/> class.
		/// </summary>
		public RpcOrderPreservingSynchronizationContext()
		{
			// Start the queue processor. It will handle all exceptions.
			this.ProcessQueueAsync().Forget();
		}

		/// <summary>
		/// Occurs when posted work throws an unhandled exception.
		/// </summary>
		public event EventHandler<Exception>? UnhandledException;

		/// <inheritdoc />
		public override void Post(SendOrPostCallback d, object? state) => this.queue.Enqueue((d, state));

		/// <summary>
		/// Throws <see cref="NotSupportedException"/>.
		/// </summary>
		/// <param name="d">The delegate to invoke.</param>
		/// <param name="state">State to pass to the delegate.</param>
		public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

		/// <summary>
		/// Throws <see cref="NotSupportedException"/>.
		/// </summary>
		/// <returns>Nothing.</returns>
		public override SynchronizationContext CreateCopy() => throw new NotSupportedException();

		/// <summary>
		/// Causes this <see cref="SynchronizationContext"/> to reject all future posted work and
		/// releases the queue processor when it is empty.
		/// </summary>
		public void Dispose() => this.queue.Complete();

		/// <summary>
		/// Executes queued work on the threadpool, one at a time.
		/// </summary>
		/// <returns>A task that always completes successfully.</returns>
		private async Task ProcessQueueAsync()
		{
			try
			{
				while (!this.queue.IsCompleted)
				{
					(SendOrPostCallback, object?) work = await this.queue.DequeueAsync().ConfigureAwait(false);
					try
					{
						work.Item1(work.Item2);
					}
					catch (Exception ex)
					{
						this.UnhandledException?.Invoke(this, ex);
					}
				}
			}
			catch (OperationCanceledException) when (this.queue.IsCompleted)
			{
				// Successful shutdown.
			}
			catch (Exception ex)
			{
				// A failure to schedule work is fatal because it can lead to hangs that are
				// very hard to diagnose to a failure in the scheduler, and even harder to identify
				// the root cause of the failure in the scheduler.
				Environment.FailFast("Failure in scheduler.", ex);
			}
		}
	}
}
