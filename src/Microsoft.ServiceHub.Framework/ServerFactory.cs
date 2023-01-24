// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

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
	[Obsolete($"Use the overload that doesn't take a pipe name instead.")]
	public static async Task<(IDisposable Server, string ServerName)> CreateAsync(string pipeName, TraceSource? logger, Func<Stream, Task> onConnectedCallback)
	{
		Requires.NotNullOrEmpty(pipeName, nameof(pipeName));
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		(IAsyncDisposable Server, string ServerName) result = await CreateCoreAsync(pipeName, logger, onConnectedCallback).ConfigureAwait(false);
		return ((IDisposable)result.Server, result.ServerName);
	}

	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="logger">The logger for the server.</param>
	/// <param name="onConnectedCallback">
	/// Callback function to be run whenever a client connects to the server.
	/// This delegate is never called in parallel.
	/// A subsequent client that attempts to connect will have to wait for a prior invocation of this delegate to complete before it can connect.
	/// It is perfectly fine to complete the returned task while still connected to the stream with a client.
	/// </param>
	/// <returns>
	/// A tuple where <c>Server</c> is disposable to shut down the pipe, and <c>ServerName</c> is the pipe name as the client will need to access it.
	/// </returns>
	public static Task<(IAsyncDisposable Server, string ServerName)> CreateAsync(TraceSource? logger, Func<Stream, Task> onConnectedCallback)
	{
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		return CreateCoreAsync(Guid.NewGuid().ToString("n"), logger, onConnectedCallback);
	}

	private static async Task<(IAsyncDisposable Server, string ServerName)> CreateCoreAsync(string channel, TraceSource? logger, Func<Stream, Task> onConnectedCallback)
	{
		if (IsolatedUtilities.IsWindowsPlatform())
		{
			// Windows uses named pipes, and allows simple names (no paths) for its named pipes.
			// But since *nix OS's require the prefix, it's part of our protocol.
			string serverPath = @"\\.\pipe\" + channel;
			return (new NamedPipeServer(channel, logger, onConnectedCallback), serverPath);
		}
		else if (IsolatedUtilities.IsLinuxPlatform() || IsolatedUtilities.IsMacPlatform())
		{
			// On *nix we use domain sockets, which requires paths to a file that will actually be created.
			string serverPath = Path.Combine(Path.GetTempPath(), channel);

			return (await UnixDomainSocketServer.CreateAsync(serverPath, logger, onConnectedCallback).ConfigureAwait(false), serverPath);
		}

		throw new PlatformNotSupportedException();
	}
}
