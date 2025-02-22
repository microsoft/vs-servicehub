// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Xunit;
using Xunit.Abstractions;

public class ServerFactoryTests : TestBase
{
	public ServerFactoryTests(ITestOutputHelper logger)
		: base(logger)
	{
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
	public async Task TestWindowsUnableToConnect()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return;
		}

		// Stopwatch to measure wall-clock time (total elapsed time)
		var stopwatch = Stopwatch.StartNew();

		// Get the current process to measure CPU usage
		var process = Process.GetCurrentProcess();
		TimeSpan initialCpuTime = process.TotalProcessorTime;

		// Try to connect to non-existent pipe, cancel after some time
		var exceptionThrown = false;
		try
		{
			var cts = new CancellationTokenSource(5 * 1000); // Delay for 5 seconds
			await ServerFactory.ConnectAsync("NonExistentPipe", new() { CpuSpinOverFirstChanceExceptions = true }, cts.Token);
		}
		catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
		{
			// Catch the exception from the cancellation token expiring and ignore it
			exceptionThrown = true;
		}

		if (!exceptionThrown)
		{
			Assert.Fail($"Expected {nameof(TaskCanceledException)} or {nameof(OperationCanceledException)} to be thrown.");
		}

		// Stop stopwatch and measure CPU time after function completes
		stopwatch.Stop();
		TimeSpan totalCpuTime = process.TotalProcessorTime - initialCpuTime;
		double percentageCpuTime = Math.Round((totalCpuTime.TotalMilliseconds / stopwatch.Elapsed.TotalMilliseconds) * 100);

		Debug.WriteLine($"CPU Time: {totalCpuTime.TotalMilliseconds}ms, Elapsed Time: {stopwatch.Elapsed.TotalMilliseconds}ms, Percentage: {percentageCpuTime}%\n.");

		// Confirm that no more than 80% of CPU time was used
		// Set a high bar to avoid flakiness, ideally this should be in the 10-20% range
		Assert.True(percentageCpuTime < 80, $"CPU Time: {totalCpuTime.TotalMilliseconds}ms, Elapsed Time: {stopwatch.Elapsed.TotalMilliseconds}ms, Percentage: {percentageCpuTime}%");
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
		string channelName = ServerFactory.PrependPipePrefix(nameof(this.ClientCallsBeforeServerIsReady));
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
}
