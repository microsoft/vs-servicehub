// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET6_0_OR_GREATER

using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Provides socket client services at a higher level
/// of abstraction than the <see cref="System.Net.Sockets.Socket" /> class.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static class SocketClient
{
	/// <summary>
	/// The time to wait between repeat attempts at connecting to the server.
	/// </summary>
	internal static readonly TimeSpan ConnectionRetryInterval = TimeSpan.FromMilliseconds(100);

	/// <summary>
	/// Opens a connection to a socket.
	/// </summary>
	/// <param name="path">The endpoint of the socket to connect to.</param>
	/// <param name="flags">The <see cref="ChannelConnectionFlags"/> used for the connection.</param>
	/// <param name="cancellationToken">A token whose cancellation will terminate a connection request.</param>
	/// <returns>A task whose result is a duplex pipe created to access the <see cref="Socket"/>.</returns>
	/// <exception cref="SocketException">Thrown when the connection attempt fails.</exception>
	internal static async Task<Socket> ConnectAsync(string path, ChannelConnectionFlags flags, CancellationToken cancellationToken)
	{
		var endPoint = new UnixDomainSocketEndPoint(path);
		Socket socket = await SocketClient.ConnectAsync(
			endPoint,
			SocketType.Stream,
			ProtocolType.Unspecified,
			flags,
			ConnectionRetryInterval,
			cancellationToken).ConfigureAwait(false);
		return socket;
	}

	/// <summary>
	/// Opens a connection to a socket.
	/// </summary>
	/// <param name="endPoint">The endpoint of the socket to connect to.</param>
	/// <param name="socketType">The type of socket to connect to.</param>
	/// <param name="protocolType">The type of protocol that will be used.</param>
	/// <param name="flags">Modifiers in the connection process.</param>
	/// <param name="connectionRetryInterval">The time to wait between repeat attempts at connecting to the socket.</param>
	/// <param name="cancellationToken">A token whose cancellation will terminate a connection request.</param>
	/// <returns>A task whose result is the created <see cref="Socket"/>.</returns>
	/// <exception cref="SocketException">Thrown when the connection attempt fails.</exception>
	internal static async Task<Socket> ConnectAsync(
		EndPoint endPoint,
		SocketType socketType,
		ProtocolType protocolType,
		ChannelConnectionFlags flags,
		TimeSpan connectionRetryInterval,
		CancellationToken cancellationToken = default)
	{
		IsolatedUtilities.RequiresNotNull(endPoint, nameof(endPoint));
		var socket = new Socket(endPoint.AddressFamily, socketType, protocolType);
		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var tcs = new TaskCompletionSource<SocketError>();
				var args = new SocketAsyncEventArgs();
				args.RemoteEndPoint = endPoint;
				args.UserToken = tcs;
				args.Completed += (sender, e) => ((TaskCompletionSource<SocketError>)e.UserToken!).SetResult(e.SocketError);
				SocketError error = socket.ConnectAsync(args) ? await tcs.Task.ConfigureAwait(false) : args.SocketError;
				if (error == SocketError.Success && socket.Connected)
				{
					break;
				}

				if (flags.HasFlag(ChannelConnectionFlags.WaitForServerToConnect))
				{
					switch (error)
					{
						case SocketError.Success: // we need to handle Success error code because Mono runtime will return this code although the socket is not connected
						case SocketError.SocketError:
						case SocketError.ConnectionRefused:
						case SocketError.AddressNotAvailable:
							// No server socket opened yet.
							// Wait a short while before trying again to avoid spinning the CPU.
							await Task.Delay(connectionRetryInterval, cancellationToken).ConfigureAwait(false);
							continue; // loop around and try again
					}
				}

				throw new SocketException((int)error);
			}
		}
		catch
		{
			socket.Dispose();
			throw;
		}

		return socket;
	}
}

#endif
