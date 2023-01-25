// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A thread-safe collection of disposable objects.
/// </summary>
/// <remarks>
/// The objects are guaranteed to be disposed exactly once when or after this collection is disposed.
/// </remarks>
internal class DisposableBag : IAsyncDisposable
{
	private List<IAsyncDisposable>? asyncDisposables = new List<IAsyncDisposable>();
	private List<IDisposable>? disposables = new List<IDisposable>();

	/// <summary>
	/// Gets a value indicating whether this bag has already been disposed.
	/// </summary>
	internal bool IsDisposed
	{
		get
		{
			lock (this)
			{
				return this.disposables is null;
			}
		}
	}

	/// <summary>
	/// Disposes of all contained values.
	/// </summary>
	/// <returns>A task that completes after all values have been disposed.</returns>
	public async ValueTask DisposeAsync()
	{
		List<IAsyncDisposable>? asyncDisposables;
		List<IDisposable>? disposables;
		lock (this)
		{
			disposables = this.disposables;
			asyncDisposables = this.asyncDisposables;
			this.disposables = null;
			this.asyncDisposables = null;
		}

		List<Exception>? exceptions = null;
		if (disposables is object)
		{
			foreach (IDisposable disposable in disposables)
			{
				try
				{
					disposable.Dispose();
				}
				catch (Exception ex)
				{
					if (exceptions is null)
					{
						exceptions = new List<Exception>();
					}

					exceptions.Add(ex);
				}
			}
		}

		if (asyncDisposables is object)
		{
			foreach (IAsyncDisposable disposable in asyncDisposables)
			{
				try
				{
					await disposable.DisposeAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					if (exceptions is null)
					{
						exceptions = new List<Exception>();
					}

					exceptions.Add(ex);
				}
			}
		}

		if (exceptions is object)
		{
			throw new AggregateException(exceptions);
		}
	}

	/// <summary>
	/// Arranges to dispose of a value when this <see cref="DisposableBag"/> is disposed of, or immediately if the bag is already disposed.
	/// </summary>
	/// <param name="disposable">The value to dispose.</param>
	internal void AddDisposable(IDisposable disposable)
	{
		Requires.NotNull(disposable, nameof(disposable));

		bool added = false;
		lock (this)
		{
			if (this.disposables is object)
			{
				this.disposables.Add(disposable);
				added = true;
			}
		}

		if (!added)
		{
			disposable.Dispose();
		}
	}

	/// <summary>
	/// Arranges to dispose of a value when this <see cref="DisposableBag"/> is disposed of.
	/// </summary>
	/// <param name="disposable">The value to dispose.</param>
	/// <returns><see langword="true"/> if the value was added to the bag; <see langword="false" /> if the bag was already disposed and the caller must dispose of this value.</returns>
	internal bool TryAddDisposable(IAsyncDisposable disposable)
	{
		Requires.NotNull(disposable, nameof(disposable));

		bool added = false;
		lock (this)
		{
			if (this.asyncDisposables is object)
			{
				this.asyncDisposables.Add(disposable);
				added = true;
			}
		}

		return added;
	}
}
