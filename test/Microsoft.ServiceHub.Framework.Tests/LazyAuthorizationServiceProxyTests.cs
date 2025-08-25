// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;

public class LazyAuthorizationServiceProxyTests : TestBase
{
	public LazyAuthorizationServiceProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public async Task ActivatedAuthorizationServiceProxyIsDisposed()
	{
		ServiceBroker.AuthorizationService authorizationService = new();
		ServiceBroker broker = new(authorizationService);
		LazyAuthorizationServiceProxy proxy = new(broker, joinableTaskFactory: null);

		Assert.True(await proxy.CheckAuthorizationAsync(new ProtectedOperation("operation"), this.TimeoutToken));

		proxy.Dispose();
		Assert.True(authorizationService.Disposed);
	}

	[Fact]
	public async Task CanActivateAndUseAuthorizationService()
	{
		ServiceBroker.AuthorizationService authorizationService = new();
		ServiceBroker broker = new(authorizationService);
		using LazyAuthorizationServiceProxy proxy = new(broker, joinableTaskFactory: null);

		Assert.True(await proxy.CheckAuthorizationAsync(new ProtectedOperation("operation"), this.TimeoutToken));
		Assert.NotNull(await proxy.GetCredentialsAsync(this.TimeoutToken));
	}

	[Fact]
	public void DisposingServiceBeforeActivationDoesNotRequestProxy()
	{
		AlwaysThrowingServiceBroker broker = new();
		LazyAuthorizationServiceProxy proxy = new(broker, joinableTaskFactory: null);
		proxy.Dispose();

		Assert.Equal(0, broker.GetProxyAsyncCalls);
	}

	private class AlwaysThrowingServiceBroker : IServiceBroker
	{
		private int getProxyAsyncCalls;

		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add => throw new NotImplementedException();
			remove => throw new NotImplementedException();
		}

		public int GetProxyAsyncCalls => this.getProxyAsyncCalls;

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default) =>
			throw new NotImplementedException();

		public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			this.getProxyAsyncCalls++;
			throw new NotImplementedException();
		}
	}

	private class ServiceBroker : IServiceBroker
	{
		private readonly AuthorizationService authorizationService;

		public ServiceBroker(AuthorizationService authorizationService)
		{
			this.authorizationService = authorizationService;
		}

		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default) =>
			throw new NotImplementedException();

		public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			if (serviceDescriptor.Moniker.Equals(FrameworkServices.Authorization.Moniker))
			{
				return new ValueTask<T?>((T?)(this.authorizationService as object));
			}

			throw new NotSupportedException();
		}

		public class AuthorizationService : IAuthorizationService, IDisposable
		{
			private bool disposed;

			public event EventHandler? CredentialsChanged
			{
				add { }
				remove { }
			}

			public event EventHandler? AuthorizationChanged
			{
				add { }
				remove { }
			}

			public bool Disposed => this.disposed;

			public ValueTask<bool> CheckAuthorizationAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) =>
				new(true);

			public void Dispose() => this.disposed = true;

			public ValueTask<IReadOnlyDictionary<string, string>> GetCredentialsAsync(CancellationToken cancellationToken = default) =>
				new(new Dictionary<string, string>());
		}
	}
}
