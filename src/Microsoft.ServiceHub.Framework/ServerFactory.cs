// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Microsoft.ServiceHub.Utility;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A static class for creating named pipe servers.
/// </summary>
public static class ServerFactory
{
	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="pipeName">The name of the server.</param>
	/// <param name="logger">The logger for the server.</param>
	/// <param name="onConnectedCallback">Callback function to be run whenever a client connects to the server.</param>
	/// <returns>
	/// A disposable server that should be disposed of when it is no longer needed as well as the name of the pipe or socket that the server is opened on.
	/// This server is also castable to <see cref="IAsyncDisposable"/>.
	/// </returns>
	public static Task<(IDisposable Server, string ServerName)> CreateAsync(string pipeName, TraceSource logger, Func<Stream, Task> onConnectedCallback)
	{
		Requires.NotNullOrEmpty(pipeName, nameof(pipeName));
		Requires.NotNull(logger, nameof(logger));
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		if (IsolatedUtilities.IsWindowsPlatform())
		{
			return Task.FromResult((ServerFactoryCore.Create(pipeName, logger, onConnectedCallback), pipeName));
		}
		else if (IsolatedUtilities.IsLinuxPlatform() || IsolatedUtilities.IsMacPlatform())
		{
			return ServerFactoryCore.CreateOnNonWindowsAsync(pipeName, pipeName, logger, onConnectedCallback);
		}

		throw new PlatformNotSupportedException();
	}
}
