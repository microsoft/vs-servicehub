// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A static class for creating named pipe servers.
/// </summary>
public static class ServerFactory
{
	/// <summary>
	/// The standard pipe options to use.
	/// </summary>
	internal const PipeOptions StandardPipeOptions = PipeOptions.Asynchronous | PipeOptionsEx.CurrentUserOnly;

	private const int ConnectRetryIntervalMs = 50;
	private const int MaxRetryAttemptsForFileNotFoundException = 3;

	/// <summary>
	/// The maximum length (in UTF-8 bytes) of the path to a unix domain socket on most unix-like operating systems.
	/// </summary>
	/// <remarks>
	/// Linux declares <c>sockaddr_un.sun_path</c> as a 108 byte buffer, which leaves 107 bytes for the path
	/// once the required null terminator is accounted for.
	/// </remarks>
	private const int MaxUnixDomainSocketPathLength = 107;

	/// <summary>
	/// The maximum length (in UTF-8 bytes) of the path to a unix domain socket on macOS.
	/// </summary>
	/// <remarks>
	/// The BSD-derived <c>sockaddr_un.sun_path</c> buffer that macOS uses is only 104 bytes,
	/// which leaves 103 bytes for the path once the required null terminator is accounted for.
	/// </remarks>
	private const int MaxMacDomainSocketPathLength = 103;

	/// <summary>
	/// The prefix that .NET prepends to a <em>relative</em> pipe name when deriving the path of the
	/// unix domain socket that backs the pipe.
	/// </summary>
	/// <remarks>
	/// This value is fixed in .NET itself, since changing it would prevent processes running on different
	/// versions of .NET from connecting to each other.
	/// </remarks>
	private const string DotNetPipeFilePrefix = "CoreFxPipe_";

	private static readonly string PipePrefix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe" : Path.GetTempPath();

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

		ServerOptions options = new()
		{
			TraceSource = logger,
			AllowMultipleClients = true,
			Name = pipeName,
		};
		IpcServer result = CreateCore(onConnectedCallback, options);
		return Task.FromResult<(IDisposable, string)>((result, result.Name));
	}

	/// <summary>
	/// Creates an IPC server.
	/// </summary>
	/// <param name="onConnectedCallback">
	/// Callback function to be run whenever a client connects to the server. This may be called concurrently if multiple clients connect.
	/// The delegate may choose to return right away while still using the <see cref="Stream"/> or to complete only after finishing communication with the client.
	/// </param>
	/// <param name="options">IPC server options.</param>
	/// <returns>
	/// The server, which includes a means to obtain its pipe name and monitor for completion.
	/// </returns>
	public static IIpcServer Create(Func<Stream, Task> onConnectedCallback, ServerOptions options = default)
	{
		Requires.NotNull(onConnectedCallback, nameof(onConnectedCallback));

		return CreateCore(onConnectedCallback, options);
	}

	/// <inheritdoc cref="ConnectAsync(string, ClientOptions, CancellationToken)"/>
	public static Task<Stream> ConnectAsync(string pipeName, CancellationToken cancellationToken) => ConnectAsync(pipeName, default(ClientOptions), cancellationToken);

	/// <summary>
	/// Connects to an IPC pipe that was created with <see cref="Create(Func{Stream, Task}, ServerOptions)"/>.
	/// </summary>
	/// <param name="pipeName">A fully-qualified pipe name, including the path. On Windows the prefixed path should be <c>\\.\pipe\</c>.</param>
	/// <param name="options">Options that can influence how the IPC pipe is connected to.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The duplex stream established over the pipe.</returns>
	public static async Task<Stream> ConnectAsync(string pipeName, ClientOptions options, CancellationToken cancellationToken)
	{
		Requires.NotNull(pipeName, nameof(pipeName));

		PipeOptions fullPipeOptions = StandardPipeOptions;
		PipeOptions pipeOptions = StandardPipeOptions;
		var name = TrimWindowsPrefixForDotNet(pipeName);
		ThrowIfSocketPathTooLong(name);
		var maxRetries = options.FailFast ? 0 : int.MaxValue;

		// A server creates its pipe before it hands out the name, so when the caller obtained the name from the
		// server a missing pipe means the server is gone rather than not started yet, and waiting for it to appear
		// would hang for the life of the cancellation token. A few retries still cover the moment where a server
		// that accepts multiple clients is replacing the instance that a previous client just connected to.
		int maxRetriesWhenPipeIsMissing = options.ServerAlreadyListening ? MaxRetryAttemptsForFileNotFoundException : int.MaxValue;
		PipeStream? pipeStream = null;
		try
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				pipeStream = new AsyncNamedPipeClientStream(".", name, PipeDirection.InOut, pipeOptions);
				await ((AsyncNamedPipeClientStream)pipeStream).ConnectAsync(maxRetries, maxRetriesWhenPipeIsMissing, ConnectRetryIntervalMs, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				pipeStream = new NamedPipeClientStream(".", name, PipeDirection.InOut, pipeOptions);
				await ConnectWithRetryAsync((NamedPipeClientStream)pipeStream, fullPipeOptions, cancellationToken, name, maxRetriesWhenPipeIsMissing, maxRetries, withSpinningWait: options.CpuSpinOverFirstChanceExceptions).ConfigureAwait(false);
			}

			return pipeStream;
		}
		catch
		{
			if (pipeStream is not null)
			{
				await pipeStream.DisposeAsync().ConfigureAwait(false);
			}

			throw;
		}
	}

	/// <summary>
	/// Prepends the OS-specific prefix to a simple pipe name.
	/// </summary>
	/// <param name="leafPipeName">The simple pipe name. This should <em>not</em> include a path.</param>
	/// <returns>The fully-qualified, OS-specific pipe name.</returns>
	public static string PrependPipePrefix(string leafPipeName) => Path.Combine(PipePrefix, leafPipeName);

	/// <summary>
	/// Removes the prefix from a pipe name if it is fully-qualified and on Windows where the prefix should <em>not</em> be used in the .NET APIs.
	/// </summary>
	/// <param name="fullyQualifiedPipeName">The fully-qualified path.</param>
	/// <returns>The pipe name to use with .NET APIs. This <em>may</em> still be fully-qualified.</returns>
	internal static string TrimWindowsPrefixForDotNet(string fullyQualifiedPipeName)
	{
		const string WindowsPipePrefix = @"\\.\pipe\";
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && fullyQualifiedPipeName.StartsWith(WindowsPipePrefix, StringComparison.OrdinalIgnoreCase)
			? fullyQualifiedPipeName.Substring(WindowsPipePrefix.Length)
			: fullyQualifiedPipeName;
	}

	/// <summary>
	/// Throws an exception when the unix domain socket that will back a named pipe would have a path
	/// that is too long for the operating system to bind or connect to.
	/// </summary>
	/// <param name="pipeName">The pipe name that will be handed to <see cref="NamedPipeServerStream"/> or <see cref="NamedPipeClientStream"/>.</param>
	/// <exception cref="PathTooLongException">Thrown when the socket path exceeds the limit imposed by the operating system.</exception>
	/// <remarks>
	/// <para>
	/// On unix-like operating systems .NET backs a named pipe with a unix domain socket, whose path must fit within the
	/// fixed-size <c>sockaddr_un.sun_path</c> buffer. Because <see cref="PrependPipePrefix(string)"/> roots pipe names at
	/// <see cref="Path.GetTempPath()"/>, which honors the <c>TMPDIR</c> environment variable and is itself long by default
	/// on macOS, that limit can be exceeded without the caller doing anything unusual.
	/// </para>
	/// <para>
	/// Without this check the failure surfaces deep inside the socket layer with nothing to indicate that the length of the
	/// path was the cause, and on the server it faults <see cref="IIpcServer.Completion"/> rather than the call that created
	/// the server, so it typically presents as a client that cannot connect to a server that appeared to start successfully.
	/// </para>
	/// </remarks>
	internal static void ThrowIfSocketPathTooLong(string pipeName)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			// Windows named pipes are not backed by unix domain sockets, so no comparable limit applies.
			return;
		}

		// Mirror how .NET derives the socket path from the pipe name so that the exception describes the path that would actually fail.
		string socketPath = Path.IsPathRooted(pipeName)
			? pipeName
			: Path.Combine(Path.GetTempPath(), DotNetPipeFilePrefix) + pipeName;

		// Assume the more generous limit for platforms we don't specifically recognize, so that this check
		// never rejects a path that the operating system would in fact have accepted.
		int maxLength = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MaxMacDomainSocketPathLength : MaxUnixDomainSocketPathLength;
		int actualLength = Encoding.UTF8.GetByteCount(socketPath);
		if (actualLength > maxLength)
		{
			throw new PathTooLongException($"The path '{socketPath}' is {actualLength} UTF-8 bytes long, which exceeds the {maxLength} byte limit that this operating system imposes on the unix domain socket that backs a named pipe. Use a shorter pipe name, or set the TMPDIR environment variable to a shorter path.");
		}
	}

	private static IpcServer CreateCore(Func<Stream, Task> onConnectedCallback, ServerOptions options)
	{
		return new IpcServer(options with { PipeOptions = StandardPipeOptions }, onConnectedCallback);
	}

	/// <summary>
	/// Connects to a named pipe without spinning the CPU as <see cref="NamedPipeClientStream.Connect(int)"/> or <see cref="NamedPipeClientStream.ConnectAsync(CancellationToken)"/> would do.
	/// </summary>
	/// <param name="npcs">The named pipe client stream to connect.</param>
	/// <param name="pipeOptions">The pipe options applied to this connection.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <param name="pipePath">The path of the file that backs the pipe. Non-Windows pipe names are rooted paths, so this is the pipe name itself.</param>
	/// <param name="maxRetriesWhenPipeIsMissing">The maximum number of retries to attempt while <paramref name="pipePath"/> does not exist, or <see cref="int.MaxValue"/> to not check.</param>
	/// <param name="maxRetries">The maximum number of retries to attempt.</param>
	/// <param name="withSpinningWait">Whether or not the connect should be attempted with a spinning wait.
	/// If the pipe being connected to is known to exist, it is safe to use a spinning wait to avoid potentially throwing exceptions for retries.</param>
	/// <returns>A <see cref="Task"/> that tracks the asynchronous connection attempt.</returns>
	private static async Task ConnectWithRetryAsync(NamedPipeClientStream npcs, PipeOptions pipeOptions, CancellationToken cancellationToken, string pipePath, int maxRetriesWhenPipeIsMissing, int maxRetries = int.MaxValue, bool withSpinningWait = false)
	{
		Requires.NotNull(npcs, nameof(npcs));

		ConcurrentDictionary<string, int> retryExceptions = new ConcurrentDictionary<string, int>();
		int fileNotFoundRetryCount = 0;
		int pipeMissingRetryCount = 0;
		int totalRetries = 0;

		while (true)
		{
			try
			{
				if (withSpinningWait)
				{
					await npcs.ConnectAsync(cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Try connecting without wait.
					// Connecting with anything else will consume CPU causing a spin wait.
					await npcs.ConnectAsync((int)NMPWAIT_NOWAIT).ConfigureAwait(false);
				}

#if NETFRAMEWORK
				ValidateRemotePipeUser(npcs, pipeOptions);
#endif
				return;
			}
			catch (Exception ex)
			{
				string exceptionType = ex.GetType().ToString();
				retryExceptions.AddOrUpdate(exceptionType, 1, (type, count) => count++);

				if (ex is ObjectDisposedException)
				{
					// Prefer to throw OperationCanceledException if the caller requested cancellation.
					cancellationToken.ThrowIfCancellationRequested();
					throw;
				}

				if (maxRetriesWhenPipeIsMissing < int.MaxValue)
				{
					// A missing pipe surfaces here as TimeoutException rather than FileNotFoundException, which the
					// chain below would retry forever, so the absence of the file is what identifies this case.
					// Only consecutive misses indicate a server that is gone. A server that is recreating its pipe
					// produces isolated misses, so observing the pipe again resets the count.
					if (File.Exists(pipePath))
					{
						pipeMissingRetryCount = 0;
					}
					else if (pipeMissingRetryCount++ >= maxRetriesWhenPipeIsMissing)
					{
						throw new FileNotFoundException($"The pipe '{pipePath}' does not exist.", pipePath);
					}
				}

				if (((ex is IOException && ex.HResult == HRESULT_FROM_WIN32(WIN32_ERROR.ERROR_SEM_TIMEOUT)) || ex is TimeoutException) && totalRetries < maxRetries)
				{
					// Ignore and retry.
					totalRetries++;
				}
				else if (ex is FileNotFoundException && fileNotFoundRetryCount < MaxRetryAttemptsForFileNotFoundException && totalRetries < maxRetries)
				{
					// Ignore and retry.
					totalRetries++;
					fileNotFoundRetryCount++;
				}
				else if (ex is not TimeoutException && totalRetries >= maxRetries && ex is not ObjectDisposedException)
				{
					// On Linux, the NamedPipeClientStream.ConnectAsync with a very short timeout can sometimes
					// throw exceptions other than TimeoutException (e.g. SocketException) due to a race condition
					// in the runtime. When retries are exhausted (including FailFast where maxRetries=0),
					// wrap the exception as TimeoutException to honor the FailFast contract.
					throw new TimeoutException(
						$"Failed to connect to pipe. Exception types encountered: {string.Join(", ", retryExceptions.Select(kv => $"{kv.Key}({kv.Value})"))}",
						ex);
				}
				else
				{
					throw;
				}
			}

			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				await Task.Delay(ConnectRetryIntervalMs, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				cancellationToken.ThrowIfCancellationRequested();
				throw;
			}
		}
	}

#if NETFRAMEWORK
	/// <remarks>
	/// Source code for this came from <see href="https://github.com/dotnet/runtime/blob/220437ef6591bee5907ed097b5e193a1d1235dca/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeClientStream.Windows.cs#LL136C8-L152C10">.NET source code</see>.
	/// </remarks>
	private static void ValidateRemotePipeUser(NamedPipeClientStream clientStream, PipeOptions pipeOptions)
	{
		if ((pipeOptions & PipeOptionsEx.CurrentUserOnly) != PipeOptionsEx.CurrentUserOnly)
		{
			return;
		}

		PipeSecurity accessControl = clientStream.GetAccessControl();
		IdentityReference? remoteOwnerSid = accessControl.GetOwner(typeof(SecurityIdentifier));
		using (WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent())
		{
			SecurityIdentifier? currentUserSid = currentIdentity.Owner;
			if (remoteOwnerSid != currentUserSid)
			{
				clientStream.Close();
				throw new UnauthorizedAccessException(Strings.PipeNotOwnedByCurrentUser);
			}
		}
	}
#endif

	/// <summary>
	/// Options that can influence the IPC server.
	/// </summary>
	public record struct ServerOptions
	{
		/// <summary>
		/// Gets the fully-qualified name of the pipe to accept connections to.
		/// </summary>
		/// <remarks>
		/// This should include the <c>\\.\pipe\</c> prefix on Windows, or the absolute path to a file to be created on linux/mac.
		/// </remarks>
		public string? Name { get; init; }

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
		/// Gets a value indicating whether to fail immediately with a <see cref="TimeoutException"/> if the server is not ready to accept the connection.
		/// When this is <see langword="false" />, continuously retry or wait for the server to listen for and respond to connection requests
		/// until it is canceled.
		/// </summary>
		public bool FailFast { get; init; }

		/// <summary>
		/// Gets a value indicating whether to prefer a CPU spinning wait over throwing first chance exceptions as a way to periodically sleep while waiting.
		/// </summary>
		/// <remarks>
		/// This property is only meaningful when <see cref="FailFast"/> is <see langword="false"/>.
		/// </remarks>
		public bool CpuSpinOverFirstChanceExceptions { get; init; }

		/// <summary>
		/// Gets a value indicating whether the server is known to have already created the pipe.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Set this when the pipe name came from the server itself, which only publishes the name after it begins
		/// listening. Under that condition a missing pipe proves the server is gone, so the connection fails with
		/// <see cref="FileNotFoundException"/> instead of waiting for a pipe that will never be created.
		/// A pipe that exists but has no free instance is still retried.
		/// </para>
		/// <para>
		/// This property is only meaningful when <see cref="FailFast"/> is <see langword="false"/>.
		/// </para>
		/// </remarks>
		public bool ServerAlreadyListening { get; init; }
	}
}
