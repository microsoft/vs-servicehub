// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel.Composition;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.ServiceHub.Framework.Testing;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Xunit;
using Xunit.Abstractions;

public class ExportedBrokeredServiceTests : TestBase, IAsyncLifetime
{
	private MockBrokeredServiceContainer container = new MockBrokeredServiceContainer();

	public ExportedBrokeredServiceTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	private interface ICalculator
	{
		ValueTask<MockService> GetThisAsync();

		ValueTask<int> AddAsync(int a, int b);
	}

	private IServiceBroker ServiceBroker => this.container.GetFullAccessServiceBroker();

	public async Task InitializeAsync()
	{
		MefHost mefHost = new MefHost
		{
			BrokeredServiceContainer = this.container,
		};

		// This has a side effect of registering MEF exported brokered services into the mock container.
		await mefHost.CreateExportProviderAsync();
	}

	public Task DisposeAsync()
	{
		return Task.CompletedTask;
	}

	[Fact]
	public async Task InvokeBrokeredService()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			Assert.Equal(5, await calc.AddAsync(2, 3));
		}
	}

	[Fact]
	public async Task ImportsAreSatisfied()
	{
		ServiceActivationOptions options = new()
		{
			ActivationArguments = new Dictionary<string, string>
				{
					{ "a", "b" },
				},
		};
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockServiceWithImports.SharedDescriptor, options);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			MockServiceWithImports realObject = (MockServiceWithImports)await calc.GetThisAsync();
			Assert.Equal(MockServiceWithImports.SharedDescriptor.Moniker, realObject.ServiceMoniker);
			Assert.NotNull(realObject.AuthorizationServiceClient);
			Assert.NotNull(realObject.ServiceBroker);
			Assert.Equal(options.ActivationArguments["a"], realObject.ServiceActivationOptions.ActivationArguments?["a"]);
		}
	}

	[Fact]
	public async Task BrokeredServiceIsInitialized()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor);
		MockService realObject;
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject = await calc.GetThisAsync();
			Assert.Equal(1, realObject.InitializeInvocationCount);
		}

		Assert.Equal(1, realObject.InitializeInvocationCount);
	}

	[Fact]
	public async Task BrokeredServiceIsDisposed()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor);
		MockService realObject;
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject = await calc.GetThisAsync();
			Assert.Equal(0, realObject.DisposalInvocationCount);
		}

		// It's a known issue that MEF exported brokered services get disposed twice.
		// If that is ever corrected, change this to assert 1.
		Assert.Equal(2, realObject.DisposalInvocationCount);
	}

	[Fact]
	public async Task BrokeredServiceWithImportsIsDisposed()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockServiceWithImports.SharedDescriptor);
		MockService realObject;
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject = await calc.GetThisAsync();
			Assert.Equal(0, realObject.DisposalInvocationCount);
		}

		// It's a known issue that MEF exported brokered services get disposed twice.
		// If that is ever corrected, change this to assert 1.
		Assert.Equal(2, realObject.DisposalInvocationCount);
	}

	/// <summary>
	/// Verifies that a MEFv1 exported brokered service is created as a non-shared part,
	/// even if not explicitly attributed as such.
	/// </summary>
	[Fact]
	public async Task BrokeredServiceWithoutImportsIsNonShared()
	{
		MockService realObject1, realObject2;
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject1 = await calc.GetThisAsync();
		}

		calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject2 = await calc.GetThisAsync();
		}

		Assert.NotSame(realObject1, realObject2);
	}

	[ExportBrokeredService("Calculator", "1.0")]
	private class MockService : IExportedBrokeredService, ICalculator, IDisposable
	{
		internal static readonly ServiceRpcDescriptor SharedDescriptor = new ServiceJsonRpcDescriptor(
			new ServiceMoniker("Calculator", new Version("1.0")),
			clientInterface: null,
			ServiceJsonRpcDescriptor.Formatters.MessagePack,
			ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
			new Nerdbank.Streams.MultiplexingStream.Options
			{
				ProtocolMajorVersion = 3,
			});

		public virtual ServiceRpcDescriptor Descriptor => SharedDescriptor;

		internal int InitializeInvocationCount { get; private set; }

		internal int DisposalInvocationCount { get; private set; }

		public ValueTask<MockService> GetThisAsync() => new(this);

		public ValueTask<int> AddAsync(int a, int b) => new(a + b);

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			this.InitializeInvocationCount++;
			return Task.CompletedTask;
		}

		public void Dispose()
		{
			this.DisposalInvocationCount++;
		}
	}

	[ExportBrokeredService("Calculator", "1.1")]
	private class MockServiceWithImports : MockService
	{
		internal static readonly new ServiceRpcDescriptor SharedDescriptor = new ServiceJsonRpcDescriptor(
			new ServiceMoniker("Calculator", new Version("1.1")),
			clientInterface: null,
			ServiceJsonRpcDescriptor.Formatters.MessagePack,
			ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
			new Nerdbank.Streams.MultiplexingStream.Options
			{
				ProtocolMajorVersion = 3,
			});

		public override ServiceRpcDescriptor Descriptor => SharedDescriptor;

		[Import]
		internal ServiceMoniker ServiceMoniker { get; set; } = null!;

		[Import]
		internal ServiceActivationOptions ServiceActivationOptions { get; set; }

		[Import]
		internal IServiceBroker ServiceBroker { get; set; } = null!;

		[Import]
		internal AuthorizationServiceClient AuthorizationServiceClient { get; set; } = null!;
	}
}
