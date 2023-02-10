// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using StreamJsonRpc;

namespace ServiceBrokerTest;

public class ActivationService
{
	private ServiceActivationOptions options;

	public ActivationService(ServiceActivationOptions options)
	{
		this.options = options;
	}

	[JsonRpcMethod("getActivationArguments")]
	public Task<IReadOnlyDictionary<string, string>?> GetActivationArgumentsAsync()
	{
		return Task.FromResult(this.options.ActivationArguments);
	}

	[JsonRpcMethod("getClientCredentials")]
	public Task<IReadOnlyDictionary<string, string>?> GetClientCredentialsAsync()
	{
		return Task.FromResult(this.options.ClientCredentials);
	}
}
