// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public abstract class TestBase : IDisposable
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
	/// Causes a <see cref="SkippableFactAttribute"/> based test to skip if <see cref="IsOnMono"/>.
	/// </summary>
	/// <param name="unsupportedFeature">Names the feature that fails on mono so the skipped test can log about the reason.</param>
	protected static void SkipOnMono(string unsupportedFeature)
	{
		Skip.If(IsOnMono, "Test marked as skipped on Mono runtime due to feature: " + unsupportedFeature);
	}

	/// <summary>
	/// Pauses until a debugger is attached when on Linux.
	/// </summary>
	protected async Task WaitForDebuggerAsync()
	{
#if NET6_0_OR_GREATER
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
			FrameworkServices.RemoteServiceBroker.ConstructRpc(serverFactory(mx), defaultChannel);
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
}
