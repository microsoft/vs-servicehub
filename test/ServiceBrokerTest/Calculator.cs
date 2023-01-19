// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ServiceBrokerTest;

public class Calculator
{
	[StreamJsonRpc.JsonRpcMethod("add")]
	public async Task<int> AddAsync(int a, int b)
	{
		await Task.Yield();
		return a + b;
	}

	[StreamJsonRpc.JsonRpcMethod("add5")]
	public async Task<int> Add5Async(int a)
	{
		await Task.Yield();
		return a + 5;
	}
}
