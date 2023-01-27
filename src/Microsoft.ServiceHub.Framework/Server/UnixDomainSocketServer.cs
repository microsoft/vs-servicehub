﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET6_0_OR_GREATER

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A socket server to be used on Unix machines that invokes a callback whenever it is connected to.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class UnixDomainSocketServer : Server
{
	private readonly string path;
	private SocketServer? socketServer;
	private bool disposed;

	private UnixDomainSocketServer(string path, ServerFactory.ServerOptions options, Func<WrappedStream, Task> createAndConfigureService)
		: base(options, createAndConfigureService)
	{
		Requires.NotNullOrEmpty(path, nameof(path));
		this.path = path;

		string? directory = Path.GetDirectoryName(this.path);
		if (directory is not null && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
	}

	/// <summary>
	/// Creates an instance of a <see cref="UnixDomainSocketServer"/>.
	/// </summary>
	/// <param name="path">The path on disk to the socket file.</param>
	/// <param name="options">IPC server options.</param>
	/// <param name="createAndConfigureService">Callback function to be run whenever a client connects to the server.</param>
	/// <returns>The <see cref="UnixDomainSocketServer"/> that was created.</returns>
	internal static UnixDomainSocketServer Create(string path, ServerFactory.ServerOptions options, Func<WrappedStream, Task> createAndConfigureService)
	{
		var server = new UnixDomainSocketServer(path, options, createAndConfigureService);

		var endPoint = new UnixDomainSocketEndPoint(server.path);
		server.socketServer = SocketServer.Create(
			endPoint,
			SocketType.Stream,
			ProtocolType.Unspecified,
			options,
			socket => server.ClientConnected(new UnixDomainSocketStream(socket)));

		return server;
	}

	/// <inheritdoc/>
	protected override Task DisposeAsyncCore()
	{
		if (!this.disposed)
		{
			this.disposed = true;
			this.socketServer?.Dispose();
			if (!this.HasClients)
			{
				this.TryDeleteUnixDomainSocketFile();
			}
		}

		return base.DisposeAsyncCore();
	}

	/// <inheritdoc/>
	protected override void ClientDisconnected(Stream stream)
	{
		base.ClientDisconnected(stream);
		if (this.disposed && !this.HasClients)
		{
			this.TryDeleteUnixDomainSocketFile();
		}
	}

	private void TryDeleteUnixDomainSocketFile()
	{
		try
		{
			File.Delete(this.path);
		}
		catch (Exception exception)
		{
			this.Logger.TraceException(exception, "Error deleting Unix Domain Socket '{0}'", this.path);
		}
	}
}

#endif
