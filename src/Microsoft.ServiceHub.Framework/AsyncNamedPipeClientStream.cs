// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.ServiceHub.Framework
{
	/// <summary>
	/// Named pipe client that avoids TimeoutExceptions and burning CPU.
	/// </summary>
	[SupportedOSPlatform("windows")]
	public class AsyncNamedPipeClientStream : PipeStream
	{
		private const int SECURITYSQOSPRESENT = 0x00100000;
		private const int GENERICREAD = unchecked((int)0x80000000);
		private const int GENERICWRITE = 0x40000000;
		private readonly string pipePath;
		private readonly TokenImpersonationLevel impersonationLevel;
		private readonly PipeOptions pipeOptions;
		private readonly PipeDirection direction;
		private readonly int access;

		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncNamedPipeClientStream"/> class.
		/// </summary>
		/// <param name="serverName">Pipe server name.</param>
		/// <param name="pipeName">Pipe name.</param>
		/// <param name="direction">Communication direction.</param>
		/// <param name="options">Pipe options.</param>
		/// <param name="impersonationLevel">Impersonation level.</param>
		public AsyncNamedPipeClientStream(
			string serverName,
			string pipeName,
			PipeDirection direction,
			PipeOptions options,
			TokenImpersonationLevel impersonationLevel = TokenImpersonationLevel.None)
			: base(direction, 4096)
		{
			Requires.NotNullOrEmpty(serverName, nameof(serverName));
			Requires.NotNullOrEmpty(pipeName, nameof(pipeName));

			this.pipePath = $@"\\{serverName}\pipe\{pipeName}";
			this.direction = direction;
			this.pipeOptions = options;
			this.impersonationLevel = impersonationLevel;
			this.access = this.GetAccess();
		}

		/// <summary>
		/// Connects pipe client to server.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <param name="maxRetries">Maximum number of retries.</param>
		/// <param name="retryDelayMs">Milliseconds delay between retries.</param>
		/// <returns>A task representing the establishment of the client connection.</returns>
		/// <exception cref="TimeoutException">Thrown if no connection can be established.</exception>
		public async Task ConnectAsync(
			CancellationToken cancellationToken,
			int maxRetries,
			int retryDelayMs)
		{
			var errorCodeMap = new Dictionary<int, int>();
			int retries = 0;
			while (!cancellationToken.IsCancellationRequested)
			{
				if (retries > maxRetries || this.TryConnect(ref errorCodeMap))
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

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafePipeHandle CreateNamedPipeClient(
			string lpFileName,
			uint dwDesiredAccess,
			uint dwShareMode,
			IntPtr secAttrs,
			uint dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		private bool TryConnect(ref Dictionary<int, int> errorCodeMap)
		{
			var pipeFlags = this.GetPipeFlags();
			SafePipeHandle handle = CreateNamedPipeClient(this.pipePath, (uint)this.access, (uint)FileShare.None, IntPtr.Zero, (uint)FileMode.Open, (uint)pipeFlags, IntPtr.Zero);

			if (handle.IsInvalid)
			{
				int errorCode = Marshal.GetLastWin32Error();
				handle.Dispose();
				var newErrorCount = errorCodeMap.ContainsKey(errorCode) ? errorCodeMap[errorCode] + 1 : 1;
				errorCodeMap[errorCode] = newErrorCount;
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
#if !NETSTANDARD
#if NETFRAMEWORK
			var isCurrentUser = (this.pipeOptions & ~PolyfillExtensions.PipeOptionsCurrentUserOnly) != 0;
#else
			var isCurrentUser = (this.pipeOptions & ~PipeOptions.CurrentUserOnly) != 0;
#endif

			if (isCurrentUser)
			{
				return;
			}

			// TBD if we need to validate remote pipe user
			PipeSecurity accessControl = this.GetAccessControl();
			IdentityReference? remoteOwnerSid = accessControl.GetOwner(typeof(SecurityIdentifier));
			using (WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent())
			{
				SecurityIdentifier? currentUserSid = currentIdentity.Owner;
				if (remoteOwnerSid != currentUserSid)
				{
					this.IsConnected = false;
					throw new UnauthorizedAccessException();
				}
			}
#endif
		}

		private int GetPipeFlags()
		{
			// This assumes that CurrentUser only has been removed
			int pipeFlags = (int)this.pipeOptions;
			if (this.impersonationLevel != TokenImpersonationLevel.None)
			{
				pipeFlags |= SECURITYSQOSPRESENT;
				pipeFlags |= ((int)this.impersonationLevel - 1) << 16;
			}

			return pipeFlags;
		}

		private int GetAccess()
		{
			int access = 0;
			if ((PipeDirection.In & this.direction) != 0)
			{
				access |= GENERICREAD;
			}

			if ((PipeDirection.Out & this.direction) != 0)
			{
				access |= GENERICWRITE;
			}

			return access;
		}
	}
}
