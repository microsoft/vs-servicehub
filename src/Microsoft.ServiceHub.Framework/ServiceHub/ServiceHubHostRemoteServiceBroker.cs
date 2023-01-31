// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Diagnostics;
using System.IO.Pipelines;

namespace Microsoft.ServiceHub.Framework
{
	/// <summary>
	/// ServiceBroker provided to services running inside of ServiceHub Hosts. Wraps an existing <see cref="RemoteServiceBroker"/>
	/// and adds the <see cref="ServiceActivationOptions"/> ServiceHubHostProcessId to each request.
	/// </summary>
	public class ServiceHubHostRemoteServiceBroker : IServiceBroker, IDisposable
	{
		private const string ServiceHubHostProcessIdActivationArgument = "__servicehub__HostProcessId";

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
			options = AddEntryToActivationArguments(ServiceHubHostProcessIdActivationArgument, Process.GetCurrentProcess().Id.ToString(), options);
			return this.inner.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken);
		}

		/// <inheritdoc />
		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			options = AddEntryToActivationArguments(ServiceHubHostProcessIdActivationArgument, Process.GetCurrentProcess().Id.ToString(), options);
			return this.inner.GetPipeAsync(serviceMoniker, options, cancellationToken);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			(this.inner as IDisposable)?.Dispose();
		}

		/// <summary>
		/// Adds the key and value to the ActivationArguments of a <see cref="ServiceActivationOptions"/> provided the key does not already exist within the ActivationArguments.
		/// </summary>
		/// <param name="key">The key of the argument being added to the dictionary.</param>
		/// <param name="value">The value of the argument being added to the dictionary.</param>
		/// <param name="options">The <see cref="ServiceActivationOptions"/> object that the key and value are being added to.</param>
		/// <returns>The updated <see cref="ServiceActivationOptions"/>.</returns>
		private static ServiceActivationOptions AddEntryToActivationArguments(string key, string value, ServiceActivationOptions options)
		{
			if (options.ActivationArguments == null || !options.ActivationArguments.ContainsKey(key))
			{
				var activationArguments = new Dictionary<string, string>();

				if (options.ActivationArguments != null)
				{
					foreach (var item in options.ActivationArguments.Keys)
					{
						if (options.ActivationArguments.TryGetValue(item, out string? itemValue))
						{
							activationArguments.Add(item, itemValue);
						}
					}
				}

				activationArguments.Add(key, value);

				options.ActivationArguments = activationArguments;
			}

			return options;
		}
	}
}
