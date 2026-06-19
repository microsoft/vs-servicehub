// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using PolyType;
using StreamJsonRpc;

public abstract partial class TestBase : IDisposable
{
	/// <summary>
	/// A reasonable amount of time to wait for async work to reasonably complete.
	/// </summary>
	protected static readonly TimeSpan AsyncDelay = TimeSpan.FromMilliseconds(250);

	private CancellationTokenSource timeoutTokenSource = new CancellationTokenSource(TestTimeout);

	public TestBase(ITestOutputHelper logger)
	{
		this.Logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	internal partial interface ITestRpcContract
	{
		/// <summary>
		/// Verifies that a multiplexing stream backs the RPC channel such that out-of-band streams can be exchanged.
		/// </summary>
		/// <param name="stream">The stream to read from.</param>
		/// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
		/// <returns>A task that represents the asynchronous read operation. The task result contains the read bytes.</returns>
		Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Gets a value indicating whether the test is running on the Mono runtime.
	/// </summary>
	protected internal static bool IsOnMono => Type.GetType("Mono.Runtime") != null;

	protected static CancellationToken ExpectedTimeoutToken => new CancellationTokenSource(AsyncDelay).Token;

	protected static CancellationToken UnexpectedTimeoutToken => Debugger.IsAttached ? CancellationToken.None : new CancellationTokenSource(TestTimeout).Token;

	protected static TimeSpan TestTimeout => Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);

	protected CancellationToken TimeoutToken => this.timeoutTokenSource.Token;

	protected ITestOutputHelper Logger { get; }

	public void Dispose()
	{
		this.Dispose(true);
	}

	/// <summary>
	/// Skips a test when <see cref="IsOnMono"/>.
	/// </summary>
	/// <param name="unsupportedFeature">Names the feature that fails on mono so the skipped test can log about the reason.</param>
	protected static void SkipOnMono(string unsupportedFeature)
	{
		Assert.SkipWhen(IsOnMono, "Test marked as skipped on Mono runtime due to feature: " + unsupportedFeature);
	}

	/// <summary>
	/// Pauses until a debugger is attached when on Linux.
	/// </summary>
	protected async Task WaitForDebuggerAsync()
	{
#if NET
		if (OperatingSystem.IsLinux())
		{
			Console.WriteLine($"Waiting for debugger to attach to PID {Process.GetCurrentProcess().Id}...");
			while (!Debugger.IsAttached)
			{
				await Task.Delay(500);
			}

			Console.WriteLine("...attached");

			// Renew the TimeoutToken, which now with the debugger attached will be set to infinity.
			this.timeoutTokenSource = new(TestTimeout);
		}
#else
		await Task.Yield(); // suppress async warning
#endif
	}

	protected async Task HostMultiplexingServerAsync(Stream stream, Func<MultiplexingStream, IRemoteServiceBroker> serverFactory, CancellationToken cancellationToken)
	{
		Requires.NotNull(stream, nameof(stream));
		Requires.NotNull(serverFactory, nameof(serverFactory));

		using (MultiplexingStream mx = await MultiplexingStream.CreateAsync(stream, this.CreateTestMXStreamOptions(isServer: true), cancellationToken))
		{
			MultiplexingStream.Channel defaultChannel = await mx.OfferChannelAsync(string.Empty, cancellationToken);
			FrameworkServices.RemoteServiceBroker
				.WithTraceSource(this.CreateTestTraceSource("server", SourceLevels.All))
				.ConstructRpc(serverFactory(mx), defaultChannel);
			await defaultChannel.Completion.WithCancellation(cancellationToken);
		}
	}

	protected MultiplexingStream.Options CreateTestMXStreamOptions(bool isServer = false)
	{
		string role = isServer ? "Server" : "Client";
		return new MultiplexingStream.Options
		{
			TraceSource = this.CreateTestTraceSource($"MX {role}"),
			DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => this.CreateTestTraceSource($"MX {role} channel {id}"),
		};
	}

	protected TraceSource CreateTestTraceSource(string name, SourceLevels levels = SourceLevels.Information)
	{
		return new TraceSource(name, levels)
		{
			Listeners =
			{
				new XunitTraceListener(this.Logger),
			},
		};
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			this.timeoutTokenSource.Dispose();
		}
	}

	private protected async Task AssertBackingMultiplexingStreamAsync(ServiceRpcDescriptor descriptor)
	{
		(IDuplexPipe client, IDuplexPipe server) = FullDuplexStream.CreatePipePair();

		descriptor.ConstructRpc(new TestRpcService(), server);
		ITestRpcContract clientProxy = descriptor.ConstructRpc<ITestRpcContract>(client);

		MemoryStream ms = new([1, 2, 3]);
		byte[] result = await clientProxy.ReadStreamAsync(ms, this.TimeoutToken);
		Assert.Equal(ms.ToArray(), result);

		(clientProxy as IDisposable)?.Dispose();
	}

	internal class TestRpcService : ITestRpcContract
	{
		public async Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken cancellationToken)
		{
			List<byte> result = [];

			byte[] buffer = new byte[1024];
			int bytesRead;
			do
			{
				bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
				result.AddRange([.. buffer.AsSpan(0, bytesRead)]);
			}
			while (bytesRead > 0);

			return [.. result];
		}
	}
}
