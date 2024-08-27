// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Used to indicate a failure in a <see cref="IServiceBroker"/> to activate a service that was found.
/// </summary>
[Serializable]
public class ServiceActivationFailedException : ServiceCompositionException
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceActivationFailedException"/> class.
	/// </summary>
	/// <param name="serviceMoniker">The moniker of the service that failed to activate.</param>
	/// <param name="inner">The exception thrown from the service during activation.</param>
	public ServiceActivationFailedException(ServiceMoniker serviceMoniker, Exception? inner)
		: base(Strings.FormatServiceActivationFailed(serviceMoniker), inner)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceActivationFailedException"/> class.
	/// </summary>
	/// <param name="info">Seralization info.</param>
	/// <param name="context">Serialization context.</param>
	protected ServiceActivationFailedException(
	  System.Runtime.Serialization.SerializationInfo info,
	  System.Runtime.Serialization.StreamingContext context)
		: base(info, context)
	{
	}
}
