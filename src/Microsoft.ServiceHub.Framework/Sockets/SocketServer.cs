// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET6_0_OR_GREATER

using System.Net;
using System.Net.Sockets;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Implements the connection logic for the socket server.
/// After accepting a connection, clientConnected event is fired.
/// Code taken from <see href="https://msdn.microsoft.com/en-us/library/system.net.sockets.socketasynceventargs.aspx"/>.
/// </summary>
internal class SocketServer : IDisposable
{
	private readonly Func<Socket, Task> clientConnected;
	private readonly ServerFactory.ServerOptions options;
	private readonly CancellationTokenSource disposeCts = new CancellationTokenSource();

	/// <summary>
	/// The socket used to listen for incoming connection requests.
	/// </summary>
	private Socket? socket;

	private SocketServer(Func<Socket, Task> clientConnected, ServerFactory.ServerOptions options)
	{
		Requires.NotNull(clientConnected, nameof(clientConnected));
		this.clientConnected = clientConnected;
		this.options = options;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		this.Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Creates an instance of <see cref="SocketServer"/>.
	/// </summary>
	/// <param name="endPoint">The socket server's network address.</param>
	/// <param name="socketType">Indicates the type of socket.</param>
	/// <param name="protocolType">The protocol to be used by the socket.</param>
	/// <param name="options">IPC server options.</param>
	/// <param name="clientConnected">Callback function to be run whenever a client connects to the socket.</param>
	/// <returns>The <see cref="SocketServer"/> instance that was created.</returns>
	internal static SocketServer Create(EndPoint endPoint, SocketType socketType, ProtocolType protocolType, ServerFactory.ServerOptions options, Func<Socket, Task> clientConnected)
	{
		Requires.NotNull(endPoint, nameof(endPoint));

		var server = new SocketServer(clientConnected, options);

		// Create the socket which listens for incoming connections.
		server.socket = new Socket(endPoint.AddressFamily, socketType, protocolType);

		try
		{
			server.socket.Bind(endPoint);
			server.socket.Listen(backlog: options.OneClientOnly ? 1 : int.MaxValue);
			_ = server.StartAcceptAsync(); // this method logs all its own exceptions.
		}
		catch
		{
			server.socket.Dispose();
			throw;
		}

		return server;
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

			if (this.socket is { } socket)
			{
				socket.Dispose();
				this.socket = null;
			}
		}
	}

	/// <summary>
	/// Begin an operation to accept a connection request from the client.
	/// </summary>
	private async Task StartAcceptAsync()
	{
		try
		{
			do
			{
				Assumes.NotNull(this.socket);
				Socket incoming = await this.socket.AcceptAsync().ConfigureAwait(false);
				if (this.options.OneClientOnly)
				{
					this.socket.Dispose();
				}

				await this.clientConnected(incoming).ConfigureAwait(false);
			}
			while (!this.options.OneClientOnly && !this.disposeCts.IsCancellationRequested);
		}
		catch (Exception ex)
		{
			this.options.TraceSource?.TraceException(ex);
			throw;
		}
	}
}

#endif
