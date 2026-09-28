// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;

public class ServerFactoryTests : TestBase
{
	private const string WindowsHasNoSocketPathLimit = "Windows named pipes are not backed by unix domain sockets, so no path length limit applies.";

	public ServerFactoryTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	/// <summary>
	/// Gets the maximum length (in UTF-8 bytes) that this operating system allows for the path to a unix domain socket.
	/// </summary>
	/// <remarks>
	/// Linux allows 107 bytes and macOS allows 103, in both cases one less than the size of the
	/// <c>sockaddr_un.sun_path</c> buffer, which must also hold a null terminator.
	/// </remarks>
	private static int MaxSocketPathLength => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 103 : 107;

	private static PipeOptions CurrentUserOnlyPipeOption
	{
		get
		{
#if NET5_0_OR_GREATER
			return PipeOptions.CurrentUserOnly;
#else
			return (PipeOptions)0x2000_0000;
#endif
		}
	}

	[Fact]
	[Trait("CWE", "284")]
	public async Task ConnectAsyncRetainsCurrentUserOnlyForOwnerValidation()
	{
		Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "This test requires Windows named pipes.");

		IIpcServer server = ServerFactory.Create(
			stream =>
			{
				stream.Dispose();
				return Task.CompletedTask;
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = this.CreateTestTraceSource(nameof(this.ConnectAsyncRetainsCurrentUserOnlyForOwnerValidation)),
			});

		try
		{
			using Stream clientStream = await ServerFactory.ConnectAsync(server.Name, default, this.TimeoutToken);
			PipeOptions pipeOptions = GetPipeOptions(clientStream);

			Assert.True(
				(pipeOptions & CurrentUserOnlyPipeOption) == CurrentUserOnlyPipeOption,
				$"Expected {clientStream.GetType().FullName} to retain CurrentUserOnly so owner validation runs. Actual options: {pipeOptions}.");
		}
		finally
		{
			await server.DisposeAsync();
		}
	}

	[Fact]
	public async Task TestConnection()
	{
		TaskCompletionSource<Stream> serverStreamSource = new();
		Stream? clientStream = null;
		IIpcServer server = ServerFactory.Create(
			stream =>
			{
				serverStreamSource.TrySetResult(stream);
				return Task.CompletedTask;
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = this.CreateTestTraceSource(nameof(this.TestConnection)),
			});

		try
		{
			clientStream = await ServerFactory.ConnectAsync(server.Name, default, this.TimeoutToken);
			Task writeTask = clientStream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3, this.TimeoutToken);
			byte[] buffer = new byte[3];
			Stream serverStream = await serverStreamSource.Task.WithCancellation(this.TimeoutToken);
			Task<int> bytesReadTask = serverStream.ReadAsync(buffer, 0, 3, this.TimeoutToken);
			await writeTask.WithCancellation(this.TimeoutToken);
			Assert.NotEqual(0, await bytesReadTask.WithCancellation(this.TimeoutToken));
		}
		finally
		{
			await server.DisposeAsync();
		}
	}

	[Fact]
	public async Task FactoryAllowsMultipleClients_ConcurrentCallback()
	{
		int callbackInvocations = 0;
		AsyncManualResetEvent callbackEntered1 = new();
		AsyncManualResetEvent callbackEntered2 = new();
		ManualResetEventSlim releaseCallback = new();
		IIpcServer server = ServerFactory.Create(
			stream =>
			{
				if (Interlocked.Increment(ref callbackInvocations) == 1)
				{
					callbackEntered1.Set();
				}
				else
				{
					callbackEntered2.Set();
				}

				releaseCallback.Wait();
				stream.Dispose();
				return Task.CompletedTask;
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = this.CreateTestTraceSource(nameof(this.FactoryAllowsMultipleClients_ConcurrentCallback)),
				AllowMultipleClients = true,
			});
		try
		{
			using Stream stream1 = await ServerFactory.ConnectAsync(server.Name, this.TimeoutToken);
			using Stream stream2 = await ServerFactory.ConnectAsync(server.Name, this.TimeoutToken);

			await callbackEntered1.WaitAsync(this.TimeoutToken);
			await callbackEntered2.WaitAsync(this.TimeoutToken);
			releaseCallback.Set();
			await callbackEntered2;
		}
		finally
		{
			await server.DisposeAsync();
		}
	}

	[Fact]
	public async Task FactoryAllowsOnlyOneConnection()
	{
		// The implementation of sockets on *nix preclude the possibility of limiting to just one connection.
		// So the guarantee is simply that the callback is only fired once, and any extra connection attempts
		// are ultimately disconnected.
		TaskCompletionSource<Stream> serverStreamSource = new();
		Stream? clientStream = null;
		int callbackInvocationCount = 0;
		IIpcServer server = ServerFactory.Create(
			stream =>
			{
				Interlocked.Increment(ref callbackInvocationCount);
				serverStreamSource.TrySetResult(stream);
				return Task.CompletedTask;
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = this.CreateTestTraceSource(nameof(this.FactoryAllowsOnlyOneConnection)),
			});
		Task<Stream> stream2Task;
		try
		{
			clientStream = await ServerFactory.ConnectAsync(server.Name, this.TimeoutToken);
			stream2Task = ServerFactory.ConnectAsync(server.Name, this.TimeoutToken);
			await serverStreamSource.Task.WithCancellation(this.TimeoutToken);
		}
		finally
		{
			await server.DisposeAsync();
		}

		// Now verify that the pipe still works, since we disposed the server.
		Task writeTask = clientStream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3, this.TimeoutToken);
		byte[] buffer = new byte[3];
		Stream serverStream = await serverStreamSource.Task.WithCancellation(this.TimeoutToken);
		Task<int> bytesReadTask = serverStream.ReadAsync(buffer, 0, 3, this.TimeoutToken);
		await writeTask.WithCancellation(this.TimeoutToken);
		Assert.NotEqual(0, await bytesReadTask.WithCancellation(this.TimeoutToken));

		// Assert that the second connection attempt ultimately fails.
		Stream? stream2 = null;
		try
		{
			stream2 = await stream2Task.WithCancellation(ExpectedTimeoutToken);
		}
		catch (OperationCanceledException)
		{
			// Acceptable to reject the connection. Windows does this.
		}

		if (stream2 is not null)
		{
			// On linux, .NET implements named pipes as unix domain sockets, which are impossible to allow only one connection to.
			// It turns out that the .NET runtime never closes these unwanted connections.
			// So all we can do is assert that the callback is only invoked once so at least it's a pointless connection attempt.
			// Acceptable to accept the connection, provided it disconnects soon, without sending any data. Linux does this.
			////try
			////{
			////	int bytesReadFromStream2 = await stream2.ReadAsync(new byte[1], 0, 1, this.TimeoutToken);
			////	Assert.Equal(0, bytesReadFromStream2);
			////}
			////catch (OperationCanceledException)
			////{
			////	this.Logger.WriteLine("The second connection attempt received a pipe that didn't end by returning 0 bytes. The callback was invoked {0} times.", callbackInvocationCount);
			////	throw;
			////}
			////catch (IOException)
			////{
			////	// This failure is also acceptable.
			////}

			stream2.Dispose();
		}

		Assert.Equal(1, callbackInvocationCount);
	}

	[Theory, PairwiseData]
	public async Task FactoryDeniesFutureConnectionsAfterDisposal(bool allowMultipleClients)
	{
		AsyncManualResetEvent callbackEntered = new();
		IIpcServer server = ServerFactory.Create(
			stream =>
			{
				callbackEntered.Set();
				stream.Dispose();
				return Task.CompletedTask;
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = this.CreateTestTraceSource(nameof(this.FactoryAllowsOnlyOneConnection)),
				AllowMultipleClients = allowMultipleClients,
			});
		await server.DisposeAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ServerFactory.ConnectAsync(server.Name, ExpectedTimeoutToken));
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, false)]
	[InlineData(false, true)]
	public async Task ClientCallsBeforeServerIsReady(bool failFast, bool spinningWait)
	{
		string channelName = ServerFactory.PrependPipePrefix($"{nameof(this.ClientCallsBeforeServerIsReady)}_{Guid.NewGuid():N}");
		Task<Stream> clientTask = ServerFactory.ConnectAsync(
			channelName,
			new ServerFactory.ClientOptions { FailFast = failFast, CpuSpinOverFirstChanceExceptions = spinningWait },
			this.TimeoutToken);

		IIpcServer? server = null;
		if (!failFast)
		{
			server = ServerFactory.Create(
				  stream =>
				  {
					  stream.Dispose();
					  return Task.CompletedTask;
				  },
				  new ServerFactory.ServerOptions
				  {
					  Name = channelName,
					  TraceSource = this.CreateTestTraceSource(nameof(this.ClientCallsBeforeServerIsReady)),
				  });
		}

		try
		{
			using Stream clientStream = await clientTask.WithCancellation(this.TimeoutToken);
			Assert.False(failFast);
		}
		catch (TimeoutException ex)
		{
			this.Logger.WriteLine(ex.ToString());
			Assert.True(failFast);
		}

		if (server is not null)
		{
			await server.DisposeAsync();
			await server.Completion.WithCancellation(this.TimeoutToken);
		}
	}

	[Fact]
	public async Task ServerAlreadyListening_FailsFastWhenPipeIsMissing()
	{
		string channelName = ServerFactory.PrependPipePrefix($"{nameof(this.ServerAlreadyListening_FailsFastWhenPipeIsMissing)}_{Guid.NewGuid():N}");

		// No server ever creates this pipe. The client claims the server already published the name,
		// so the connection must report the missing pipe instead of waiting for the token to be canceled.
		await Assert.ThrowsAsync<FileNotFoundException>(() => ServerFactory.ConnectAsync(
			channelName,
			new ServerFactory.ClientOptions { ServerAlreadyListening = true },
			this.TimeoutToken));
	}

	[Fact]
	public async Task ServerAlreadyListening_ConnectsToExistingServer()
	{
		AsyncManualResetEvent callbackEntered = new();
		IIpcServer server = ServerFactory.Create(
			stream =>
			{
				callbackEntered.Set();
				stream.Dispose();
				return Task.CompletedTask;
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = this.CreateTestTraceSource(nameof(this.ServerAlreadyListening_ConnectsToExistingServer)),
			});

		try
		{
			using Stream clientStream = await ServerFactory.ConnectAsync(
				server.Name,
				new ServerFactory.ClientOptions { ServerAlreadyListening = true },
				this.TimeoutToken);
			await callbackEntered.WaitAsync(this.TimeoutToken);
		}
		finally
		{
			await server.DisposeAsync();
		}

		await server.Completion.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public void Create_ThrowsWhenSocketPathExceedsPlatformLimit()
	{
		Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), WindowsHasNoSocketPathLimit);

		int limit = MaxSocketPathLength;
		string pipeName = CreateSocketPathWithByteLength(limit + 1);

		PathTooLongException ex = Assert.Throws<PathTooLongException>(
			() => ServerFactory.Create(DisposeStreamAsync, new ServerFactory.ServerOptions { Name = pipeName }));
		this.Logger.WriteLine(ex.Message);

		Assert.Contains(pipeName, ex.Message);
		Assert.Contains((limit + 1).ToString(CultureInfo.InvariantCulture), ex.Message);
		Assert.Contains(limit.ToString(CultureInfo.InvariantCulture), ex.Message);
	}

	[Fact]
	public async Task Create_AllowsSocketPathAtPlatformLimit()
	{
		Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), WindowsHasNoSocketPathLimit);

		string pipeName = CreateSocketPathWithByteLength(MaxSocketPathLength);
		this.Logger.WriteLine($"Creating a server at a path of {MaxSocketPathLength} bytes: {pipeName}");

		IIpcServer server = ServerFactory.Create(DisposeStreamAsync, new ServerFactory.ServerOptions { Name = pipeName });
		await server.DisposeAsync();
	}

	[Fact]
	public void Create_CountsSocketPathLengthInBytesRatherThanCharacters()
	{
		Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), WindowsHasNoSocketPathLimit);

		// 'é' occupies two bytes when encoded as UTF-8, so this path is within the limit when measured
		// in characters but one byte beyond it when measured the way the operating system measures it.
		string pipeName = CreateSocketPathWithByteLength(MaxSocketPathLength - 1) + "é";
		Assert.True(pipeName.Length <= MaxSocketPathLength, "The test path should be within the limit when measured in characters.");

		PathTooLongException ex = Assert.Throws<PathTooLongException>(
			() => ServerFactory.Create(DisposeStreamAsync, new ServerFactory.ServerOptions { Name = pipeName }));
		this.Logger.WriteLine(ex.Message);
	}

	[Fact]
	public async Task Create_AllowsLongPipeNamesOnWindows()
	{
		Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "This test verifies that the unix domain socket path limit is not applied to Windows named pipes.");

		// Comfortably longer than any unix domain socket path limit, but still a valid Windows pipe name.
		string pipeName = "servicehub-" + new string('a', 200);

		IIpcServer server = ServerFactory.Create(DisposeStreamAsync, new ServerFactory.ServerOptions { Name = pipeName });
		await server.DisposeAsync();
	}

	[Fact]
	public async Task ConnectAsync_ThrowsWhenSocketPathExceedsPlatformLimit()
	{
		Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), WindowsHasNoSocketPathLimit);

		string pipeName = CreateSocketPathWithByteLength(MaxSocketPathLength + 1);

		PathTooLongException ex = await Assert.ThrowsAsync<PathTooLongException>(
			() => ServerFactory.ConnectAsync(pipeName, this.TimeoutToken));
		this.Logger.WriteLine(ex.Message);

		Assert.Contains(pipeName, ex.Message);
	}

	private static Task DisposeStreamAsync(Stream stream)
	{
		stream.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Creates an absolute unix domain socket path of an exact length in UTF-8 bytes.
	/// </summary>
	/// <param name="byteLength">The required length of the path, in UTF-8 bytes.</param>
	/// <returns>A rooted path made up entirely of ASCII characters.</returns>
	/// <remarks>
	/// The path is rooted at <c>/tmp</c> rather than <see cref="Path.GetTempPath()"/> so that the test
	/// controls the total length regardless of how long the temporary directory happens to be.
	/// </remarks>
	private static string CreateSocketPathWithByteLength(int byteLength)
	{
		const string Prefix = "/tmp/servicehub-";
		return Prefix + new string('a', byteLength - Prefix.Length);
	}

	private static PipeOptions GetPipeOptions(Stream pipeStream)
	{
		FieldInfo? pipeOptionsField = pipeStream.GetType().GetField("pipeOptions", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(pipeOptionsField);

		return Assert.IsType<PipeOptions>(pipeOptionsField.GetValue(pipeStream));
	}
}
