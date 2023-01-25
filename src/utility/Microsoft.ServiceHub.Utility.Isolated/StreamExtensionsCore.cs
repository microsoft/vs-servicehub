// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A class containing the core implementations for stream extensions. This exists so that we don't run into any type errors with types that are
/// shared between assemblies within DevCore. Internally in DevCore the "Core" implementation should be used while externally the public implementations are used instead.
/// </summary>
internal static class StreamExtensionsCore
{
	/// <summary>
	/// Attempts to get the handle of ServiceHub stream.
	/// </summary>
	/// <param name="stream">The stream to get the handle of.</param>
	/// <param name="handle">The handle of the stream if it exists, null otherwise.</param>
	/// <returns>True if the stream has a <see cref="SafePipeHandle"/>, false otherwise.</returns>
	internal static bool TryGetHandleCore(this Stream? stream, [NotNullWhen(true)] out SafePipeHandle? handle)
	{
		if (stream is ServiceHubPipeStream devHubPipeStream)
		{
			handle = devHubPipeStream.SafePipeHandle;
			return true;
		}

		if (stream is PipeStream pipeStream)
		{
			handle = pipeStream.SafePipeHandle;
			return true;
		}

		handle = null;
		return false;
	}
}
