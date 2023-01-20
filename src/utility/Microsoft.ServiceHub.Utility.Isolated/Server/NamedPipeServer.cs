// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.ServiceHub.Framework;
#if TELEMETRY
using Microsoft.VisualStudio.Telemetry;
#endif

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// A server whose backing streams are based on named pipes. Invokes a callback when a client connects to the server.
/// </summary>
internal sealed class NamedPipeServer : Server
{
	/// <summary>
	/// The transmission mode used for the named pipes.
	/// </summary>
	/// <remarks>
	/// We use <see cref="PipeTransmissionMode.Byte"/> rather than <see cref="PipeTransmissionMode.Message"/>
	/// because our goal is to hide the transport from the clients and services, and <see cref="PipeTransmissionMode.Message"/>
	/// behavior is a named pipe specific behavior. Unix domain sockets cannot emulate it, so if any service or client were to
	/// depend on the message boundaries that named pipes offered, they might malfunction on *nix platforms.
	/// So instead, we simply don't offer that unique behavior.
	/// </remarks>
	private const PipeTransmissionMode TransmissionMode = PipeTransmissionMode.Byte;

	private readonly CancellationTokenSource serverTaskCts;
	private readonly Task serverTask;

	private bool executingClientConnectedCallback;

	/// <summary>
	/// Initializes a new instance of the <see cref="NamedPipeServer"/> class.
	/// </summary>
	/// <param name="channelName">The name of the named pipe.</param>
	/// <param name="logger">A trace source to be used for logging.</param>
	/// <param name="createAndConfigureService">The callback that is invoked when a client connects to the server.</param>
	internal NamedPipeServer(string channelName, TraceSource logger, Func<WrappedStream, Task> createAndConfigureService)
		: base(logger, createAndConfigureService)
	{
		IsolatedUtilities.RequiresNotNullOrEmpty(channelName, nameof(channelName));

		this.serverTaskCts = new CancellationTokenSource();
		CancellationToken cancellationToken = this.serverTaskCts.Token;

		// Create the pipe server here and not in the server task so when the NamedPipeServer instance is created,
		// it is ready to accept its first client.
		NamedPipeServerStream pipeServer = this.CreatePipe(channelName);

		// Do not pass the cancellation token to the Task.Run() because we want to run it at least
		// to dispose of the pipe server even if it has been cancelled before it starts executing .
		this.serverTask = Task.Run(async () =>
		{
			int retryCount = 0;
			ConcurrentDictionary<string, int> retryExceptions = new ConcurrentDictionary<string, int>();
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					try
					{
						await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						break;
					}
					catch (IOException ex)
					{
						string exceptionType = ex.GetType().ToString();
						retryExceptions.AddOrUpdate(exceptionType, 1, (type, count) => count++);
#if TELEMETRY
                        if (retryCount != 0)
                        {
                            TelemetryLogger.LogAtomicOperation(
                                TelemetryConstants.NamedPipeServerStreamConnectSuccessRetriesRequiredEvent,
                                new Dictionary<string, object>()
                                {
                                    { TelemetryConstants.NamedPipeServerStreamConnectSuccessRetriesRequired_RetryCount, retryCount },
                                    { TelemetryConstants.NamedPipeServerStreamConnectSuccessRetriesRequired_Exceptions, new TelemetryComplexProperty(retryExceptions) },
                                });
                        }
#endif

						// The client has disconnected prematurely before WaitForConnection could pick it up.
						// Ignore that and wait for the next connection unless cancellation is requested.
						if (cancellationToken.IsCancellationRequested)
						{
							break;
						}

						await pipeServer.DisposeAsync().ConfigureAwait(false);
						pipeServer = this.CreatePipe(channelName);
						continue;
					}

					// A client has connected. Open a stream to it and start listening for the next client
					// unless cancellation is requested.
					if (!cancellationToken.IsCancellationRequested)
					{
						var stream = new ServiceHubPipeStream(pipeServer);

						// Create a new pipe server before configuring the client connection
						// so the next client doesn't wait for the first client's createAndConfigureService.
						pipeServer = this.CreatePipe(channelName);

						this.executingClientConnectedCallback = true;
						await this.ClientConnected(stream).ConfigureAwait(false);
						this.executingClientConnectedCallback = false;
					}
				}

				retryCount++;
			}
			catch (Exception exception)
			{
				this.Logger.TraceException(exception, "Exception running {0}.{1}", this.GetType().Name, nameof(this.serverTask));
				LogNamedPipeServerStreamConnectFailedTelemetry(exception, retryCount, retryExceptions);
			}
			finally
			{
				await pipeServer.DisposeAsync().ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc/>
	protected override async Task DisposeAsyncCore()
	{
		const int ServerTaskShutdownTimeoutMs = 2000;
		try
		{
			this.serverTaskCts.Cancel();
		}
		catch (AggregateException ex)
		{
			// CLR sometimes throws ObjectDisposedException when canceling WaitForConnectionAsync (https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_workitems/edit/533872)
			ex.Handle(e => e is ObjectDisposedException);
		}

		try
		{
			if (!this.serverTask.IsCompleted)
			{
				Task serverShutdownTimeout = Task.Delay(ServerTaskShutdownTimeoutMs);
				await Task.WhenAny(this.serverTask, serverShutdownTimeout).ConfigureAwait(false);

				if (!this.serverTask.IsCompleted)
				{
					if (!this.executingClientConnectedCallback)
					{
						this.Logger.TraceError("{0}.{1} timed out to finish in {2} ms.", this.GetType().Name, nameof(this.serverTask), ServerTaskShutdownTimeoutMs);
					}
					else
					{
						this.Logger.TraceWarning("{0}.{1} timed out to finish in {2} ms waiting on the ClientConnected callback.", this.GetType().Name, nameof(this.serverTask), ServerTaskShutdownTimeoutMs);
					}
				}
			}
		}
		catch (Exception exception)
		{
			this.Logger.TraceException(exception, "{0}.{1} failed to finish with exception.", this.GetType().Name, nameof(this.serverTask));
		}

		await base.DisposeAsyncCore().ConfigureAwait(false);
	}

	private static void LogNamedPipeServerStreamConnectFailedTelemetry(Exception ex, int retryCount, ConcurrentDictionary<string, int> retryExceptions)
	{
#if TELEMETRY
        TelemetryLogger.LogFaultEvent(
            TelemetryConstants.NamedPipeServerStreamConnectFailedEvent,
            ex,
            new Dictionary<string, object>()
            {
                { TelemetryConstants.NamedPipeServerStreamConnect_Retries, retryCount },
                { TelemetryConstants.NamedPipeServerStreamConnect_Exceptions, new TelemetryComplexProperty(retryExceptions) },
            });
#endif
	}

	private NamedPipeServerStream CreatePipe(string channelName)
	{
		// TODO: for pipe security see if we need to use a different constructor for .NET Core.
		// Tracked by task 178897: Investigate Named Pipe and Unix Domain Socket channel security.
		return new NamedPipeServerStream(
			channelName,
			PipeDirection.InOut,
			NamedPipeServerStream.MaxAllowedServerInstances,
			TransmissionMode,
			PipeOptions.Asynchronous);
	}
}
