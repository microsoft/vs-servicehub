// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

/// <summary>
/// Defines the several reasons a brokered service might not be obtained.
/// </summary>
public enum MissingBrokeredServiceErrorCode
{
	/// <summary>
	/// Nothing could be found wrong to explain the missing service.
	/// It may be available now.
	/// </summary>
	NoExplanation,

	/// <summary>
	/// The requested service had no match in the local service registry.
	/// </summary>
	/// <remarks>
	/// All services, whether local or remote, must be in the local registry in order to be acquired locally.
	/// </remarks>
	NotLocallyRegistered,

	/// <summary>
	/// Special resiliency testing configuration is in place and denied access to this service.
	/// </summary>
	ChaosConfigurationDeniedRequest,

	/// <summary>
	/// The service is expected to come from an exclusive server (e.g. a Codespace Server)
	/// but the connection is not ready yet or the server does not offer it.
	/// </summary>
	[Obsolete("Use " + nameof(LocalServiceHiddenOnRemoteClient) + " instead.")]
	LocalServiceHiddenOnExclusiveClient,

	/// <summary>
	/// The service is not exposed to the audience making the request.
	/// </summary>
	ServiceAudienceMismatch,

	/// <summary>
	/// The service is registered but no factory has been loaded for it.
	/// </summary>
	ServiceFactoryNotProffered,

	/// <summary>
	/// The service factory returned null instead of an instance of the service.
	/// </summary>
	ServiceFactoryReturnedNull,

	/// <summary>
	/// The service factory threw an exception.
	/// </summary>
	ServiceFactoryFault,

	/// <summary>
	/// The service is expected to come from a remote server
	/// but the connection is not ready yet or the server does not offer it.
	/// A locally proffered service is not available when it also can come remotely and a remote connection exists or is expected.
	/// </summary>
	LocalServiceHiddenOnRemoteClient,
}
