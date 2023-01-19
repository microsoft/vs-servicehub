// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Utility;
using Microsoft.ServiceHub.Utility.Shared;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// ServiceBroker provided to services running inside of ServiceHub Hosts. Wraps an existing <see cref="RemoteServiceBroker"/>
/// and adds the <see cref="ServiceActivationOptions"/> ServiceHubHostProcessId to each request.
/// </summary>
public class ServiceHubHostRemoteServiceBroker : IServiceBroker, IDisposable
{
	private readonly IServiceBroker inner;

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceHubHostRemoteServiceBroker"/> class.
	/// </summary>
	/// <param name="inner">The inner <see cref="IServiceBroker"/> that this object wraps.</param>
	public ServiceHubHostRemoteServiceBroker(IServiceBroker inner)
	{
		this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
	}

	/// <inheritdoc />
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
	{
		add
		{
			this.inner.AvailabilityChanged += value;
		}

		remove
		{
			this.inner.AvailabilityChanged -= value;
		}
	}

	/// <inheritdoc />
	public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
		where T : class
	{
		options = SharedUtilities.AddEntryToActivationArguments(Constants.ServiceHubHostProcessId, Process.GetCurrentProcess().Id.ToString(), options);
		return this.inner.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken);
	}

	/// <inheritdoc />
	public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
	{
		options = SharedUtilities.AddEntryToActivationArguments(Constants.ServiceHubHostProcessId, Process.GetCurrentProcess().Id.ToString(), options);
		return this.inner.GetPipeAsync(serviceMoniker, options, cancellationToken);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		(this.inner as IDisposable)?.Dispose();
	}
}
