// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;

public class AuthorizationServiceTests : RpcTestBase<IAuthorizationService, AuthorizationServiceMock>
{
	public AuthorizationServiceTests(ITestOutputHelper logger)
		: base(logger, FrameworkServices.Authorization)
	{
	}

	[Fact]
	public async Task AuthorizeOrThrowAsync()
	{
		ProtectedOperation expected = new ProtectedOperation("moniker", 5);
		await this.ClientProxy.AuthorizeOrThrowAsync(expected, this.TimeoutToken);
		Assert.Equal(expected, this.Service.LastReceivedOperation);
	}

	[Fact]
	public async Task GetCredentialsAsync()
	{
		this.Service.CredentialsToReturn = new Dictionary<string, string>
		{
			["token"] = "abc",
		};

		IReadOnlyDictionary<string, string> actual = await this.ClientProxy.GetCredentialsAsync(this.TimeoutToken);
		Assert.Equal("abc", actual["token"]);
	}

	[Fact]
	public async Task AuthorizationChanged()
	{
		AsyncManualResetEvent authorizationChangedEvent = new();
		this.ClientProxy.AuthorizationChanged += (s, e) => authorizationChangedEvent.Set();
		this.Service.OnAuthorizationChanged();
		await authorizationChangedEvent.WaitAsync(this.TimeoutToken);
	}

	[Fact]
	public async Task CredentialsChanged()
	{
		AsyncManualResetEvent credentialsChangedEvent = new();
		this.ClientProxy.CredentialsChanged += (s, e) => credentialsChangedEvent.Set();
		this.Service.OnCredentialsChanged();
		await credentialsChangedEvent.WaitAsync(this.TimeoutToken);
	}
}
