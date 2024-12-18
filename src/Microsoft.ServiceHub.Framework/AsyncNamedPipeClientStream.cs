
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
	public class AsyncNamedPipeClientStream : PipeStream
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
		private readonly string? pipePath;
		private readonly TokenImpersonationLevel impersonationLevel;
		private readonly System.IO.Pipes.PipeOptions pipeOptions;
		private readonly HandleInheritability inheritability;
		private readonly PipeDirection direction;
		private readonly int accessRights;
		private SafePipeHandle? pipeHandle;
		private ThreadPoolBoundHandle? threadPoolHandle;

		public bool IsCurrentUserOnly { get; } = false;

		public PipeStatus CurrentPipeStatus { get; private set; }

		public enum PipeStatus
		{
			Error = 0,
			Pending = 1,
			Connected = 2,
		}

		public AsyncNamedPipeClientStream(
			string serverName,
			string pipeName,
			PipeDirection direction,
			System.IO.Pipes.PipeOptions options,
			TokenImpersonationLevel impersionationLevel = TokenImpersonationLevel.None,
			HandleInheritability inheritability = HandleInheritability.None,
			int accessRights = DefaultPipeAccessRights)
			: base(direction, 4096)
		{
			this.pipePath = GetPipePath(serverName, pipeName);
			this.impersonationLevel = impersionationLevel;
			this.pipeOptions = options;
			this.inheritability = inheritability;
			this.direction = direction;
			this.accessRights = accessRights;

			this.IsCurrentUserOnly = this.GetIsCurrentUserOnly(options);
			this.CurrentPipeStatus = PipeStatus.Pending;
		}

		public async Task ConnectAsync(
			CancellationToken cancellationToken,
			int delayScaleMultiple = 100,
			int delayMax = 5 * 60 * 1000,
			int maxRetries = 5)
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
				await Task.Delay(Math.Min(delayMax, delayScaleMultiple * retries), cancellationToken).ConfigureAwait(false);
			}

			if (retries > maxRetries || !this.IsConnected)
			{
				throw new TimeoutException($"Failed with errors: {string.Join(", ", errorCodeMap.Select(x => $"(code: {x.Key}, count: {x.Value})"))}");
			}
		}

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true)]
		private static extern SafePipeHandle CreateNamedPipeClient(
			string? lpFileName,
			int dwDesiredAccess,
			System.IO.FileShare dwShareMode,
			ref SECURITY_ATTRIBUTES secAttrs,
			FileMode dwCreationDisposition,
			int dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		private bool TryConnect(ref Dictionary<int, int> errorCodeMap)
		{
			var pipeFlags = this.GetPipeFlags();
			var access = this.GetAccess();
			SECURITY_ATTRIBUTES secAttrs = this.GetSecurityAttributes();

			SafePipeHandle handle = CreateNamedPipeClient(this.pipePath, access, FileShare.None, ref secAttrs, FileMode.Open, pipeFlags, IntPtr.Zero);

			if (handle.IsInvalid)
			{
				int errorCode = this.GetLastErrorCode();
				handle.Dispose();
				var newErrorCount = errorCodeMap.ContainsKey(errorCode) ? errorCodeMap[errorCode] + 1 : 1;
				errorCodeMap[errorCode] = newErrorCount;
				return false;
			}

			// Success!
			var boundHandle = ThreadPoolBoundHandle.BindHandle(handle);
			this.pipeHandle = handle;
			this.threadPoolHandle = boundHandle;

			this.CurrentPipeStatus = PipeStatus.Connected;
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
					this.IsConnected = false;
					throw new UnauthorizedAccessException();
				}
			}
#endif
		}

		private int GetPipeFlags()
		{
#if NET5_0_OR_GREATER
			int pipeFlags = (int)(this.pipeOptions & ~System.IO.Pipes.PipeOptions.CurrentUserOnly);
#else
			int pipeFlags = (int)(this.pipeOptions & ~PolyfillExtensions.PipeOptionsCurrentUserOnly);
#endif
			if (this.impersonationLevel != TokenImpersonationLevel.None)
			{
				pipeFlags |= SECURITY_SQOS_PRESENT;
				pipeFlags |= ((int)this.impersonationLevel - 1) << 16;
			}

			return pipeFlags;
		}

		private int GetAccess()
		{
			int access = 0;
			if ((PipeDirection.In & this.direction) != 0)
			{
				access |= GENERIC_READ;
			}

			if ((PipeDirection.Out & this.direction) != 0)
			{
				access |= GENERIC_WRITE;
			}

			return access;
		}

		private SECURITY_ATTRIBUTES GetSecurityAttributes()
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

		private static string GetPipePath(string serverName, string pipeName)
		{
			return Path.GetFullPath(@"\\" + serverName + @"\pipe\" + pipeName);
		}

		public void Dispose()
		{
			if (this.pipeHandle is not null)
			{
				this.pipeHandle.Dispose();
			}

			if (this.threadPoolHandle is not null)
			{
				this.threadPoolHandle.Dispose();
			}

			this.pipeHandle = null;
			this.threadPoolHandle = null;

			GC.SuppressFinalize(this);
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
