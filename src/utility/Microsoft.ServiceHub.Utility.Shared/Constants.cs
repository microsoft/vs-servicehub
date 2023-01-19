// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// Contains various utility constants.
/// </summary>
internal static class Constants
{
	/// <summary>
	/// String used to access the ServiceHubServiceLocation <see cref="ServiceActivationOptions"/> activation argument.
	/// </summary>
	internal const string ServiceHubServiceLocationActivationArgument = ServiceHubActivationArgumentNamespace + "ServiceHubServiceLocation";

	/// <summary>
	/// String used to access the ServiceHubHostGroup <see cref="ServiceActivationOptions"/> activation argument.
	/// </summary>
	internal const string ServiceHubHostGroupActivationArgument = ServiceHubActivationArgumentNamespace + "ServiceHubHostGroup";

	/// <summary>
	/// String used to access the ServiceModuleInfo <see cref="ServiceActivationOptions"/> activation argument.
	/// </summary>
	internal const string ServiceHubServiceModuleInfoActivationArgument = ServiceHubActivationArgumentNamespace + "ServiceModuleInfo";

	/// <summary>
	/// String used to access the ServiceHubRemoteServiceBrokerPipeName <see cref="ServiceActivationOptions"/> activation argument.
	/// </summary>
	internal const string ServiceHubRemoteServiceBrokerPipeNameActivationArgument = ServiceHubActivationArgumentNamespace + "ServiceHubRemoteServiceBrokerPipeName";

	/// <summary>
	/// String used to access the requested <see cref="System.Version"/> information from <see cref="ServiceActivationOptions"/> activation argument.
	/// </summary>
	internal const string ServiceHubVersionActivationArgument = ServiceHubActivationArgumentNamespace + "RequestedServiceVersion";

	/// <summary>
	/// String used to access the ServiceModuleInfo files of Friend services from <see cref="ServiceActivationOptions"/> activation argument.
	/// </summary>
	internal const string ServiceHubFriendServiceModuleInfoFormatter = ServiceHubActivationArgumentNamespace + "FriendServiceModuleInfo__{0}";

	/// <summary>
	/// String used to access the ServiceHubHostProcessId <see cref="ServiceActivationOptions"/> activation argument. The constant is used by the VS repo indirectly
	/// in src\Platform\Utilities\Impl\ServiceBroker\RemoteServiceBrokerWrapper.cs.
	/// </summary>
	internal const string ServiceHubHostProcessId = ServiceHubActivationArgumentNamespace + "HostProcessId";

	/// <summary>
	/// String used to get variables to replace in host arguments. These are provided in ServiceActivationOptions as a hint to host that
	/// would host the service. Their use is optional by the host.
	/// </summary>
	internal const string ServiceHubHostVariableActivationArgumentPrefix = "HostVariable_";

	private const string ServiceHubActivationArgumentNamespace = "__servicehub__";
}
