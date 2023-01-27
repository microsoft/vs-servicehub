// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Wraps a stream to augment its capabilities.
/// </summary>
internal sealed class ServiceHubPipeStream : WrappedStream
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceHubPipeStream"/> class.
	/// </summary>
	/// <param name="stream">The stream to be wrapped.</param>
	internal ServiceHubPipeStream(PipeStream stream)
		: base(stream)
	{
		this.UpdateConnectedState();
	}

	/// <summary>
	/// Gets the stream's <see cref="SafePipeHandle"/>.
	/// </summary>
	internal SafePipeHandle SafePipeHandle => ((PipeStream)this.Stream).SafePipeHandle;

	/// <inheritdoc/>
	protected override bool GetConnected() => base.GetConnected() && ((PipeStream)this.Stream).IsConnected;
}
