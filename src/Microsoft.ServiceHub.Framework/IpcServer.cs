// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A server that invokes a callback whenever a client connects to it.
/// </summary>
internal class IpcServer : IDisposable, IIpcServer
{
	private readonly CancellationTokenSource disposalTokenSource = new();
	private readonly Task listeningTask;
	private readonly Func<Stream, Task> createAndConfigureService;

	/// <summary>
	/// Initializes a new instance of the <see cref="IpcServer"/> class.
	/// </summary>
	/// <param name="options">IPC server options.</param>
	/// <param name="createAndConfigureService">The callback that is invoked when a client connects to the server.</param>
	internal IpcServer(ServerFactory.ServerOptions options, Func<Stream, Task> createAndConfigureService)
	{
		if (options.Name is null)
		{
			options = options with { Name = ServerFactory.PrependPipePrefix(Guid.NewGuid().ToString("n")) };
		}
		else
		{
			Requires.Argument(options.Name.Length > 0, nameof(options), "A non-empty pipe name is required.");
		}

		// We always prefix the channel name with a path. On Windows this might be optional on .NET, but other platforms like Node.js require it.
		// And when we're running off Windows, we need to specify the path so that we can tell other platforms where the pipe is.
		this.Name = options.Name;

		this.TraceSource = options.TraceSource ?? new TraceSource("ServiceHub.Framework pipe server", SourceLevels.Off);
		this.Options = options;
		this.createAndConfigureService = createAndConfigureService;

		this.listeningTask = this.ListenForIncomingRequestsAsync(this.disposalTokenSource.Token);
	}

	/// <inheritdoc/>
	public string Name { get; }

	/// <inheritdoc/>
	public Task Completion => this.listeningTask;

	/// <summary>
	/// Gets a trace source used for logging.
	/// </summary>
	public TraceSource TraceSource { get; }

	/// <summary>
	/// Gets the server options.
	/// </summary>
	private ServerFactory.ServerOptions Options { get; }

	/// <inheritdoc/>
	public void Dispose()
	{
		GC.SuppressFinalize(this);
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
		this.DisposeAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
	}

	/// <summary>
	/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
	/// </summary>
	/// <returns>A task tracking the work.</returns>
	public async ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);
		try
		{
			this.disposalTokenSource.Cancel();
		}
		catch (AggregateException ex)
		{
			// CLR sometimes throws ObjectDisposedException when canceling WaitForConnectionAsync (https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_workitems/edit/533872)
			ex.Handle(e => e is ObjectDisposedException);
		}

		TimeSpan serverTaskShutdownTimeout = TimeSpan.FromSeconds(2);
		try
		{
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks (this is ours, and we know it never picks up a SyncContext)
			await this.listeningTask.WithTimeout(serverTaskShutdownTimeout).ConfigureAwait(false);
#pragma warning restore VSTHRD003 // Avoid awaiting foreign Tasks
		}
		catch (TimeoutException)
		{
			this.TraceSource.TraceEvent(TraceEventType.Error, 0, "{0}.{1} timed out to finish in {2} ms.", nameof(IpcServer), nameof(this.ListenForIncomingRequestsAsync), serverTaskShutdownTimeout.TotalMilliseconds);
		}
		catch (Exception exception)
		{
			this.TraceSource.TraceException(exception, "{0}.{1} failed to finish with exception.", this.GetType().Name, nameof(this.listeningTask));
		}
	}

	private async Task ListenForIncomingRequestsAsync(CancellationToken cancellationToken)
	{
		// For our own pipe, we only use the fully-qualified names on *nix.
		// Otherwise, our \\.\pipe\ prefix will be concatenated to the implied prefix, causing a mismatch when connected to from another platform like node.js.
		string pipeName = ServerFactory.TrimWindowsPrefixForDotNet(this.Name);

		Dictionary<Type, int> retryExceptions = new();
		NamedPipeServerStream? pipeServer = CreatePipe(pipeName);
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					// This should be the first await in the method so that the pipe server is running before we return to our caller.
					await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (IOException ex)
				{
					retryExceptions.TryGetValue(ex.GetType(), out int retryThisExceptionType);
					retryExceptions[ex.GetType()] = retryThisExceptionType + 1;

					// The client has disconnected prematurely before WaitForConnectionAsync could pick it up.
					// Ignore that and wait for the next connection unless cancellation is requested.
					if (cancellationToken.IsCancellationRequested)
					{
						break;
					}

					await pipeServer.DisposeAsync().ConfigureAwait(false);
					pipeServer = CreatePipe(pipeName);
					continue;
				}

				// A client has connected. Open a stream to it (and possibly start listening for the next client)
				// unless cancellation is requested.
				if (!cancellationToken.IsCancellationRequested)
				{
					// We invoke the callback in a fire-and-forget fashion as documented. It handles its own exceptions.
					ClientConnectedAsync(pipeServer).Forget();

					// Prepare to listen for another connection, or exit as requested.
					if (this.Options.AllowMultipleClients)
					{
						pipeServer = CreatePipe(pipeName);
					}
					else
					{
						// Clear our local variable so we don't dispose the stream that we just handed to the client.
						pipeServer = null;
						break;
					}
				}
			}
		}
		catch (Exception exception)
		{
			this.TraceSource.TraceException(exception, "Exception in {0}.{1}", nameof(IpcServer), nameof(this.ListenForIncomingRequestsAsync));
			throw;
		}
		finally
		{
			if (pipeServer is not null)
			{
				await pipeServer.DisposeAsync().ConfigureAwait(false);
			}
		}

		NamedPipeServerStream CreatePipe(string channelName)
		{
			// We use PipeTransmissionMode.Byte rather than PipeTransmissionMode.Message
			// because our goal is to hide the transport from the clients and services, and PipeTransmissionMode.Message
			// behavior is a named pipe specific behavior. Unix domain sockets cannot emulate it, so if any service or client were to
			// depend on the message boundaries that named pipes offered, they might malfunction on *nix platforms.
			// So instead, we simply don't offer that unique behavior.
			return new NamedPipeServerStream(
				channelName,
				PipeDirection.InOut,
				NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte,
				this.Options.PipeOptions);
		}

		async Task ClientConnectedAsync(PipeStream stream)
		{
			Requires.NotNull(stream, nameof(stream));

			try
			{
				// Always yield before invoking the callback so as to avoid slowing down our incoming read loop.
				await TaskScheduler.Default.SwitchTo(alwaysYield: true);
				await this.createAndConfigureService(stream).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
				this.TraceSource.TraceException(exception);
				return;
			}

			if (!stream.IsConnected)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
			}
		}
	}
}
