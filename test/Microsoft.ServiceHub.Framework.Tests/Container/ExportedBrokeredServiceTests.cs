// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel.Composition;
using System.IO.Pipelines;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.ServiceHub.Framework.Testing;
using Microsoft.VisualStudio.Composition;
using Microsoft.VisualStudio.Shell.ServiceBroker;

public class ExportedBrokeredServiceTests : TestBase, IAsyncLifetime
{
	private MockBrokeredServiceContainer container = new MockBrokeredServiceContainer();

	public ExportedBrokeredServiceTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	private interface ICalculator
	{
		ValueTask<ICalculator> GetThisAsync();

		ValueTask<int> AddAsync(int a, int b);
	}

	private interface ISubtractingCalculator
	{
		ValueTask<int> SubtractAsync(int a, int b);
	}

	private interface ICallbackInterface
	{
		Task CallbackAsync(string message);
	}

	private interface IServiceWithCallback
	{
		Task CallMeAsync(string message);
	}

	private IServiceBroker ServiceBroker => this.container.GetFullAccessServiceBroker();

	public async ValueTask InitializeAsync()
	{
		MefHost mefHost = new MefHost
		{
			BrokeredServiceContainer = this.container,
		};

		// This has a side effect of registering MEF exported brokered services into the mock container.
		await mefHost.CreateExportProviderAsync();
	}

	public ValueTask DisposeAsync()
	{
		return default;
	}

	[Fact]
	public async Task InvokeBrokeredService()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor, this.TimeoutToken);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			Assert.Equal(5, await calc.AddAsync(2, 3));
		}
	}

	[Fact]
	public async Task InvokeBrokeredService_WithoutOptionalInterface()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockServiceWithImports.SharedDescriptor, this.TimeoutToken);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			Assert.IsNotAssignableFrom<ISubtractingCalculator>(calc);
		}
	}

	[Theory, PairwiseData]
	public async Task InvokeBrokeredService_WithOptionalInterface(bool serializedCatalog)
	{
		if (serializedCatalog)
		{
			this.container = new();

			MefHost mefHost = new MefHost(serializedCatalog: true)
			{
				BrokeredServiceContainer = this.container,
			};

			// This has a side effect of registering MEF exported brokered services into the mock container.
			await mefHost.CreateExportProviderAsync(this.TimeoutToken);
		}

		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockServiceWithImports.SharedDescriptorVNext, this.TimeoutToken);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			ISubtractingCalculator subCalc = Assert.IsAssignableFrom<ISubtractingCalculator>(calc);
			Assert.Equal(-1, await subCalc.SubtractAsync(2, 3));
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
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockServiceWithImports.SharedDescriptor, options, this.TimeoutToken);
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
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor, this.TimeoutToken);
		MockService realObject;
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject = (MockService)await calc.GetThisAsync();
			Assert.Equal(1, realObject.InitializeInvocationCount);
		}

		Assert.Equal(1, realObject.InitializeInvocationCount);
	}

	[Fact]
	public async Task BrokeredServiceIsDisposed()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor, this.TimeoutToken);
		MockService realObject;
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject = (MockService)await calc.GetThisAsync();
			Assert.Equal(0, realObject.DisposalInvocationCount);
		}

		// It's a known issue that MEF exported brokered services get disposed twice.
		// If that is ever corrected, change this to assert 1.
		Assert.Equal(2, realObject.DisposalInvocationCount);
	}

	[Fact]
	public async Task BrokeredServiceWithImportsIsDisposed()
	{
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockServiceWithImports.SharedDescriptor, this.TimeoutToken);
		MockService realObject;
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject = (MockService)await calc.GetThisAsync();
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
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor, this.TimeoutToken);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject1 = (MockService)await calc.GetThisAsync();
		}

		calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(MockService.SharedDescriptor, this.TimeoutToken);
		using (calc as IDisposable)
		{
			Assumes.Present(calc);
			realObject2 = (MockService)await calc.GetThisAsync();
		}

		Assert.NotSame(realObject1, realObject2);
	}

	[Fact]
	public async Task NullVersion_NullDescriptor_GetPipe()
	{
		NullVersionedCalculator.CreatedInstances.Clear();
		IDuplexPipe? calc = await this.ServiceBroker.GetPipeAsync(new ServiceMoniker("Calculator", new Version(9, 0)), this.TimeoutToken);
		Assert.Null(calc);
		Assert.True(Assert.Single(NullVersionedCalculator.CreatedInstances).IsDisposed);
	}

	[Fact]
	public async Task NullVersion_NonNullDescriptor_GetPipe()
	{
		NullVersionedCalculator.CreatedInstances.Clear();
		IDuplexPipe? calc = await this.ServiceBroker.GetPipeAsync(new ServiceMoniker("Calculator", new Version(8, 0)), this.TimeoutToken);
		Assert.NotNull(calc);
		Assert.Single(NullVersionedCalculator.CreatedInstances);
	}

	[Fact]
	public async Task NullVersion_NonNullDescriptor_GetPipe_NullVersion()
	{
		NullVersionedCalculator.CreatedInstances.Clear();
		IDuplexPipe? calc = await this.ServiceBroker.GetPipeAsync(new ServiceMoniker("Calculator", null), this.TimeoutToken);
		Assert.NotNull(calc);
		Assert.Single(NullVersionedCalculator.CreatedInstances);
	}

	[Fact]
	public async Task NullVersion_NullDescriptor_GetProxy()
	{
		NullVersionedCalculator.CreatedInstances.Clear();
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(NullVersionedCalculator.CreateDescriptor(new Version(9, 0)), this.TimeoutToken);
		Assert.Null(calc);
		Assert.True(Assert.Single(NullVersionedCalculator.CreatedInstances).IsDisposed);
	}

	[Fact]
	public async Task NullVersion_NonNullDescriptor_GetProxy()
	{
		NullVersionedCalculator.CreatedInstances.Clear();
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(NullVersionedCalculator.CreateDescriptor(new Version(8, 0)), this.TimeoutToken);
		Assert.NotNull(calc);
		Assert.Single(NullVersionedCalculator.CreatedInstances);
	}

	[Fact]
	public async Task NullVersion_NonNullDescriptor_GetProxy_NullVersion()
	{
		NullVersionedCalculator.CreatedInstances.Clear();
		ICalculator? calc = await this.ServiceBroker.GetProxyAsync<ICalculator>(NullVersionedCalculator.CreateDescriptor(null), this.TimeoutToken);
		Assert.NotNull(calc);
		Assert.Single(NullVersionedCalculator.CreatedInstances);
	}

	[Theory, CombinatorialData]
	public async Task ActivateBrokeredServiceWithClientCallback(bool usePipe)
	{
		string? receivedMessage = null;
		ClientCallback client = new()
		{
			Callback = (string message) => receivedMessage = message,
		};
		IServiceWithCallback? svc;
		if (usePipe)
		{
			// We do *not* pass the client RPC target in as options because for a genuine pipe, it wouldn't be available till we set up the RPC connection later.
			IDuplexPipe? pipe = await this.ServiceBroker.GetPipeAsync(CallBackService.SharedDescriptor.Moniker, options: default, this.TimeoutToken);
			Assert.NotNull(pipe);

			// This is when a pipe client naturally sets up the client RPC target.
			svc = CallBackService.SharedDescriptor.ConstructRpc<IServiceWithCallback>(client, pipe);
		}
		else
		{
			ServiceActivationOptions options = new()
			{
				ClientRpcTarget = client,
			};
			svc = await this.ServiceBroker.GetProxyAsync<IServiceWithCallback>(CallBackService.SharedDescriptor, options, this.TimeoutToken);
			Assert.NotNull(svc);
		}

		try
		{
			await svc.CallMeAsync("hi");
			Assert.Equal("hi", receivedMessage);
		}
		finally
		{
			(svc as IDisposable)?.Dispose();
		}
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

		public ValueTask<ICalculator> GetThisAsync() => new(this);

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
	[ExportBrokeredService("Calculator", "1.2", typeof(ISubtractingCalculator))]
	private class MockServiceWithImports : MockService, ISubtractingCalculator
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

		internal static readonly ServiceRpcDescriptor SharedDescriptorVNext = new ServiceJsonRpcDescriptor(
			new ServiceMoniker("Calculator", new Version("1.2")),
			clientInterface: null,
			ServiceJsonRpcDescriptor.Formatters.MessagePack,
			ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
			new Nerdbank.Streams.MultiplexingStream.Options
			{
				ProtocolMajorVersion = 3,
			});

		public override ServiceRpcDescriptor Descriptor => this.ServiceMoniker.Version?.Minor == 2 ? SharedDescriptorVNext : SharedDescriptor;

		[Import]
		internal ServiceMoniker ServiceMoniker { get; set; } = null!;

		[Import]
		internal ServiceActivationOptions ServiceActivationOptions { get; set; }

		[Import]
		internal IServiceBroker ServiceBroker { get; set; } = null!;

		[Import]
		internal AuthorizationServiceClient AuthorizationServiceClient { get; set; } = null!;

		public ValueTask<int> SubtractAsync(int a, int b) => new(a - b);
	}

	[ExportBrokeredService("CallBackService", "0.1")]
	private class CallBackService : IServiceWithCallback, IExportedBrokeredService
	{
		internal static readonly ServiceRpcDescriptor SharedDescriptor = new ServiceJsonRpcDescriptor(
			new ServiceMoniker("CallBackService", new Version(0, 1)),
			typeof(ICallbackInterface),
			ServiceJsonRpcDescriptor.Formatters.UTF8,
			ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);

		private CallBackService()
		{
		}

		public ServiceRpcDescriptor Descriptor => SharedDescriptor;

		[Import]
		internal ServiceActivationOptions ServiceActivationOptions { get; set; }

		public async Task CallMeAsync(string message)
		{
			Verify.Operation(this.ServiceActivationOptions.ClientRpcTarget is not null, "No callback provided.");
			await ((ICallbackInterface)this.ServiceActivationOptions.ClientRpcTarget).CallbackAsync(message);
		}

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	private class ClientCallback : ICallbackInterface
	{
		internal Action<string>? Callback { get; set; }

		public Task CallbackAsync(string message)
		{
			this.Callback?.Invoke(message);
			return Task.CompletedTask;
		}
	}

	[ExportBrokeredService("Calculator", null)]
	private class NullVersionedCalculator : IExportedBrokeredService, ICalculator, IDisposable
	{
		internal static readonly Queue<NullVersionedCalculator> CreatedInstances = new();

		private NullVersionedCalculator()
		{
			CreatedInstances.Enqueue(this);
		}

		public ServiceRpcDescriptor? Descriptor => this.ServiceMoniker.Version switch
		{
			null or { Major: 8 } => CreateDescriptor(this.ServiceMoniker.Version),
			_ => null,
		};

		internal bool IsDisposed { get; private set; }

		[Import]
		private ServiceMoniker ServiceMoniker { get; set; } = null!;

		public ValueTask<int> AddAsync(int a, int b)
		{
			throw new NotImplementedException();
		}

		public void Dispose()
		{
			this.IsDisposed = true;
		}

		public ValueTask<ICalculator> GetThisAsync()
		{
			throw new NotImplementedException();
		}

		public Task InitializeAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		internal static ServiceRpcDescriptor CreateDescriptor(Version? version) =>
			new ServiceJsonRpcDescriptor(new ServiceMoniker("Calculator", version), ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
	}
}
