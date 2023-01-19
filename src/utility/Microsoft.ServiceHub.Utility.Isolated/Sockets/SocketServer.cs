// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Sockets;

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// Implements the connection logic for the socket server.
/// After accepting a connection, clientConnected event is fired.
/// Code taken from <see href="https://msdn.microsoft.com/en-us/library/system.net.sockets.socketasynceventargs.aspx"/>.
/// </summary>
internal class SocketServer : IDisposable
{
	private const int MaxAcceptRetries = 10;
	private readonly object socketLock = new object();
	private readonly Func<Socket, Task> clientConnected;
	private readonly CancellationTokenSource disposeCts = new CancellationTokenSource();

	private Socket? socket;                // The socket used to listen for incoming connection requests.
	private int acceptRetryCount = MaxAcceptRetries;

	private SocketServer(Func<Socket, Task> clientConnected)
	{
		IsolatedUtilities.RequiresNotNull(clientConnected, nameof(clientConnected));
		this.clientConnected = clientConnected;
	}

	/// <summary>
	/// Creates an instance of <see cref="SocketServer"/>.
	/// </summary>
	/// <param name="endPoint">The socket server's network address.</param>
	/// <param name="socketType">Indicates the type of socket.</param>
	/// <param name="protocolType">The protocol to be used by the socket.</param>
	/// <param name="clientConnected">Callback function to be run whenever a client connects to the socket.</param>
	/// <returns>The <see cref="SocketServer"/> instance that was created.</returns>
	public static async Task<SocketServer> CreateAsync(EndPoint endPoint, SocketType socketType, ProtocolType protocolType, Func<Socket, Task> clientConnected)
	{
		IsolatedUtilities.RequiresNotNull(endPoint, nameof(endPoint));

		var server = new SocketServer(clientConnected);

		// Create the socket which listens for incoming connections.
		server.socket = new Socket(endPoint.AddressFamily, socketType, protocolType);

		try
		{
			server.socket.Bind(endPoint);
			server.socket.Listen(backlog: int.MaxValue);
			await server.StartAcceptAsync(null).ConfigureAwait(false);
		}
		catch
		{
			server.socket.Dispose();
			throw;
		}

		return server;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		this.Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Performs application-defined tasks associated with freeing, releasing, or resetting resources.
	/// </summary>
	/// <param name="disposing">Indicated whether or not managed resources should be disposed. Should be false when called from a finalizer.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			this.disposeCts.Cancel();
			this.disposeCts.Dispose();

			lock (this.socketLock)
			{
				if (this.socket != null)
				{
					this.socket.Dispose();
					this.socket = null;
				}
			}
		}
	}

	/// <summary>
	/// Begin an operation to accept a connection request from the client.
	/// </summary>
	/// <param name="acceptEventArg">
	/// The context object to use when issuing the accept operation on the server's listening socket.
	/// </param>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Legacy", "VSTHRD101: Avoid using async lambda for a void returning delegate type, because any exceptions not handled by the delegate will crash the process", Justification = "This method has worked fine in the past before this error was introduced. Creating a work item to track it getting fixed.")]
	private async Task StartAcceptAsync(SocketAsyncEventArgs? acceptEventArg)
	{
		if (acceptEventArg == null)
		{
			acceptEventArg = new SocketAsyncEventArgs();
			acceptEventArg.Completed += async (sender, e) => await this.ProcessAcceptAsync(e).ConfigureAwait(false);
		}
		else
		{
			// Socket must be cleared since the context object is being reused.
			acceptEventArg.AcceptSocket = null;
		}

		bool willRaiseEvent = false;
		lock (this.socketLock)
		{
			if (this.socket == null)
			{
				// The socket server has been disposed
				return;
			}

			willRaiseEvent = this.socket.AcceptAsync(acceptEventArg);
		}

		if (!willRaiseEvent)
		{
			await this.ProcessAcceptAsync(acceptEventArg).ConfigureAwait(false);
		}
	}

	private async Task ProcessAcceptAsync(SocketAsyncEventArgs e)
	{
		if (e.SocketError != SocketError.Success)
		{
			// If possible, retry the accept after a short wait.
			if (e.SocketError != SocketError.OperationAborted
				&& e.SocketError != SocketError.Interrupted
				&& !this.disposeCts.IsCancellationRequested)
			{
				// If there were too many errors, the socket won't accept any more connections.
				// TODO: fire an event on socket accept error and let the event handler decide what to do here.
				if (this.acceptRetryCount-- > 0)
				{
					await Task.Delay(500, this.disposeCts.Token).ConfigureAwait(false);
					await this.StartAcceptAsync(e).ConfigureAwait(false);
				}
			}

			return;
		}

		this.acceptRetryCount = MaxAcceptRetries;

		if (e.AcceptSocket == null)
		{
			throw new ArgumentNullException(nameof(e.AcceptSocket));
		}

		await this.clientConnected(e.AcceptSocket).ConfigureAwait(false);

		// Accept the next connection request.
		await this.StartAcceptAsync(e).ConfigureAwait(false);
	}
}
