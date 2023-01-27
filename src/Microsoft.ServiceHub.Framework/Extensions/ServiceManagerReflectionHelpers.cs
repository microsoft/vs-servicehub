// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualBasic;
using Nerdbank.Streams;

namespace Microsoft.ServiceHub.Framework.Extensions;

/// <summary>
/// Extension methods used via reflection in Microsoft.ServiceHub.HostStub.IServiceManager inside of the DevCore repository inside of Visual Studio.
/// </summary>
internal static class ServiceManagerReflectionHelpers
{
	/// <summary>
	/// <see cref="ServiceActivationOptions"/> extension method for getting an <see cref="IServiceBroker"/>.
	/// </summary>
	/// <param name="options">The <see cref="ServiceActivationOptions"/> to get the <see cref="IServiceBroker"/> from.</param>
	/// <param name="cancellationToken">A token to signal cancellation.</param>
	/// <returns>The <see cref="IServiceBroker"/> referenced in the <see cref="ServiceActivationOptions"/> or null if one is not referenced.</returns>
	/// <devremarks>
	/// This is called via reflection from Microsoft.ServiceHub.HostStub.ServiceManager.GetServiceBrokerFromServiceActivationOptionsAsync so that the
	/// <see cref="IServiceBroker"/> can be passed directly to the constructor of a ServiceHub service.
	/// </devremarks>
	internal static async Task<IServiceBroker?> GetServiceBrokerAsync(ServiceActivationOptions options, CancellationToken cancellationToken)
	{
		var serverPipeName = options.GetServiceBrokerServerPipeName();

		if (!string.IsNullOrEmpty(serverPipeName))
		{
			return await RemoteServiceBroker.ConnectToServerAsync(serverPipeName, cancellationToken).ConfigureAwait(false);
		}

		return null;
	}

	/// <summary>
	/// <see cref="IServiceBroker"/> extension method for getting a <see cref="AuthorizationServiceClient"/>.
	/// </summary>
	/// <param name="broker">The <see cref="IServiceBroker"/> to get the <see cref="AuthorizationServiceClient"/> from.</param>
	/// <param name="cancellationToken">A token to signal cancellation.</param>
	/// <returns>A <see cref="Task{AuthorizationServiceClient}"/> representing the result of the asynchronous operation or null if the
	/// service wasn't found.</returns>
	/// <devremarks>
	/// This called via reflection from Microsoft.ServiceHub.HostStub.ServiceManager.GetServiceFactoryCreateAsyncArguments so that an
	/// <see cref="AuthorizationServiceClient"/> can be passed directly to the constructor of a ServiceHub service.
	/// </devremarks>
	internal static async Task<AuthorizationServiceClient> GetAuthorizationServiceClientAsync(IServiceBroker broker, CancellationToken cancellationToken)
	{
		IAuthorizationService? authService = null;

		if (broker != null)
		{
			try
			{
				authService = await broker.GetProxyAsync<IAuthorizationService>(FrameworkServices.Authorization, cancellationToken).ConfigureAwait(false);
			}
			catch (ServiceActivationFailedException)
			{
			}
		}

		if (authService is null)
		{
			authService = new DefaultAuthorizationService();
		}

		return new AuthorizationServiceClient(authService);
	}

	/// <summary>
	/// Helper method for setting up RpcConnection for hosted services.
	/// </summary>
	/// <param name="getRpcObject">The method reference to get the ServiceFactoryResult from ServiceFactory that accepts input <see cref="ServiceActivationOptions"/>.</param>
	/// <param name="stream">Stream.</param>
	/// <param name="serviceDescriptor">The <see cref="ServiceRpcDescriptor"/> for the requested service.</param>
	/// <param name="options">The <see cref="ServiceActivationOptions"/> for the requested service.</param>
	/// <param name="traceSource">The default <see cref="TraceSource"/> object.</param>
	/// <param name="completionTask">The method reference to get the completion task from <see cref="ServiceRpcDescriptor.RpcConnection"/>.</param>
	/// <returns>>Local RPC service object.</returns>
	internal static async Task<object> SetupRpcConnectionAsync(
		Func<object, Task<object>> getRpcObject,
		Stream stream,
		ServiceRpcDescriptor serviceDescriptor,
		ServiceActivationOptions options,
		TraceSource traceSource,
		Action<Task> completionTask)
	{
		Requires.NotNull(getRpcObject, nameof(Func<object, Task<object>>));
		Requires.NotNull(serviceDescriptor, nameof(ServiceRpcDescriptor));
		Requires.NotNull(stream, nameof(Stream));
		Requires.NotNull(completionTask, nameof(completionTask));

		if (serviceDescriptor.TraceSource is not null)
		{
			serviceDescriptor.TraceSource.Listeners.AddRange(traceSource.Listeners);
		}
		else
		{
			serviceDescriptor = serviceDescriptor.WithTraceSource(traceSource);
		}

		using (options.ApplyCultureToCurrentContext())
		{
			ServiceRpcDescriptor.RpcConnection connection = serviceDescriptor.ConstructRpcConnection(stream.UsePipe());

			try
			{
				if (serviceDescriptor.ClientInterface is not null && options.ClientRpcTarget is null)
				{
					options.ClientRpcTarget = connection.ConstructRpcClient(serviceDescriptor.ClientInterface);
				}

				var serviceFactoryResult = await getRpcObject(options).ConfigureAwait(false);

				connection.AddLocalRpcTarget(serviceFactoryResult);
				connection.StartListening();

				return serviceFactoryResult;
			}
			catch (Exception)
			{
				connection.Dispose();
				throw;
			}
			finally
			{
				completionTask(connection.Completion);
			}
		}
	}

	/// <summary>
	/// Helper method for constructing a <see cref="ServiceMoniker"/> with the version of the service getting activated.
	/// </summary>
	/// <param name="name">The service name.</param>
	/// <param name="version">Version of service.</param>
	/// <returns>The service moniker for service.</returns>
	internal static ServiceMoniker GetServiceMonikerForRequestingService(string name, string version)
	{
		Requires.NotNull(name, "Service Name");

		Version? serviceVersion = null;
		if (version is not null)
		{
			Version.TryParse(version, out serviceVersion);
		}

		return new ServiceMoniker(name, serviceVersion);
	}

	/// <summary>
	/// Helper method for getting the version information from <see cref="ServiceActivationOptions.ActivationArguments"/>.
	/// </summary>
	/// <param name="serviceActivationOptions">The serviceActivationOptions.</param>
	/// <returns>The value that is associated for the requested service version.</returns>
	internal static string GetVersionInformationFromServiceActivationOptions(ServiceActivationOptions serviceActivationOptions)
	{
		var keyValue = string.Empty;
		serviceActivationOptions.ActivationArguments?.TryGetValue("__servicehub__RequestedServiceVersion", out keyValue);

		return keyValue ?? string.Empty;
	}

	/// <summary>
	/// Gets the pipe name that the <see cref="IRemoteServiceBroker"/> is available over from the <see cref="ServiceActivationOptions"/>.
	/// </summary>
	/// <param name="options">The <see cref="ServiceActivationOptions"/> to get the pipe name from.</param>
	/// <returns>The pipe name or an empty string if there isn't one.</returns>
	private static string GetServiceBrokerServerPipeName(this ServiceActivationOptions options)
	{
		if (options.ActivationArguments != null &&
			options.ActivationArguments.TryGetValue("__servicehub__ServiceHubRemoteServiceBrokerPipeName", out string? pipeName))
		{
			return pipeName;
		}

		return string.Empty;
	}
}
