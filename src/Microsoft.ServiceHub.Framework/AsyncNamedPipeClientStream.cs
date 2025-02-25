// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using static Windows.Win32.PInvoke;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Named pipe client that avoids TimeoutExceptions and burning CPU.
/// </summary>
[SupportedOSPlatform("windows")]
internal class AsyncNamedPipeClientStream : PipeStream
{
	private const FILE_SHARE_MODE SharingFlags = FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE;

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
	/// Wrapper for CreateFile to create a named pipe client.
	/// Previous attempts used CreateFile directly, but for unknown reasons there were issues with the pipe handle.
	/// </summary>
	/// <returns>A handle to the named pipe.</returns>
	/// <param name="lpFileName">The name of the pipe.</param>
	/// <param name="dwDesiredAccess">The requested access to the file or device.</param>
	/// <param name="dwShareMode">The requested sharing mode of the file or device.</param>
	/// <param name="securityAttributes">A SECURITY_ATTRIBUTES structure.</param>
	/// <param name="dwCreationDisposition">The action to take on files that exist, and on files that do not exist.</param>
	/// <param name="dwFlagsAndAttributes">The file attributes and flags.</param>
	/// <param name="hTemplateFile">A handle to a template file with the GENERIC_READ access right.</param>
	[DllImport("kernel32.dll", EntryPoint = "CreateFile", CharSet = CharSet.Auto, SetLastError = true, BestFitMapping = false)]
	[SecurityCritical]
	internal static extern SafePipeHandle CreateNamedPipeClient(
		string lpFileName,
		int dwDesiredAccess,
		System.IO.FileShare dwShareMode,
		Windows.Win32.Security.SECURITY_ATTRIBUTES securityAttributes,
		System.IO.FileMode dwCreationDisposition,
		int dwFlagsAndAttributes,
		IntPtr hTemplateFile);

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
			if (retries > maxRetries || this.TryConnect())
			{
				break;
			}

			retries++;
			var errorCode = Marshal.GetLastWin32Error();
			errorCodeMap[errorCode] = errorCodeMap.ContainsKey(errorCode) ? errorCodeMap[errorCode] + 1 : 1;

			await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);
		}

		// Throw if cancellation requested, otherwise throw a TimeoutException
		cancellationToken.ThrowIfCancellationRequested();

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

	private bool TryConnect()
	{
		// Immediately return if the pipe is not available
		if (!WaitNamedPipe(this.pipePath, 1))
		{
			return false;
		}

		SafePipeHandle handle = CreateNamedPipeClient(
			this.pipePath,
			(int)this.access,
			FileShare.ReadWrite | FileShare.Delete,
			this.GetSecurityAttributes(true),
			System.IO.FileMode.Open,
			(int)this.pipeOptions,
			IntPtr.Zero);

		if (handle.IsInvalid)
		{
			handle.Dispose();
			return false;
		}

		// Success!
		this.InitializeHandle(handle, false, true);
		this.IsConnected = true;
		this.ValidateRemotePipeUser();
		return true;
	}

	private void ValidateRemotePipeUser()
	{
#if NETFRAMEWORK
		var isCurrentUserOnly = (this.pipeOptions & PolyfillExtensions.PipeOptionsCurrentUserOnly) == PolyfillExtensions.PipeOptionsCurrentUserOnly;
#elif NET5_0_OR_GREATER
		var isCurrentUserOnly = (this.pipeOptions & PipeOptions.CurrentUserOnly) == PipeOptions.CurrentUserOnly;
#endif

#if NETFRAMEWORK || NET5_0_OR_GREATER
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

	private Windows.Win32.Security.SECURITY_ATTRIBUTES GetSecurityAttributes(bool inheritable)
	{
		var secAttr = new Windows.Win32.Security.SECURITY_ATTRIBUTES
		{
			bInheritHandle = inheritable,
			nLength = (uint)Marshal.SizeOf(typeof(Windows.Win32.Security.SECURITY_ATTRIBUTES)),
		};

		return secAttr;
	}
}
