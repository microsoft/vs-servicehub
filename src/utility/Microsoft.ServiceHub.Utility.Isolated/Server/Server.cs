// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A server that invokes a callback whenever a client connects to it.
/// </summary>
internal abstract class Server : IDisposable, IAsyncDisposable
{
	private readonly Func<WrappedStream, Task> createAndConfigureService;

	private readonly object streamsLock = new object();
	private readonly List<Stream> streams = new List<Stream>();

	/// <summary>
	/// Initializes a new instance of the <see cref="Server"/> class.
	/// </summary>
	/// <param name="logger">A trace source to be used for logging.</param>
	/// <param name="createAndConfigureService">The callback to be invoked when a client connects to the server.</param>
	internal Server(TraceSource? logger, Func<WrappedStream, Task> createAndConfigureService)
	{
		this.Logger = logger ?? new TraceSource("ServiceHub.Framework pipe server", SourceLevels.Off);
		this.createAndConfigureService = createAndConfigureService;
	}

	/// <summary>
	/// Gets a value indicating whether or not server is disposed. Used for Unit Testing.
	/// </summary>
	internal bool IsDisposed { get; private set; } = false;

	/// <summary>
	/// Gets a trace source used for logging.
	/// </summary>
	protected TraceSource Logger { get; }

	/// <summary>
	/// Gets a value indicating whether or not clients are currently connected to the server.
	/// </summary>
	protected bool HasClients
	{
		get
		{
			lock (this.streamsLock)
			{
				return this.streams.Count > 0;
			}
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
		this.DisposeAsyncCore().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
	}

	/// <summary>
	/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
	/// </summary>
	/// <returns>A task tracking the work.</returns>
	public async ValueTask DisposeAsync()
	{
		await this.DisposeAsyncCore().ConfigureAwait(false);
	}

	/// <summary>
	/// Implements the core disposal logic to be used by the class.
	/// </summary>
	/// <returns>A task tracking the work.</returns>
#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods
	protected virtual Task DisposeAsyncCore()
#pragma warning restore VSTHRD200 // Use "Async" suffix for async methods
	{
		this.IsDisposed = true;
		GC.SuppressFinalize(this);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Method that is called when a client disconnects from the server.
	/// </summary>
	/// <param name="stream">The stream that was disconnected from.</param>
	protected virtual void ClientDisconnected(Stream stream)
	{
		IsolatedUtilities.RequiresNotNull(stream, nameof(stream));

		lock (this.streamsLock)
		{
			this.streams.Remove(stream);
		}
	}

	/// <summary>
	/// Method that is called when a client connects to the server.
	/// </summary>
	/// <param name="stream">The stream that was connected to.</param>
	/// <returns>A task tracking the work.</returns>
#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods
	protected async Task ClientConnected(WrappedStream stream)
#pragma warning restore VSTHRD200 // Use "Async" suffix for async methods
	{
		IsolatedUtilities.RequiresNotNull(stream, nameof(stream));

		try
		{
			await this.createAndConfigureService(stream).ConfigureAwait(false);
			if (!stream.IsConnected)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
				this.ClientDisconnected(stream);
				return;
			}

			lock (this.streamsLock)
			{
				this.streams.Add(stream);
			}

			stream.Disconnected += this.StreamDisconnected;
		}
		catch (Exception exception)
		{
			await stream.DisposeAsync().ConfigureAwait(false);

			if (exception is ClientConnectionCanceledException)
			{
				this.Logger.TraceInformation("A new connection was cancelled: {0}", exception.GetMessageWithInnerExceptions());
			}
			else
			{
				this.Logger.TraceException(exception);
			}
		}
	}

	private void StreamDisconnected(object? sender, EventArgs e)
	{
		if (sender is null)
		{
			throw new ArgumentNullException(nameof(sender));
		}

		var stream = (WrappedStream)sender;
		stream.Disconnected -= this.StreamDisconnected;
		this.ClientDisconnected(stream);
	}
}
