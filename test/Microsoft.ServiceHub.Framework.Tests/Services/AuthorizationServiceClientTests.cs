// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;

public class AuthorizationServiceClientTests : TestBase
{
	private readonly MockAuthorizationService mockAuthorizationService = new MockAuthorizationService(new Dictionary<string, string> { { "a", "b" } });
	private AuthorizationServiceClient client;

	public AuthorizationServiceClientTests(ITestOutputHelper logger)
		: base(logger)
	{
		this.client = new AuthorizationServiceClient(this.mockAuthorizationService);
	}

	[Fact]
	public void Ctor_ValidatesArgs()
	{
		Assert.Throws<ArgumentNullException>(() => new AuthorizationServiceClient(null!));
	}

	[Fact]
	public void IsDisposed()
	{
		Assert.False(this.client.IsDisposed);
		this.client.Dispose();
		Assert.True(this.client.IsDisposed);
	}

	[Fact]
	public void Dispose_DoesNotDisposeUnderlyingService()
	{
		this.client = new AuthorizationServiceClient(this.mockAuthorizationService, ownsAuthorizationService: false);
		this.client.Dispose();
		Assert.False(this.mockAuthorizationService.IsDisposed);
	}

	[Fact]
	public void Dispose_DoesDisposeUnderlyingService()
	{
		this.client.Dispose();
		Assert.True(this.mockAuthorizationService.IsDisposed);
	}

	[Fact]
	public async Task GetCredentialsAsync_IsCached()
	{
		IReadOnlyDictionary<string, string> credentials = await this.client.GetCredentialsAsync(this.TimeoutToken);
		Assert.Equal("b", credentials["a"]);
		Assert.Equal(1, this.mockAuthorizationService.GetCredentialsAsyncInvocationCount);

		credentials = await this.client.GetCredentialsAsync(this.TimeoutToken);
		Assert.Equal("b", credentials["a"]);
		Assert.Equal(1, this.mockAuthorizationService.GetCredentialsAsyncInvocationCount);

		this.mockAuthorizationService.UpdateCredentials(new Dictionary<string, string> { { "c", "d" } });

		credentials = await this.client.GetCredentialsAsync(this.TimeoutToken);
		Assert.False(credentials.ContainsKey("a"));
		Assert.Equal("d", credentials["c"]);
		Assert.Equal(2, this.mockAuthorizationService.GetCredentialsAsyncInvocationCount);
	}

	[Fact]
	public async Task CheckAuthorizationAsync_IsCached()
	{
		string opmk1 = Guid.NewGuid().ToString();

		var op = new ProtectedOperation(opmk1, 1);
		int expectedInvocations = 0;

		await this.client.CheckAuthorizationAsync(op, this.TimeoutToken);
		Assert.Equal(++expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);

		// Verify cache hit.
		await this.client.CheckAuthorizationAsync(op, this.TimeoutToken);
		Assert.Equal(expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);

		// Raise the event that caches should be cleared.
		this.mockAuthorizationService.OnAuthorizationChanged();

		// Verify that repeating a past query is a cache miss.
		await this.client.CheckAuthorizationAsync(op, this.TimeoutToken);
		Assert.Equal(++expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);
	}

	[Fact]
	public async Task CheckAuthorizationAsync_Caches_NegativeResponses()
	{
		string opmk1 = Guid.NewGuid().ToString();

		var opLow = new ProtectedOperation(opmk1, 1);
		var opMedium = new ProtectedOperation(opmk1, 2);
		var opHigh = new ProtectedOperation(opmk1, 3);

		int expectedInvocations = 0;

		// A medium level check will be a cache miss.
		bool result = await this.client.CheckAuthorizationAsync(opMedium, this.TimeoutToken);
		Assert.False(result);
		Assert.Equal(++expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);

		// Given medium level was denied, high level needn't be asked for.
		result = await this.client.CheckAuthorizationAsync(opHigh, this.TimeoutToken);
		Assert.False(result);
		Assert.Equal(expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);

		// A low level check is a cache miss.
		result = await this.client.CheckAuthorizationAsync(opLow, this.TimeoutToken);
		Assert.False(result);
		Assert.Equal(++expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);
	}

	[Fact]
	public async Task CheckAuthorizationAsync_Caches_PositiveResponses()
	{
		string opmk1 = Guid.NewGuid().ToString();

		var opLow = new ProtectedOperation(opmk1, 1);
		var opMedium = new ProtectedOperation(opmk1, 2);
		var opHigh = new ProtectedOperation(opmk1, 3);

		this.mockAuthorizationService.ApprovedOperations.Add(opLow);
		this.mockAuthorizationService.ApprovedOperations.Add(opMedium);
		int expectedInvocations = 0;

		// A medium level check will be a cache miss.
		bool result = await this.client.CheckAuthorizationAsync(opMedium, this.TimeoutToken);
		Assert.True(result);
		Assert.Equal(++expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);

		// A low level check should match the medium level cache.
		result = await this.client.CheckAuthorizationAsync(opLow, this.TimeoutToken);
		Assert.True(result);
		Assert.Equal(expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);

		// A high level check should be a cache miss.
		result = await this.client.CheckAuthorizationAsync(opHigh, this.TimeoutToken);
		Assert.False(result);
		Assert.Equal(++expectedInvocations, this.mockAuthorizationService.CheckAuthorizationAsyncInvocationCount);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			this.client.Dispose();
			this.mockAuthorizationService.Dispose();
		}

		base.Dispose(disposing);
	}
}
