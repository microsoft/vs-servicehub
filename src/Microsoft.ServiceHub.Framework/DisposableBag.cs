// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A thread-safe collection of disposable objects.
/// </summary>
/// <remarks>
/// The objects are guaranteed to be disposed exactly once when or after this collection is disposed.
/// </remarks>
internal class DisposableBag : IDisposableObservable
{
	private List<IDisposable>? disposables = new List<IDisposable>();

	/// <inheritdoc/>
	public bool IsDisposed
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
	/// Disposes of all contained links and signals the cancellation token.
	/// </summary>
	public void Dispose()
	{
		List<IDisposable>? disposables;
		lock (this)
		{
			disposables = this.disposables;
			this.disposables = null;
		}

		if (disposables is object)
		{
			List<Exception>? exceptions = null;
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

			if (exceptions is object)
			{
				throw new AggregateException(exceptions);
			}
		}
	}

	/// <summary>
	/// Arranges to dispose of a value when this <see cref="DisposableBag"/> is disposed of, or immediately if the bag is already disposed.
	/// </summary>
	/// <param name="disposable">The value to dispose.</param>
	public void AddDisposable(IDisposable disposable)
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
}
