// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// The exception thrown when a previously available brokered service is temporarily unavailable.
/// </summary>
[Serializable]
public class ServiceUnavailableException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceUnavailableException"/> class.
	/// </summary>
	public ServiceUnavailableException()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceUnavailableException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public ServiceUnavailableException(string? message)
		: base(message)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceUnavailableException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public ServiceUnavailableException(string? message, Exception? innerException)
		: base(message, innerException)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceUnavailableException"/> class.
	/// </summary>
	/// <param name="info">Serialization info.</param>
	/// <param name="context">Serialization context.</param>
#if NET
	[Obsolete]
#endif
	protected ServiceUnavailableException(
		System.Runtime.Serialization.SerializationInfo info,
		System.Runtime.Serialization.StreamingContext context)
		: base(info, context)
	{
	}
}
