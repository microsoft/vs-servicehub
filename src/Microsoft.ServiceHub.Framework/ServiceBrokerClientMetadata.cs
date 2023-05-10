// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JsonNET = Newtonsoft.Json;
using STJ = System.Text.Json.Serialization;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes the environment, capabilities and attributes of a client of the <see cref="IRemoteServiceBroker"/>.
/// </summary>
public struct ServiceBrokerClientMetadata
{
	/// <summary>
	/// Gets or sets the remote service connections that the client supports.
	/// </summary>
	/// <remarks>
	/// This allows an <see cref="IRemoteServiceBroker"/> to choose the optimal mutually supported connection kind
	/// when responding to future service requests.
	/// </remarks>
	[JsonNET.JsonConverter(typeof(JsonNET.Converters.StringEnumConverter))]
	[STJ.JsonConverter(typeof(STJ.JsonStringEnumConverter))]
	public RemoteServiceConnections SupportedConnections { get; set; }

	/// <summary>
	/// Gets or sets metadata regarding the client's environment for use as a potential services host
	/// for service which are originally requested of the <see cref="IRemoteServiceBroker"/>
	/// but which services may in fact be available for activation locally within the client's environment.
	/// </summary>
	public ServiceHostInformation LocalServiceHost { get; set; }
}
