// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.Threading;

internal class Calculator : ICalculator, IDisposable
{
	private readonly AsyncManualResetEvent disposedEvent = new AsyncManualResetEvent();

	public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);

	void IDisposable.Dispose() => this.disposedEvent.Set();

	internal Task WaitForDisposalAsync(CancellationToken cancellationToken) => this.disposedEvent.WaitAsync().WithCancellation(cancellationToken);
}
