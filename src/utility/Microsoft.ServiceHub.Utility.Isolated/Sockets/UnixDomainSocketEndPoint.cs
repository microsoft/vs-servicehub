// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An <see cref="EndPoint"/> used to represent a Unix domain socket (i.e. a OSX/Linux equivalent of named pipes in Windows).
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class UnixDomainSocketEndPoint : EndPoint
{
	/// <summary>
	/// The maximum path allowed for an endpoint.
	/// </summary>
	/// <seealso href="http://pubs.opengroup.org/onlinepubs/9699919799/basedefs/sys_un.h.html">sockaddr_un.sun_path</seealso>
	internal const int MaxPathLength = 92;

	/// <summary>
	/// The <see cref="SocketType"/> to use for this endpoint.
	/// </summary>
	internal const SocketType Stream = SocketType.Stream;

	/// <summary>
	/// The <see cref="ProtocolType"/> to use for this endpoint.
	/// </summary>
	internal const ProtocolType Protocol = ProtocolType.Unspecified;

	// offsetof(struct sockaddr_un, sun_path). It's the same on Linux and OSX
	private const int PathOffset = 2;

	private const int MaxSocketAddressSize = PathOffset + MaxPathLength;
	private const int MinSocketAddressSize = PathOffset + 2; // +1 for one character and +1 for \0 ending
	private const AddressFamily EndPointAddressFamily = AddressFamily.Unix;

	private static readonly Encoding PathEncoding = Encoding.UTF8;

	private readonly string path;
	private readonly byte[] encodedPath;

	/// <summary>
	/// Initializes a new instance of the <see cref="UnixDomainSocketEndPoint"/> class.
	/// </summary>
	/// <param name="path">The path to the file that represents the socket.</param>
	internal UnixDomainSocketEndPoint(string path)
	{
		IsolatedUtilities.RequiresNotNullOrEmpty(path, nameof(path));

		if (PathEncoding.GetByteCount(path) >= MaxPathLength)
		{
			throw new ArgumentOutOfRangeException(nameof(path));
		}

		this.path = path;
		this.encodedPath = PathEncoding.GetBytes(this.path);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="UnixDomainSocketEndPoint"/> class.
	/// </summary>
	/// <param name="socketAddress">The address of the socket.</param>
	internal UnixDomainSocketEndPoint(SocketAddress socketAddress)
	{
		IsolatedUtilities.RequiresNotNull(socketAddress, nameof(socketAddress));

		if (socketAddress.Family != EndPointAddressFamily || socketAddress.Size > MaxSocketAddressSize)
		{
			throw new ArgumentException(nameof(socketAddress));
		}

		if (socketAddress.Size >= MinSocketAddressSize)
		{
			this.encodedPath = new byte[socketAddress.Size - PathOffset];
			for (int index = 0; index < socketAddress.Size - PathOffset; index++)
			{
				this.encodedPath[index] = socketAddress[PathOffset + index];
			}

			this.path = PathEncoding.GetString(this.encodedPath, 0, this.encodedPath.Length);
		}
		else
		{
			// Empty path may be used by System.Net.Socket logging.
			this.encodedPath = new byte[0];
			this.path = string.Empty;
		}
	}

	/// <inheritdoc />
	public override AddressFamily AddressFamily => EndPointAddressFamily;

	/// <summary>
	/// Gets the path to this socket.
	/// </summary>
	internal string Path => this.path;

	/// <inheritdoc />
	public override SocketAddress Serialize()
	{
		SocketAddress result = new SocketAddress(AddressFamily.Unix, MaxSocketAddressSize);

		// Ctor has already checked that PathOffset + _encodedPath.Length < MaxSocketAddressSize
		for (int index = 0; index < this.encodedPath.Length; index++)
		{
			result[PathOffset + index] = this.encodedPath[index];
		}

		// The path must be ending with \0
		result[PathOffset + this.encodedPath.Length] = 0;

		return result;
	}

	/// <inheritdoc />
	public override EndPoint Create(SocketAddress socketAddress) => new UnixDomainSocketEndPoint(socketAddress);

	/// <inheritdoc />
	public override string ToString() => this.Path;
}
