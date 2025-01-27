// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Named pipe client that avoids TimeoutExceptions and burning CPU.
/// </summary>
[SupportedOSPlatform("windows")]
internal class AsyncNamedPipeClientStream : PipeStream
{
	private readonly string pipePath;
	private readonly TokenImpersonationLevel impersonationLevel;
	private readonly PipeOptions pipeOptions;
	private readonly PipeDirection direction;
	private readonly uint access;

	/// <summary>
	/// Initializes a new instance of the <see cref="AsyncNamedPipeClientStream"/> class.
	/// </summary>
	/// <param name="serverName">Pipe server name.</param>
	/// <param name="pipeName">Pipe name.</param>
	/// <param name="direction">Communication direction.</param>
	/// <param name="options">Pipe options.</param>
	/// <param name="impersonationLevel">Impersonation level.</param>
	internal AsyncNamedPipeClientStream(
		string serverName,
		string pipeName,
		PipeDirection direction,
		PipeOptions options,
		TokenImpersonationLevel impersonationLevel = TokenImpersonationLevel.None)
		: base(direction, 4096)
	{
		Requires.NotNullOrEmpty(serverName);
		Requires.NotNullOrEmpty(pipeName);

		this.pipePath = $@"\\{serverName}\pipe\{pipeName}";
		this.direction = direction;
		this.pipeOptions = options;
		this.impersonationLevel = impersonationLevel;
		this.access = GetAccess(direction);
	}

	/// <summary>
	/// Connects pipe client to server.
	/// </summary>
	/// <param name="maxRetries">Maximum number of retries.</param>
	/// <param name="retryDelayMs">Milliseconds delay between retries.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A task representing the establishment of the client connection.</returns>
	/// <exception cref="TimeoutException">Thrown if no connection can be established.</exception>
	internal async Task ConnectAsync(
		int maxRetries,
		int retryDelayMs,
		CancellationToken cancellationToken)
	{
		var errorCodeMap = new Dictionary<int, int>();
		int retries = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			if (retries > maxRetries || this.TryConnect(errorCodeMap))
			{
				break;
			}

			retries++;
			await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);
		}

		if (retries > maxRetries || !this.IsConnected)
		{
			throw new TimeoutException($"Failed with errors: {string.Join(", ", errorCodeMap.Select(x => $"(code: {x.Key}, count: {x.Value})"))}");
		}
	}

	private static uint GetAccess(PipeDirection direction)
	{
		uint access = 0;
		if ((PipeDirection.In & direction) == PipeDirection.In)
		{
			access |= (uint)GENERIC_ACCESS_RIGHTS.GENERIC_READ;
		}

		if ((PipeDirection.Out & direction) == PipeDirection.Out)
		{
			access |= (uint)GENERIC_ACCESS_RIGHTS.GENERIC_WRITE;
		}

		return access;
	}

	private bool TryConnect(Dictionary<int, int> errorCodeMap)
	{
		var pipeFlags = (int)this.pipeOptions;

#pragma warning disable CA1416 // Validate platform compatibility - no way to validate this OS version is greater than 5
		SafeFileHandle handle = CreateFile(
			this.pipePath,
			(uint)this.access,
			Windows.Win32.Storage.FileSystem.FILE_SHARE_MODE.FILE_SHARE_NONE,
			null,
			Windows.Win32.Storage.FileSystem.FILE_CREATION_DISPOSITION.OPEN_EXISTING,
			(Windows.Win32.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES)pipeFlags,
			null);
#pragma warning restore CA1416 // Validate platform compatibility

		if (handle.IsInvalid)
		{
			int errorCode = Marshal.GetLastWin32Error();
			int newErrorCount = errorCodeMap.TryGetValue(errorCode, out var currentCount) ? currentCount + 1 : 1;
			errorCodeMap[errorCode] = newErrorCount;
			return false;
		}

		// Success!
		this.InitializeHandle(new SafePipeHandle(handle.DangerousGetHandle(), true), false, true);
		this.IsConnected = true;
		this.ValidateRemotePipeUser();
		return true;
	}

	private void ValidateRemotePipeUser()
	{
#if NETFRAMEWORK || NET5_0_OR_GREATER
#if NETFRAMEWORK
		var isCurrentUserOnly = (this.pipeOptions & PolyfillExtensions.PipeOptionsCurrentUserOnly) == PolyfillExtensions.PipeOptionsCurrentUserOnly;
#else
		var isCurrentUserOnly = (this.pipeOptions & PipeOptions.CurrentUserOnly) == PipeOptions.CurrentUserOnly;
#endif

		// No validation needed - pipe is not restricted to current user
		if (!isCurrentUserOnly)
		{
			return;
		}

		System.IO.Pipes.PipeSecurity accessControl = this.GetAccessControl();
		IdentityReference? remoteOwnerSid = accessControl.GetOwner(typeof(SecurityIdentifier));
		using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
		SecurityIdentifier? currentUserSid = currentIdentity.Owner;
		if (remoteOwnerSid != currentUserSid)
		{
			this.IsConnected = false;
			throw new UnauthorizedAccessException();
		}
#endif
	}
}
