// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A static class for creating named pipe servers.
/// </summary>
public static class ServerFactory
{
	/// <summary>
	/// The standard pipe options to use.
	/// </summary>
#if NET5_0_OR_GREATER
	internal const PipeOptions StandardPipeOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
#else
	internal const PipeOptions StandardPipeOptions = PipeOptions.Asynchronous;
#endif

	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="pipeName">The name of the server. Typically just the result of calling <see cref="Guid.ToString()"/> on the result of <see cref="Guid.NewGuid()"/>. This should <em>not</em> include path separators.</param>
	/// <param name="logger">The logger for the server.</param>
	/// <param name="onConnectedCallback"><inheritdoc cref="Create" path="/param[@name='onConnectedCallback']"/></param>
	/// <returns>
	/// A tuple where <c>Server</c> is disposable to shut down the pipe, and <c>ServerName</c> is the pipe name as the client will need to access it. It implements <see cref="IAsyncDisposable"/>.
	/// <c>ServerName</c> will typically be the same as <paramref name="pipeName"/> on Windows, but on mac/linux it will have a path prepended to it.
	/// </returns>
	[Obsolete($"Use {nameof(Create)} instead.")]
	public static Task<(IDisposable Server, string ServerName)> CreateAsync(string pipeName, TraceSource? logger, Func<Stream, Task> onConnectedCallback)
	{
		Requires.NotNullOrEmpty(pipeName, nameof(pipeName));
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		IpcServer result = CreateCore(pipeName, new ServerOptions { TraceSource = logger, AllowMultipleClients = true }, onConnectedCallback);
		return Task.FromResult<(IDisposable, string)>((result, result.Name));
	}

	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="onConnectedCallback">
	/// Callback function to be run whenever a client connects to the server. This may be called concurrently if multiple clients connect.
	/// The delegate may choose to return right away while still using the <see cref="Stream"/> or to complete only after finishing communication with the client.
	/// </param>
	/// <param name="options">IPC server options.</param>
	/// <returns>
	/// A tuple where <c>Server</c> is disposable to shut down the pipe, and <c>ServerName</c> is the pipe name as the client will need to access it.
	/// </returns>
	public static IIpcServer Create(Func<Stream, Task> onConnectedCallback, ServerOptions options = default)
	{
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		return CreateCore(Guid.NewGuid().ToString("n"), options, onConnectedCallback);
	}

	/// <inheritdoc cref="ConnectAsync(string, ClientOptions, CancellationToken)"/>
	public static Task<Stream> ConnectAsync(string pipeName, CancellationToken cancellationToken) => ConnectAsync(pipeName, default(ClientOptions), cancellationToken);

	/// <summary>
	/// Connects to an IPC pipe that was created with <see cref="Create(Func{Stream, Task}, ServerOptions)"/>.
	/// </summary>
	/// <param name="pipeName">The name of the pipe as returned from the <see cref="Create(Func{Stream, Task}, ServerOptions)"/> method.</param>
	/// <param name="options">Options that can influence how the IPC pipe is connected to.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The duplex stream established over the pipe.</returns>
	public static async Task<Stream> ConnectAsync(string pipeName, ClientOptions options, CancellationToken cancellationToken)
	{
		Requires.NotNull(pipeName, nameof(pipeName));

		const string WindowsPipePrefix = @"\\.\pipe\";
		string leafName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && pipeName.StartsWith(WindowsPipePrefix, StringComparison.OrdinalIgnoreCase)
			? pipeName.Substring(WindowsPipePrefix.Length)
			: pipeName;

		NamedPipeClientStream pipeStream = new(".", leafName, PipeDirection.InOut, StandardPipeOptions);
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

	private static IpcServer CreateCore(string channel, ServerOptions options, Func<Stream, Task> onConnectedCallback)
	{
		return new IpcServer(channel, options with { PipeOptions = StandardPipeOptions }, onConnectedCallback);
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
		/// Gets a value indicating whether to serve more than one incoming client.
		/// </summary>
		public bool AllowMultipleClients { get; init; }

		/// <summary>
		/// Gets the options to use on the named pipes.
		/// </summary>
		internal PipeOptions PipeOptions { get; init; }
	}

	/// <summary>
	/// Options that can influence the IPC client.
	/// </summary>
	public record struct ClientOptions
	{
		/// <summary>
		/// Gets a value indicating whether to continuously retry or wait for the server to listen for and respond to connection requests
		/// until it is canceled.
		/// </summary>
		/// <remarks>
		/// Without this flag, the connection will be attempted only once and immediately fail if the
		/// server is not online and responsive.
		/// </remarks>
		public bool WaitForServerToConnect { get; init; }
	}
}
