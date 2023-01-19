// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Used to indicate when a failure to discover or activate a service occurs.
/// </summary>
[Serializable]
public class ServiceCompositionException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceCompositionException"/> class.
	/// </summary>
	public ServiceCompositionException()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceCompositionException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public ServiceCompositionException(string? message)
		: base(message)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceCompositionException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="inner">The inner exception.</param>
	public ServiceCompositionException(string? message, Exception? inner)
		: base(message, inner)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceCompositionException"/> class.
	/// </summary>
	/// <param name="info">Serialization info.</param>
	/// <param name="context">Serialization context.</param>
	protected ServiceCompositionException(
	  System.Runtime.Serialization.SerializationInfo info,
	  System.Runtime.Serialization.StreamingContext context)
		: base(info, context)
	{
	}
}
