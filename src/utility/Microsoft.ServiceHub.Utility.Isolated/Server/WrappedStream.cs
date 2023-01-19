// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// Wraps a <see cref="Stream"/> making it simpler to interact with it.
/// </summary>
internal class WrappedStream : Stream
{
	private readonly object disconnectedEventLock = new object();

	private bool isConnected;
	private bool isEndReached;
	private bool disposed;
	private bool hasDisconnectedEventBeenRaised;
	private EventHandler? disconnectedListeners;

	/// <summary>
	/// Initializes a new instance of the <see cref="WrappedStream"/> class.
	/// </summary>
	/// <param name="stream">The Stream to be wrapped.</param>
	public WrappedStream(Stream stream)
	{
		IsolatedUtilities.RequiresNotNull(stream, nameof(stream));
		this.Stream = stream;
	}

	/// <summary>
	/// Event that is triggered when data is read from the stream.
	/// </summary>
	public event Action<ArraySegment<byte>>? DataRead;

	/// <summary>
	/// Event that is triggered when a byte is read from the stream.
	/// </summary>
	public event Action<byte>? ByteRead;

	/// <summary>
	/// Event that is triggered when data is written to the stream.
	/// </summary>
	public event Action<ArraySegment<byte>>? DataWrite;

	/// <summary>
	/// Event that is triggered when a byte is written to the stream.
	/// </summary>
	public event Action<byte>? ByteWrite;

	/// <summary>
	/// Event that is triggered when the stream has disconnected.
	/// </summary>
	public event EventHandler Disconnected
	{
		add
		{
			if (value != null)
			{
				bool handlerAdded = false;
				lock (this.disconnectedEventLock)
				{
					if (!this.hasDisconnectedEventBeenRaised)
					{
						this.disconnectedListeners += value;
						handlerAdded = true;
					}
				}

				if (!handlerAdded)
				{
					value(this, EventArgs.Empty);
				}
				else
				{
					this.UpdateConnectedState();
				}
			}
		}

		remove
		{
			this.disconnectedListeners -= value;
		}
	}

	/// <inheritdoc/>
	public override bool CanRead => this.Stream.CanRead;

	/// <inheritdoc/>
	public override bool CanSeek => this.Stream.CanSeek;

	/// <inheritdoc/>
	public override bool CanTimeout => this.Stream.CanTimeout;

	/// <inheritdoc/>
	public override bool CanWrite => this.Stream.CanWrite;

	/// <inheritdoc/>
	public override long Length => this.Stream.Length;

	/// <inheritdoc/>
	public override long Position
	{
		get
		{
			return this.Stream.Position;
		}

		set
		{
			this.Stream.Position = value;
		}
	}

	/// <inheritdoc/>
	public override int ReadTimeout => this.Stream.ReadTimeout;

	/// <inheritdoc/>
	public override int WriteTimeout => this.Stream.WriteTimeout;

	/// <summary>
	/// Gets a value indicating whether or not the stream has been connected to.
	/// </summary>
	public bool IsConnected
	{
		get
		{
			bool result = this.GetConnected();
			this.SetConnected(result);
			return result;
		}
	}

	/// <summary>
	/// Gets a value indicating whether or not the end of the stream has been reached.
	/// </summary>
	public bool IsEndReached => this.isEndReached;

	/// <summary>
	/// Gets a value indicating whether the stream has been disposed.
	/// </summary>
	public bool IsDisposed => this.disposed;

	/// <summary>
	/// Gets the stream that is wrapped.
	/// </summary>
	protected Stream Stream { get; }

	/// <inheritdoc/>
	public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
	{
		this.UpdateConnectedState();
		return this.Stream.CopyToAsync(destination, bufferSize, cancellationToken);
	}

	/// <inheritdoc/>
	public override void Flush()
	{
		this.UpdateConnectedState();
		this.Stream.Flush();
	}

	/// <inheritdoc/>
	public override Task FlushAsync(CancellationToken cancellationToken)
	{
		this.UpdateConnectedState();
		return this.Stream.FlushAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public override int Read(byte[] buffer, int offset, int count)
	{
		this.UpdateConnectedState();
		int result = this.Stream.Read(buffer, offset, count);
		if (result == 0)
		{
			this.EndReached();
		}

		this.OnDataRead(buffer, offset, result);
		return result;
	}

	/// <inheritdoc/>
	public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		this.UpdateConnectedState();
		int result = await this.Stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
		if (result == 0)
		{
			this.EndReached();
		}

		this.OnDataRead(buffer, offset, result);
		return result;
	}

	/// <inheritdoc/>
	public override int ReadByte()
	{
		this.UpdateConnectedState();
		int result = this.Stream.ReadByte();
		if (result == -1)
		{
			this.EndReached();
		}

		this.OnDataRead((byte)result);
		return result;
	}

	/// <inheritdoc/>
	public override long Seek(long offset, SeekOrigin origin)
	{
		this.UpdateConnectedState();
		return this.Stream.Seek(offset, origin);
	}

	/// <inheritdoc/>
	public override void SetLength(long value)
	{
		this.UpdateConnectedState();
		this.Stream.SetLength(value);
	}

	/// <inheritdoc/>
	public override void Write(byte[] buffer, int offset, int count)
	{
		this.UpdateConnectedState();
		this.Stream.Write(buffer, offset, count);
		this.OnDataWrite(buffer, offset, count);
	}

	/// <inheritdoc/>
	public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		this.UpdateConnectedState();
		await this.Stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
		this.OnDataWrite(buffer, offset, count);
	}

	/// <inheritdoc/>
	public override void WriteByte(byte value)
	{
		this.UpdateConnectedState();
		this.Stream.WriteByte(value);
		this.OnDataWrite(value);
	}

	/// <summary>
	/// Gets a value indicating whether or not the stream has been connected to.
	/// </summary>
	/// <returns>True if the stream is connected to, false otherwise.</returns>
	protected virtual bool GetConnected()
	{
		return !this.isEndReached && !this.disposed;
	}

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (!this.disposed)
			{
				this.disposed = true;
				this.Stream.Dispose();
				this.UpdateConnectedState();
			}
		}

		base.Dispose(disposing);
	}

	/// <summary>
	/// Method that is called when data is read from the stream.
	/// </summary>
	/// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
	/// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
	/// <param name="count">The maximum number of bytes to be read from the current stream.</param>
	protected virtual void OnDataRead(byte[] buffer, int offset, int count) => this.DataRead?.Invoke(new ArraySegment<byte>(buffer, offset, count));

	/// <summary>
	/// Method that is called when a byte is read from the stream.
	/// </summary>
	/// <param name="charCode">The byte that was read.</param>
	protected virtual void OnDataRead(byte charCode) => this.ByteRead?.Invoke(charCode);

	/// <summary>
	/// Method that is called when data is written to the stream.
	/// </summary>
	/// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
	/// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
	/// <param name="count">The maximum number of bytes to be read from the current stream.</param>
	protected virtual void OnDataWrite(byte[] buffer, int offset, int count) => this.DataWrite?.Invoke(new ArraySegment<byte>(buffer, offset, count));

	/// <summary>
	/// Method that is called when a byte is written to the stream.
	/// </summary>
	/// <param name="charCode">The byte that was written.</param>
	protected virtual void OnDataWrite(byte charCode) => this.ByteWrite?.Invoke(charCode);

	/// <summary>
	/// Updates the connected state of the <see cref="WrappedStream"/>.
	/// </summary>
	protected void UpdateConnectedState()
	{
		this.SetConnected(this.GetConnected());
	}

	private void EndReached()
	{
		if (!this.isEndReached)
		{
			this.isEndReached = true;
			this.UpdateConnectedState();
		}
	}

	private void SetConnected(bool value)
	{
		if (this.isConnected != value)
		{
			this.isConnected = value;
			if (!value)
			{
				lock (this.disconnectedEventLock)
				{
					if (this.hasDisconnectedEventBeenRaised)
					{
						return;
					}

					this.hasDisconnectedEventBeenRaised = true;
				}

				this.disconnectedListeners?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
