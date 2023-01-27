// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Sockets;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A wrapper for a socket stream to augment its capabilities.
/// </summary>
internal sealed class UnixDomainSocketStream : WrappedStream
{
	private readonly Socket socket;

	/// <summary>
	/// Initializes a new instance of the <see cref="UnixDomainSocketStream"/> class.
	/// </summary>
	/// <param name="socket">The socket to be wrapped.</param>
	public UnixDomainSocketStream(Socket socket)
		: base(new NetworkStream(socket, ownsSocket: true))
	{
		Requires.NotNull(socket, nameof(socket));
		this.socket = socket;
		this.UpdateConnectedState();
	}

	/// <inheritdoc />
	protected override bool GetConnected() => base.GetConnected() && this.socket.Connected;
}
