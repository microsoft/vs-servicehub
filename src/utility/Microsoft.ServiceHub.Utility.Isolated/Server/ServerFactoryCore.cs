// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Microsoft.ServiceHub.Utility;

#pragma warning disable CS1574 // XML comment has cref attribute that could not be resolved
/// <summary>
/// Static class containing the core implementations for a server factory. This exists so that we don't run into any type errors with types that are
/// shared between assemblies within DevCore. Internally in DevCore the "Core" implementation should be used while externally the public implementations are used instead.
/// </summary>
internal static class ServerFactoryCore
{
	/// <summary>
	/// Creates a named pipe server.
	/// </summary>
	/// <param name="pipeName">The name of the server.</param>
	/// <param name="logger">The logger for the server.</param>
	/// <param name="createAndConfigureService">Callback function to be run whenever a client connects to the server.</param>
	/// <returns>
	/// A disposable server that should be disposed of when it is no longer needed.
	/// This object is also castable to <see cref="IAsyncDisposable"/> except if this method is referenced from Microsoft.ServiceHub.HostStub.dll.
	/// </returns>
	/// <remarks>This method should only ever be used on Windows platforms.</remarks>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
	internal static IDisposable Create(string pipeName, TraceSource logger, Func<Stream, Task> createAndConfigureService)
	{
		IsolatedUtilities.RequiresNotNullOrEmpty(pipeName, nameof(pipeName));
		IsolatedUtilities.RequiresNotNull(createAndConfigureService, nameof(createAndConfigureService));

		return new NamedPipeServer(pipeName, logger, createAndConfigureService);
	}

	/// <summary>
	/// Creates a named pipe server on a linux or mac machine.
	/// </summary>
	/// <param name="channelName">The multiplexed channel name to be used for the socket.</param>
	/// <param name="locationServiceChannelName">The base channel name to be used for the socket.</param>
	/// <param name="logger">The logger for the server.</param>
	/// <param name="createAndConfigureService">Callback function to be run whenever a client connects to the server.</param>
	/// <returns>The server that is to be disposed when it is no longer needed.</returns>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
#endif
	internal static async Task<(IDisposable Server, string ServerPath)> CreateOnNonWindowsAsync(string channelName, string locationServiceChannelName, TraceSource logger, Func<Stream, Task> createAndConfigureService)
	{
		if (locationServiceChannelName is null)
		{
			throw new ArgumentNullException(nameof(locationServiceChannelName));
		}

		if (!UnixDomainSocketEndPoint.IsSupported)
		{
			throw new PlatformNotSupportedException();
		}

		string serverPath = IsolatedUtilities.GetUnixSocketDir(channelName, locationServiceChannelName);

		return (await UnixDomainSocketServer.CreateAsync(serverPath, logger, createAndConfigureService).ConfigureAwait(false), serverPath);
	}
}
#pragma warning restore CS1574 // XML comment has cref attribute that could not be resolved

