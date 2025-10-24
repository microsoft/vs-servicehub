// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VerifyCS = CSharpSourceGeneratorVerifier<Microsoft.ServiceHub.Analyzers.ProxyGenerator>;

/// <summary>
/// Tests the local proxy source generator itself.
/// </summary>
public class ProxyGeneratorTests
{
	[Fact]
	public async Task Public_NotNested()
	{
		await VerifyCS.RunDefaultAsync("""
			[JsonRpcContract]
			public partial interface IMyRpc
			{
				Task JustCancellationAsync(CancellationToken cancellationToken);
				ValueTask AnArgAndCancellationAsync(int arg, CancellationToken cancellationToken);
				ValueTask<int> NoArgsOrCancellation();
				Task<int> AddAsync(int a, int b, CancellationToken cancellationToken);
				Task<int> MultiplyAsync(int a, int b);
				void Start(string bah);
				void StartCancelable(string bah, CancellationToken token);
				IAsyncEnumerable<int> CountAsync(int start, int count, CancellationToken cancellationToken);
			}
			""");
	}

	[Fact]
	public async Task Public_Nested()
	{
		await VerifyCS.RunDefaultAsync("""
			public partial class OuterClass
			{
				[JsonRpcContract]
				public partial interface IMyRpc
				{
					Task JustCancellationAsync(CancellationToken cancellationToken);
				}
			}
			""");
	}

	[Fact]
	public async Task ContractGroups()
	{
		await VerifyCS.RunDefaultAsync("""
			[JsonRpcContract]
			[JsonRpcProxyInterfaceGroup(typeof(IApple))]
			[JsonRpcProxyInterfaceGroup(typeof(IPear))]
			public partial interface IFruit
			{
				Task EatFruitAsync();
			}

			[JsonRpcContract]
			public partial interface IApple
			{
				Task EatAppleAsync();
			}

			[JsonRpcContract]
			public partial interface IPear
			{
				Task EatPearAsync();
			}
			""");
	}
}
