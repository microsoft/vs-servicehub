
using System.ComponentModel;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using static Nerdbank.Streams.MultiplexingStream;

namespace Microsoft.ServiceHub.Framework
{
	[SupportedOSPlatform("windows")]
	public class AsyncNamedPipeClientStream// : PipeStream
	{
#if NETSTANDARD
		private const int DefaultPipeAccessRights = 131483;
#else
		private const int DefaultPipeAccessRights = (int)PipeAccessRights.ReadWrite;
#endif
		private const int SECURITY_SQOS_PRESENT = 0x00100000;
		private const int ERROR_FILE_NOT_FOUND = 0x2;
		private const int ERROR_PIPE_BUSY = 0xE7;
		private const int ERROR_SEM_TIMEOUT = 0x79;
		private const int GENERIC_READ = unchecked(((int)0x80000000));
		private const int GENERIC_WRITE = (0x40000000);
		private readonly string? normalizedPipePath;
		private readonly TokenImpersonationLevel impersonationLevel;
		private readonly System.IO.Pipes.PipeOptions pipeOptions;
		private readonly HandleInheritability inheritability;
		private readonly PipeDirection direction;
		private readonly int accessRights;
		private SafePipeHandle pipeHandle;
		private ThreadPoolBoundHandle threadPoolHandle;

		public bool IsCurrentUserOnly { get; } = false;

		public PipeStatus CurrentPipeStatus { get; private set; }

		public enum PipeStatus
		{
			Open = 0,
			Waiting = 1,
			Closed = 2,
			Connectd = 3,
		}

		public AsyncNamedPipeClientStream(
			string serverName,
			string normalizedPipePath,
			PipeDirection direction,
			System.IO.Pipes.PipeOptions options,
			TokenImpersonationLevel impersionationLevel = TokenImpersonationLevel.None,
			HandleInheritability inheritability = HandleInheritability.None,
			int accessRights = DefaultPipeAccessRights)
		{
			this.normalizedPipePath = normalizedPipePath;
			this.impersonationLevel = impersionationLevel;
			this.pipeOptions = options;
			this.inheritability = inheritability;
			this.direction = direction;
			this.accessRights = accessRights;

			this.IsCurrentUserOnly = this.GetIsCurrentUserOnly(options);
			this.CurrentPipeStatus = PipeStatus.Waiting;
		}

		public async Task ConnectAsync(CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				if (this.TryConnect(50))
				{
					break;
				}

				await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
			}
		}

		// Declare CreateNamedPipe function from kernel32.dll
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern IntPtr CreateNamedPipe(
			string lpName,
			uint dwOpenMode,
			uint dwPipeMode,
			uint nMaxInstances,
			uint nOutBufferSize,
			uint nInBufferSize,
			uint nDefaultTimeOut,
			ref SECURITY_ATTRIBUTES lpSecurityAttributes);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool WaitNamedPipe(
			  string lpNamedPipeName,
			  uint nTimeOut);

		private static SafePipeHandle CreateNamedPipeClient(string? path, ref SECURITY_ATTRIBUTES secAttrs, int pipeFlags, int access)
		{
			IntPtr handle = CreateNamedPipe(path ?? string.Empty, (uint)FileMode.Open, (int)PipeTransmissionMode.Message, 1, 4096, 4096, 100, ref secAttrs);
			bool ownsHandle = true;
			return new SafePipeHandle(handle, ownsHandle);
		}

		private bool TryConnect(int timeout)
		{
			var pipeFlags = this.RemoveCurrentUserSettingFromPipeOptions();
			if (this.impersonationLevel != TokenImpersonationLevel.None)
			{
				pipeFlags |= SECURITY_SQOS_PRESENT;
				pipeFlags |= ((int)this.impersonationLevel - 1) << 16;
			}

			int access = 0;
			if ((PipeDirection.In & this.direction) != 0)
			{
				access |= GENERIC_READ;
			}
			if ((PipeDirection.Out & this.direction) != 0)
			{
				access |= GENERIC_WRITE;
			}


			var secAttrs = this.GetDefaultSecurityAttributes();

			SafePipeHandle handle = CreateNamedPipeClient(this.normalizedPipePath, ref secAttrs, pipeFlags, access);

			if (handle.IsInvalid)
			{
				int errorCode = this.GetLastErrorCode();

				handle.Dispose();

				// CreateFileW: "If the CreateNamedPipe function was not successfully called on the server prior to this operation,
				// a pipe will not exist and CreateFile will fail with ERROR_FILE_NOT_FOUND"
				// WaitNamedPipeW: "If no instances of the specified named pipe exist,
				// the WaitNamedPipe function returns immediately, regardless of the time-out value."
				// We know that no instances exist, so we just quit without calling WaitNamedPipeW.
				if (errorCode == ERROR_FILE_NOT_FOUND)
				{
					return false;
				}

				if (errorCode != ERROR_PIPE_BUSY)
				{
					throw new Win32Exception(errorCode);
				}

				if (WaitNamedPipe(this.normalizedPipePath ?? string.Empty, Convert.ToUInt32(timeout)))
				{
					errorCode = this.GetLastErrorCode();

					if (errorCode == ERROR_FILE_NOT_FOUND || // server has been closed
						errorCode == ERROR_SEM_TIMEOUT)
					{
						return false;
					}

					throw new Win32Exception(errorCode);
				}

				// Pipe server should be free. Let's try to connect to it.
				handle = CreateNamedPipeClient(this.normalizedPipePath, ref secAttrs, pipeFlags, this.accessRights);

				if (handle.IsInvalid)
				{
					errorCode = this.GetLastErrorCode();

					handle.Dispose();

					// WaitNamedPipe: "A subsequent CreateFile call to the pipe can fail,
					// because the instance was closed by the server or opened by another client."
					if (errorCode == ERROR_PIPE_BUSY || // opened by another client
						errorCode == ERROR_FILE_NOT_FOUND) // server has been closed
					{
						return false;
					}

					throw new Win32Exception(errorCode);
				}
			}

			// Success!
#if !NETSTANDARD
			var boundHandle = ThreadPoolBoundHandle.BindHandle(handle);
			this.pipeHandle = handle;
			this.threadPoolHandle = boundHandle;
#endif

			// State = PipeState.Connected;
			this.CurrentPipeStatus = PipeStatus.Connectd;
			this.ValidateRemotePipeUser();
			return true;
		}

		private void ValidateRemotePipeUser()
		{
#if !NETSTANDARD
			if (!this.IsCurrentUserOnly)
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
					this.CurrentPipeStatus = PipeStatus.Closed;
					throw new UnauthorizedAccessException();
				}
			}
#endif
		}

#if !NETSTANDARD
		private PipeSecurity GetAccessControl()
		{
			var ps = new PipeSecurity();
			// var ps = new PipeSecurity(this.pipeHandle, AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
			// TODO: access rules
			return ps;
		}
#endif

		private SECURITY_ATTRIBUTES GetDefaultSecurityAttributes()
		{
			return new SECURITY_ATTRIBUTES
			{
				nLength = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
				lpSecurityDescriptor = IntPtr.Zero,
				bInheritHandle = this.inheritability == HandleInheritability.Inheritable,
			};
		}

		private int GetLastErrorCode()
		{
#if NET5_0_OR_GREATER
			int errorCode = Marshal.GetLastPInvokeError();
#else
			int errorCode = Marshal.GetLastWin32Error();
#endif
			return errorCode;
		}

		private bool GetIsCurrentUserOnly(System.IO.Pipes.PipeOptions options)
		{
#if NET5_0_OR_GREATER
			if ((options & System.IO.Pipes.PipeOptions.CurrentUserOnly) != 0)
			{
				return true;
			}
#else
			if ((options & PolyfillExtensions.PipeOptionsCurrentUserOnly) != 0)
			{
				return true;
			}
#endif
			return false;
		}

		private int RemoveCurrentUserSettingFromPipeOptions()
		{
#if NET5_0_OR_GREATER
			int pipeFlags = (int)(this.pipeOptions & ~System.IO.Pipes.PipeOptions.CurrentUserOnly);
#else
			int pipeFlags = (int)(this.pipeOptions & ~PolyfillExtensions.PipeOptionsCurrentUserOnly);
#endif
			return pipeFlags;
		}

		// Define the SECURITY_ATTRIBUTES structure
		[StructLayout(LayoutKind.Sequential)]
		[ComVisible(true)]
		private struct SECURITY_ATTRIBUTES
		{
			public uint nLength;
			public IntPtr lpSecurityDescriptor;
			public bool bInheritHandle;
		}
	}
}
