// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// The exception that is thrown when a client connection is canceled.
/// </summary>
internal class ClientConnectionCanceledException : OperationCanceledException
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ClientConnectionCanceledException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public ClientConnectionCanceledException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
