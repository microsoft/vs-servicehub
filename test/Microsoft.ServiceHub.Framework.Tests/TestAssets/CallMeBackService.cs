// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

public class CallMeBackService : ICallMeBack
{
	private readonly ICallMeBackClient client;

	public CallMeBackService(ServiceActivationOptions options)
	{
		this.client = (ICallMeBackClient)(options.ClientRpcTarget ?? throw new ArgumentException("Required client RPC target not provided."));
	}

	public async Task CallMeBackAsync(string message, CancellationToken cancellationToken)
	{
		await this.client.YouPhonedAsync(message, cancellationToken);
	}
}
