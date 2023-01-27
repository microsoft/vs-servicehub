// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A static class for creating named pipe servers.
/// </summary>
public static class ServerFactory
{
	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="pipeName">The name of the server. Typically just the result of calling <see cref="Guid.ToString()"/> on the result of <see cref="Guid.NewGuid()"/>. This should <em>not</em> include path separators.</param>
	/// <param name="logger">The logger for the server.</param>
	/// <param name="onConnectedCallback">Callback function to be run whenever a client connects to the server.</param>
	/// <returns>
	/// A tuple where <c>Server</c> is disposable to shut down the pipe, and <c>ServerName</c> is the pipe name as the client will need to access it. It implements <see cref="IAsyncDisposable"/>.
	/// <c>ServerName</c> will typically be the same as <paramref name="pipeName"/> on Windows, but on mac/linux it will have a path prepended to it.
	/// </returns>
	[Obsolete($"Use {nameof(Create)} instead.")]
	public static Task<(IDisposable Server, string ServerName)> CreateAsync(string pipeName, TraceSource? logger, Func<Stream, Task> onConnectedCallback)
	{
		Requires.NotNullOrEmpty(pipeName, nameof(pipeName));
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		(IAsyncDisposable Server, string ServerName) result = CreateCore(pipeName, new ServerOptions { TraceSource = logger }, onConnectedCallback);
		return Task.FromResult(((IDisposable)result.Server, result.ServerName));
	}

	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="onConnectedCallback">
	/// Callback function to be run whenever a client connects to the server.
	/// This delegate is never called in parallel.
	/// A subsequent client that attempts to connect will have to wait for a prior invocation of this delegate to complete before it can connect.
	/// It is perfectly fine to complete the returned task while still connected to the stream with a client.
	/// </param>
	/// <param name="options">IPC server options.</param>
	/// <returns>
	/// A tuple where <c>Server</c> is disposable to shut down the pipe, and <c>ServerName</c> is the pipe name as the client will need to access it.
	/// </returns>
	public static (IAsyncDisposable Server, string ServerName) Create(Func<Stream, Task> onConnectedCallback, ServerOptions options = default)
	{
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		return CreateCore(Guid.NewGuid().ToString("n"), options, onConnectedCallback);
	}

	/// <summary>
	/// Connects to an IPC pipe that was created with <see cref="Create(Func{Stream, Task}, ServerOptions)"/>.
	/// </summary>
	/// <param name="pipeName">The name of the pipe as returned from the <see cref="Create(Func{Stream, Task}, ServerOptions)"/> method.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The duplex stream established over the pipe.</returns>
	public static async Task<Stream> ConnectAsync(string pipeName, CancellationToken cancellationToken)
	{
		Requires.NotNull(pipeName, nameof(pipeName));

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			const string WindowsPipePrefix = @"\\.\pipe\";
			string leafName = pipeName.StartsWith(WindowsPipePrefix, StringComparison.OrdinalIgnoreCase)
				? pipeName.Substring(WindowsPipePrefix.Length)
				: pipeName;

			var pipeStream = new NamedPipeClientStream(".", leafName, PipeDirection.InOut, PipeOptions.Asynchronous);
			try
			{
				await pipeStream.ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
				return pipeStream;
			}
			catch
			{
				await pipeStream.DisposeAsync().ConfigureAwait(false);
				throw;
			}
		}
		else
		{
#if NET6_0_OR_GREATER
			Socket socket = await SocketClient.ConnectAsync(pipeName, ChannelConnectionFlags.WaitForServerToConnect, cancellationToken).ConfigureAwait(false);
			return new NetworkStream(socket, ownsSocket: true);
#else
			throw new PlatformNotSupportedException("Use the .NET-specific assembly for this.");
#endif
		}
	}

	/// <summary>
	/// Attempts to get the native handle behind a stream
	/// that was created by <see cref="ConnectAsync(string, CancellationToken)"/> or <see cref="Create(Func{Stream, Task}, ServerOptions)"/>.
	/// </summary>
	/// <param name="stream">The stream to get the handle of.</param>
	/// <param name="handle">The handle of the stream if it exists, <see langword="null" /> otherwise.</param>
	/// <returns><see langword="true" /> if the stream has a <see cref="SafePipeHandle"/>, <see langword="false" /> otherwise.</returns>
	public static bool TryGetHandle(Stream? stream, [NotNullWhen(true)] out SafePipeHandle? handle)
	{
		if (stream is ServiceHubPipeStream devHubPipeStream)
		{
			handle = devHubPipeStream.SafePipeHandle;
			return true;
		}

		if (stream is PipeStream pipeStream)
		{
			handle = pipeStream.SafePipeHandle;
			return true;
		}

		handle = null;
		return false;
	}

	private static (IAsyncDisposable Server, string ServerName) CreateCore(string channel, ServerOptions options, Func<Stream, Task> onConnectedCallback)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			// Windows uses named pipes, and allows simple names (no paths) for its named pipes.
			// But since *nix OS's require the prefix, it's part of our protocol.
			string serverPath = @"\\.\pipe\" + channel;
			return (new NamedPipeServer(channel, options, onConnectedCallback), serverPath);
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
#if NET6_0_OR_GREATER
			// On *nix we use domain sockets, which requires paths to a file that will actually be created.
			string serverPath = Path.Combine(Path.GetTempPath(), channel);

			return (UnixDomainSocketServer.Create(serverPath, options, onConnectedCallback), serverPath);
#else
			throw new PlatformNotSupportedException("Use the .NET-specific assembly for this.");
#endif
		}

		throw new PlatformNotSupportedException();
	}

	/// <summary>
	/// Options that can influence the IPC server.
	/// </summary>
	public record struct ServerOptions
	{
		/// <summary>
		/// Gets the means of logging regarding connection attempts.
		/// </summary>
		public TraceSource? TraceSource { get; init; }

		/// <summary>
		/// Gets a value indicating whether only one incoming request will be served.
		/// </summary>
		/// <remarks>
		/// Implementations of this vary across operating systems.
		/// On Windows, only one named pipe client will be accepted.
		/// On other operating systems where unix domain sockets are used, more than one client may be able to connect,
		/// but the extra clients will be disconnected without communicating with them, and without invoking the callback
		/// that passes the incoming stream to the server.
		/// </remarks>
		public bool OneClientOnly { get; init; }
	}
}
