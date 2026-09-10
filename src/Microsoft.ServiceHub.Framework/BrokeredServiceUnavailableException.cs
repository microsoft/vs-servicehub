// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// The exception thrown when a previously available brokered service is temporarily unavailable.
/// </summary>
[Serializable]
public class BrokeredServiceUnavailableException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BrokeredServiceUnavailableException"/> class.
	/// </summary>
	public BrokeredServiceUnavailableException()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BrokeredServiceUnavailableException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public BrokeredServiceUnavailableException(string? message)
		: base(message)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BrokeredServiceUnavailableException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public BrokeredServiceUnavailableException(string? message, Exception? innerException)
		: base(message, innerException)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BrokeredServiceUnavailableException"/> class.
	/// </summary>
	/// <param name="info">Serialization info.</param>
	/// <param name="context">Serialization context.</param>
#if NET
	[Obsolete]
#endif
	protected BrokeredServiceUnavailableException(
		System.Runtime.Serialization.SerializationInfo info,
		System.Runtime.Serialization.StreamingContext context)
		: base(info, context)
	{
	}
}
