// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.ServiceHub.Utility;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using IPC = System.IO.Pipes;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An <see cref="IRemoteServiceBroker"/> which proffers all services from another <see cref="IServiceBroker"/>
/// over named pipes on Windows or Unix domain sockets on other operating systems.
/// </summary>
public class IpcRelayServiceBroker : IRemoteServiceBroker, IDisposable
{
	private readonly IServiceBroker serviceBroker;

	/// <summary>
	/// An event to set upon disposal.
	/// </summary>
	private readonly AsyncManualResetEvent disposedEvent = new AsyncManualResetEvent();

	private ImmutableDictionary<Guid, IDisposable> remoteServiceRequests = ImmutableDictionary<Guid, IDisposable>.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="IpcRelayServiceBroker"/> class.
	/// </summary>
	/// <param name="serviceBroker">The service broker whose services are to be exposed.</param>
	public IpcRelayServiceBroker(IServiceBroker serviceBroker)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		this.serviceBroker = serviceBroker;
	}

	/// <inheritdoc/>
	public event EventHandler<BrokeredServicesChangedEventArgs> AvailabilityChanged
	{
		add => this.serviceBroker.AvailabilityChanged += value;
		remove => this.serviceBroker.AvailabilityChanged -= value;
	}

	/// <summary>
	/// Gets a <see cref="Task"/> that completes when this instance is disposed of.
	/// </summary>
	/// <remarks>
	/// This event will occur when the client disconnects from the relay,
	/// if the RPC library is configured to dispose target objects at that time.
	/// </remarks>
	public Task Completion => this.disposedEvent.WaitAsync();

	/// <inheritdoc />
	public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
	{
		if (!clientMetadata.SupportedConnections.HasFlag(RemoteServiceConnections.IpcPipe))
		{
			// Only support pipes.
			throw new NotSupportedException();
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken = default)
	{
		var faultOrCancelBag = new DisposableBag();
		try
		{
			IDuplexPipe? servicePipe = await this.serviceBroker.GetPipeAsync(serviceMoniker, serviceActivationOptions, cancellationToken).ConfigureAwait(false);
			if (servicePipe == null)
			{
				return default;
			}

			faultOrCancelBag.AddDisposable(new DisposeAction(() =>
			{
				servicePipe.Input?.Complete();
				servicePipe.Output?.Complete();
			}));

			string pipeName = Guid.NewGuid().ToString();
			var ipcPipeStream = new NamedPipeServerStream(
				pipeName,
				PipeDirection.InOut,
				maxNumberOfServerInstances: 1,
				PipeTransmissionMode.Byte,
				IPC.PipeOptions.Asynchronous);
			faultOrCancelBag.AddDisposable(ipcPipeStream);

			var requestId = Guid.NewGuid();
			ImmutableInterlocked.TryAdd(ref this.remoteServiceRequests, requestId, faultOrCancelBag);

			// Nothing related to responding to the named pipe call should honor the CancellationToken for this outer call,
			// because the outer call will be long done with by the time a request comes in for this named pipe.
			Task waitForConnectionTask = ipcPipeStream.WaitForConnectionAsync(CancellationToken.None);
			_ = Task.Run(async delegate
			{
				await waitForConnectionTask.NoThrowAwaitable(false);

				// Once a connection is made (or fails), it is no longer cancelable.
				ImmutableInterlocked.TryRemove(ref this.remoteServiceRequests, requestId, out IDisposable _);

				// Clean up if it failed.
				if (waitForConnectionTask.IsFaulted)
				{
					faultOrCancelBag.Dispose();
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks (this one was created by our parent context)
					await waitForConnectionTask.ConfigureAwait(false); // rethrow exception.
#pragma warning restore VSTHRD003 // Avoid awaiting foreign Tasks
				}

				// Link the two pipes so that all incoming/outgoing calls get forwarded
				IDuplexPipe ipcPipe = ipcPipeStream.UsePipe();
				await Task.WhenAll(
					LinkAsync(ipcPipe.Input, servicePipe.Output),
					LinkAsync(servicePipe.Input, ipcPipe.Output)).ConfigureAwait(false);
			});

			return new RemoteServiceConnectionInfo
			{
				RequestId = requestId,
				PipeName = pipeName,
			};
		}
		catch
		{
			faultOrCancelBag.Dispose();
			throw;
		}
	}

	/// <inheritdoc />
	public Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		if (ImmutableInterlocked.TryRemove(ref this.remoteServiceRequests, serviceRequestId, out IDisposable? disposable))
		{
			disposable.Dispose();
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		this.Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Disposes managed and unmanaged resources owned by this instance.
	/// </summary>
	/// <param name="disposing"><see langword="true" /> if this object is being disposed; <see langword="false" /> if it is being finalized.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			this.disposedEvent.Set();
		}
	}

	/// <summary>
	/// Copies all bytes from a <see cref="PipeReader"/> to a <see cref="PipeWriter"/>.
	/// </summary>
	/// <param name="reader">The reader to copy from.</param>
	/// <param name="writer">The writer to copy to.</param>
	/// <returns>A <see cref="Task"/> that completes on error or when the <paramref name="reader"/> has completed and all bytes have been written to the <paramref name="writer"/>.</returns>
	private static async Task LinkAsync(PipeReader reader, PipeWriter writer)
	{
		Requires.NotNull(reader, nameof(reader));
		Requires.NotNull(writer, nameof(writer));

		try
		{
			while (true)
			{
				ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
				foreach (ReadOnlyMemory<byte> sourceMemory in result.Buffer)
				{
					writer.Write(sourceMemory.Span);
					await writer.FlushAsync().ConfigureAwait(false);
				}

				reader.AdvanceTo(result.Buffer.End);
				if (result.IsCompleted)
				{
					break;
				}
			}

			await writer.CompleteAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await writer.CompleteAsync(ex).ConfigureAwait(false);
		}
	}
}
