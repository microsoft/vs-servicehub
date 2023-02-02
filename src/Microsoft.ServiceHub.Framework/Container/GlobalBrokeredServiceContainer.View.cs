// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// A filtered view on the services proffered to a <see cref="GlobalBrokeredServiceContainer"/>, exposed as an <see cref="IServiceBroker"/>.
	/// </summary>
	[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
	private class View : IServiceBroker, IRemoteServiceBroker
	{
		/// <summary>
		/// The owner of this view.
		/// </summary>
		private readonly GlobalBrokeredServiceContainer container;
		private readonly IReadOnlyDictionary<string, string> clientCredentials;
		private readonly ClientCredentialsPolicy clientCredentialsPolicy;
		private readonly CultureInfo? clientCulture;
		private readonly CultureInfo? clientUICulture;

		/// <summary>
		/// The set of services that have been queried for (whether or not they were found)
		/// since the last time the <see cref="AvailabilityChanged"/> event has been raised regarding them.
		/// </summary>
		private readonly HashSet<ServiceMoniker> observedServices = new HashSet<ServiceMoniker>();

		private EventHandler<BrokeredServicesChangedEventArgs>? availabilityChanged;

		/// <summary>
		/// Initializes a new instance of the <see cref="View"/> class.
		/// </summary>
		/// <param name="container">The parent container.</param>
		/// <param name="audience">
		/// The audience for this <see cref="IServiceBroker"/>.
		/// Each flag specified applies an additional filter (i.e. fewer exposed services).
		/// Use <see cref="ServiceAudience.None"/> to make *all* services available (to be used for local consumption only).
		/// </param>
		/// <param name="clientCredentials">The client credentials to apply to incoming requests.</param>
		/// <param name="clientCredentialsPolicy">Specifies which client credentials prevails when the service request contains non-empty credentials.</param>
		/// <param name="clientCulture">The value to apply to service requests when <see cref="ServiceActivationOptions.ClientCulture"/> is <see langword="null"/>.</param>
		/// <param name="clientUICulture">The value to apply to service requests when <see cref="ServiceActivationOptions.ClientUICulture"/> is <see langword="null"/>.</param>
		protected internal View(GlobalBrokeredServiceContainer container, ServiceAudience audience, IReadOnlyDictionary<string, string> clientCredentials, ClientCredentialsPolicy clientCredentialsPolicy, CultureInfo? clientCulture = null, CultureInfo? clientUICulture = null)
		{
			this.container = container ?? throw new ArgumentNullException(nameof(container));
			this.Audience = audience;
			this.clientCredentials = clientCredentials ?? throw new ArgumentNullException(nameof(clientCredentials));
			this.clientCredentialsPolicy = clientCredentialsPolicy;
			this.clientCulture = clientCulture;
			this.clientUICulture = clientUICulture;
		}

		/// <inheritdoc />
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add
			{
				lock (this.SyncObject)
				{
					if (this.availabilityChanged == null)
					{
						// Wire our own handler up to the service container
						this.container.AvailabilityChanged += this.OnAvailabilityChanged;
					}

					this.availabilityChanged += value;
				}
			}

			remove
			{
				lock (this.SyncObject)
				{
					this.availabilityChanged -= value;

					if (this.availabilityChanged == null)
					{
						// Unadvise for events from the service container so we neither leak nor require the owner to dispose of us.
						this.container.AvailabilityChanged -= this.OnAvailabilityChanged;
					}
				}
			}
		}

		/// <summary>
		/// Gets the filter to apply to services.
		/// </summary>
		internal ServiceAudience Audience { get; }

		/// <summary>
		/// Gets an object that this class can lock on to synchronize field access.
		/// </summary>
		/// <remarks>
		/// We return a private field because that's good enough, and it avoids an extra allocation of a dedicated sync object.
		/// </remarks>
		private object SyncObject => this.observedServices;

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string DebuggerDisplay => $"{this.Audience} view";

		/// <inheritdoc />
		public virtual async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			Requires.NotNull(serviceMoniker, nameof(serviceMoniker));

			using StartActivityExtension.TraceActivity activity = this.container.traceSource.StartActivity("Requesting pipe to \"{0}\"", serviceMoniker);

			var proffered = default(IProffered);
			MissingBrokeredServiceErrorCode errorCode;
			RequestResult requestResult = RequestResult.Declined;

			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				options = this.ApplyOptionsFilter(options);

				(proffered, errorCode) = await this.TryGetProfferingSourceAsync(serviceMoniker, cancellationToken).ConfigureAwait(false);

				if (proffered is object)
				{
					IDuplexPipe? pipe = proffered is ProfferedViewIntrinsicService viewIntrinsic
						? await viewIntrinsic.GetPipeAsync(this, serviceMoniker, options, cancellationToken).ConfigureAwait(false)
						: await proffered.GetPipeAsync(serviceMoniker, options, cancellationToken).ConfigureAwait(false);

					TraceEventType eventType = TraceEventType.Warning;
					if (pipe is object)
					{
						eventType = TraceEventType.Information;
						requestResult = RequestResult.Fulfilled;
					}
					else
					{
						errorCode = MissingBrokeredServiceErrorCode.ServiceFactoryReturnedNull;
					}

					if (this.container.traceSource.Switch.ShouldTrace(eventType))
					{
						this.container.traceSource.TraceEvent(
							eventType,
							(int)TraceEvents.Request,
							"Request for pipe to \"{0}\" is {1} by {2}: {3}.",
							serviceMoniker,
							requestResult,
							proffered.Source,
							errorCode);
					}

					return pipe;
				}
				else
				{
					requestResult = RequestResult.DeclinedNotFound;

					if (this.container.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
					{
						this.container.traceSource.TraceEvent(
						TraceEventType.Warning,
						(int)TraceEvents.Request,
						"Request for pipe to \"{0}\" is declined: {1}.",
						serviceMoniker,
						errorCode);
					}
				}

				return default;
			}
			catch (Exception ex) when (!(ex is ServiceActivationFailedException) && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				var outer = new ServiceActivationFailedException(serviceMoniker, ex);
				this.container.traceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.Request, "{0}", outer);
				throw outer;
			}
			catch (ServiceActivationFailedException ex)
			{
				this.container.traceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.Request, "{0}", ex);
				throw;
			}
			finally
			{
				this.container.OnRequestHandled(serviceMoniker, (proffered as ProfferedServiceFactory)?.Descriptor, RequestType.Pipe, requestResult, proffered);

				// Record the client's interest in this service.
				// We do this after actually acquiring the service so that if an AvailabilityChanged event was raised *during* the request,
				// which can sometimes happen if the very act of requesting the service changes the service graph,
				// the client will get an event if the service changes *after* we reached this point.
				// We also want to avoid the mid-request event being raised to the client if it's just from loading a package
				// since that can cause the client to dispose the service we just acquired and re-request it.
				// If we ever need to address the race condition of a proffered service changing *during* acquisition,
				// we should be careful to avoid propagating events to the client unless it really is important/relevant/new.
				lock (this.SyncObject)
				{
					this.observedServices.Add(serviceMoniker);
				}
			}
		}

		/// <inheritdoc />
		public virtual async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			Requires.NotNull(serviceDescriptor, nameof(serviceDescriptor));

			using StartActivityExtension.TraceActivity activity = this.container.traceSource.StartActivity("Requesting proxy to \"{0}\"", serviceDescriptor.Moniker);

			var proffered = default(IProffered);
			MissingBrokeredServiceErrorCode errorCode;
			RequestResult requestResult = RequestResult.Declined;

			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				options = this.ApplyOptionsFilter(options);

				(proffered, errorCode) = await this.TryGetProfferingSourceAsync(serviceDescriptor.Moniker, cancellationToken).ConfigureAwait(false);
				if (proffered is object)
				{
					serviceDescriptor = serviceDescriptor
						.WithTraceSource(await this.container.GetTraceSourceForConnectionAsync(this, serviceDescriptor.Moniker, options, clientRole: true, cancellationToken).ConfigureAwait(false));

					GC.KeepAlive(typeof(ValueTask<T>)); // workaround CLR bug https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1358442
					T? proxy = proffered is ProfferedViewIntrinsicService viewIntrinsic
						? await viewIntrinsic.GetProxyAsync<T>(this, serviceDescriptor, options, cancellationToken).ConfigureAwait(false)
						: await proffered.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken).ConfigureAwait(false);

					TraceEventType eventType = TraceEventType.Warning;
					if (proxy is object)
					{
						eventType = TraceEventType.Information;
						requestResult = RequestResult.Fulfilled;
					}
					else
					{
						errorCode = MissingBrokeredServiceErrorCode.ServiceFactoryReturnedNull;
					}

					if (this.container.traceSource.Switch.ShouldTrace(eventType))
					{
						this.container.traceSource.TraceEvent(
							eventType,
							(int)TraceEvents.Request,
							"Request for proxy to \"{0}\" is {1} by {2}: {3}.",
							serviceDescriptor.Moniker,
							requestResult,
							proffered.Source,
							errorCode);
					}

					return proxy;
				}
				else
				{
					requestResult = RequestResult.DeclinedNotFound;

					if (this.container.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
					{
						this.container.traceSource.TraceEvent(
							TraceEventType.Warning,
							(int)TraceEvents.Request,
							"Request for proxy to \"{0}\" is declined: {1}.",
							serviceDescriptor.Moniker,
							errorCode);
					}
				}

				return default;
			}
			catch (Exception ex) when (!(ex is ServiceActivationFailedException) && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				var outer = new ServiceActivationFailedException(serviceDescriptor.Moniker, ex);
				this.container.traceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.Request, "{0}", outer);
				throw outer;
			}
			catch (ServiceActivationFailedException ex)
			{
				this.container.traceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.Request, "{0}", ex);
				throw;
			}
			finally
			{
				this.container.OnRequestHandled(serviceDescriptor.Moniker, serviceDescriptor, RequestType.Proxy, requestResult, proffered);

				// Record the client's interest in this service.
				// We do this after actually acquiring the service so that if an AvailabilityChanged event was raised *during* the request,
				// which can sometimes happen if the very act of requesting the service changes the service graph,
				// the client will get an event if the service changes *after* we reached this point.
				// We also want to avoid the mid-request event being raised to the client if it's just from loading a package
				// since that can cause the client to dispose the service we just acquired and re-request it.
				// If we ever need to address the race condition of a proffered service changing *during* acquisition,
				// we should be careful to avoid propagating events to the client unless it really is important/relevant/new.
				lock (this.SyncObject)
				{
					this.observedServices.Add(serviceDescriptor.Moniker);
				}
			}
		}

		/// <inheritdoc />
		public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			if (!clientMetadata.SupportedConnections.HasFlag(RemoteServiceConnections.IpcPipe))
			{
				// We only support pipes.
				throw new NotSupportedException();
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public async Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken)
		{
			Requires.NotNull(serviceMoniker, nameof(serviceMoniker));
			serviceActivationOptions = this.ApplyOptionsFilter(serviceActivationOptions);

			try
			{
				(IProffered? proffered, MissingBrokeredServiceErrorCode errorCode) = await this.TryGetProfferingSourceAsync(serviceMoniker, cancellationToken).ConfigureAwait(false);
				if (proffered is object)
				{
					RemoteServiceConnectionInfo connectionInfo = proffered is ProfferedViewIntrinsicService viewIntrinsic
						? await viewIntrinsic.RequestServiceChannelAsync(this, serviceMoniker, serviceActivationOptions, cancellationToken).ConfigureAwait(false)
						: await proffered.RequestServiceChannelAsync(serviceMoniker, serviceActivationOptions, cancellationToken).ConfigureAwait(false);
					return connectionInfo;
				}

				if (this.container.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
				{
					this.container.traceSource.TraceEvent(
						TraceEventType.Warning,
						(int)TraceEvents.Request,
						"Remote request for \"{0}\" is declined: {1}.",
						serviceMoniker,
						errorCode);
				}

				return default;
			}
			finally
			{
				// Record the client's interest in this service.
				// We do this after actually acquiring the service so that if an AvailabilityChanged event was raised *during* the request,
				// which can sometimes happen if the very act of requesting the service changes the service graph,
				// the client will get an event if the service changes *after* we reached this point.
				// We also want to avoid the mid-request event being raised to the client if it's just from loading a package
				// since that can cause the client to dispose the service we just acquired and re-request it.
				// If we ever need to address the race condition of a proffered service changing *during* acquisition,
				// we should be careful to avoid propagating events to the client unless it really is important/relevant/new.
				lock (this.SyncObject)
				{
					this.observedServices.Add(serviceMoniker);
				}
			}
		}

		/// <inheritdoc />
		public async Task CancelServiceRequestAsync(Guid serviceRequestId)
		{
			// Try sending the cancellation to all remote sources since we don't know which one actually handled the request
			// that's being canceled. Checking if a request should be canceled by the broker should be relatively cheap and
			// since request ids are guids there's no risk of id collisions
			foreach (IProffered proffered in this.container.remoteSources.Values)
			{
				await proffered.CancelServiceRequestAsync(serviceRequestId).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Raises the <see cref="IServiceBroker.AvailabilityChanged"/> event.
		/// </summary>
		/// <param name="sender">Unused.</param>
		/// <param name="args">The arguments for the event. The set of impacted services must be a hash-based collection so we can use Intersect on it.</param>
		internal void OnAvailabilityChanged(object? sender, (ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>>? OldIndex, ImmutableHashSet<ServiceMoniker> ImpactedServices) args)
		{
			EventHandler<BrokeredServicesChangedEventArgs>? availabilityChanged = this.availabilityChanged;
			if (availabilityChanged == null)
			{
				// No one cares.
				return;
			}

			ImmutableDictionary<ServiceSource, ImmutableDictionary<ServiceMoniker, IProffered>>? oldIndex = args.OldIndex;
			ImmutableHashSet<ServiceMoniker> impactedServices = args.ImpactedServices;

			// Filter down to just those services that our client has asked about.
			IImmutableSet<ServiceMoniker> impactedAndObservedServices;
			lock (this.SyncObject)
			{
				// Record which services were both impacted by the sender and observed by this view.
				impactedAndObservedServices = impactedServices.Intersect(this.observedServices);

				// Remove the impacted services from our observed collection since we're raising a changed event now.
				// No more events will be raised regarding these impacted services unless they are queried for again.
				this.observedServices.ExceptWith(impactedAndObservedServices);
			}

			if (oldIndex != null)
			{
				// This change came from a change to the proffering tables (not just an backing service broker internal change).
				// Further filter down by verifying that the proffering source for each service was *actually* impacted.
				// If a local service proffering changed, but we're connected to a remote environment where the service is expected to come from,
				// then there's no meaningful change to the service from the consumer's perspective for example.
				foreach (ServiceMoniker moniker in impactedAndObservedServices)
				{
					bool oldResult = this.container.TryGetProfferingSource(oldIndex, moniker, this.Audience, out IProffered? oldProffered, out _);
					bool newResult = this.container.TryGetProfferingSource(moniker, this.Audience, out IProffered? newProffered, out _);
					if (oldResult == newResult && oldProffered == newProffered)
					{
						// The source of this service hasn't actually changed, so don't tell consumers that it has.
						impactedAndObservedServices = impactedAndObservedServices.Remove(moniker);
					}
				}
			}

			if (impactedAndObservedServices.Count == 0)
			{
				// Avoid raising the event if it's essentially empty after we've applied our filter.
				return;
			}

			// We do not filter the monikers to just those the client is privileged to see, because they've already asked about the service
			// since they asked about it earlier. And it's possible that the change to the service is a proffering change where the availability for this client changed.
			try
			{
				availabilityChanged.Invoke(this, new BrokeredServicesChangedEventArgs(impactedAndObservedServices, otherServicesImpacted: false));
			}
			catch
			{
				// TODO: report failures as a fault in telemetry.
			}
		}

		internal async ValueTask<(IProffered? ProfferingSource, MissingBrokeredServiceErrorCode ErrorCode)> TryGetProfferingSourceAsync(ServiceMoniker serviceMoniker, CancellationToken cancellationToken)
		{
			if (this.container.TryGetProfferingSource(serviceMoniker, this.Audience, out IProffered? proffered, out MissingBrokeredServiceErrorCode errorCode))
			{
				return (proffered, errorCode);
			}

			if (errorCode == MissingBrokeredServiceErrorCode.ServiceFactoryNotProffered)
			{
				if (await this.LoadProfferingPackageAsync(serviceMoniker, cancellationToken).ConfigureAwait(false))
				{
					if (this.container.TryGetProfferingSource(serviceMoniker, this.Audience, out proffered, out errorCode))
					{
						return (proffered, errorCode);
					}
				}
			}

			return (null, errorCode);
		}

		private async Task<bool> LoadProfferingPackageAsync(ServiceMoniker serviceMoniker, CancellationToken cancellationToken)
		{
			if (!this.container.TryLookupServiceRegistration(serviceMoniker, out ServiceRegistration? serviceRegistration, out _)
				|| serviceRegistration.ProfferingPackageId is null)
			{
				if (this.container.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
				{
					this.container.traceSource.TraceEvent(TraceEventType.Warning, (int)TraceEvents.LoadPackage, "Brokered service \"{0}\" has no proffering source and no package registered to load.", serviceMoniker);
				}

				return false;
			}

			// We found the service and its proffering package. Only load the package if the client has access to the service.
			if (!serviceRegistration.IsExposedTo(this.Audience))
			{
				if (this.container.traceSource.Switch.ShouldTrace(TraceEventType.Warning))
				{
					this.container.traceSource.TraceEvent(
						TraceEventType.Warning,
						(int)TraceEvents.LoadPackage,
						"Proffering package for brokered service \"{0}\" will not be loaded because the requesting party ({1}) isn't allowed to call this service (with audience {2}) anyway.",
						serviceMoniker,
						this.Audience,
						serviceRegistration.Audience);
				}

				return false;
			}

			// Perf optimization: skip all the work if we've already done it for this moniker before.
			if (this.container.IsPackageLoaded(serviceRegistration.ProfferingPackageId) || serviceRegistration.ProfferingPackageId is null)
			{
				return false;
			}

			await serviceRegistration.LoadProfferingPackageAsync(cancellationToken).ConfigureAwait(false);
			this.container.RecordPackageLoaded(serviceRegistration.ProfferingPackageId);

			if (this.container.traceSource.Switch.ShouldTrace(TraceEventType.Information))
			{
				this.container.traceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.LoadPackage, "Proffering package {0} for brokered service \"{1}\" is now loaded.", serviceRegistration.ProfferingPackageId, serviceMoniker);
			}

			return true;
		}

		private ServiceActivationOptions ApplyOptionsFilter(ServiceActivationOptions options)
		{
			if (this.clientCredentialsPolicy == ClientCredentialsPolicy.FilterOverridesRequest || (options.ClientCredentials?.Count ?? 0) == 0)
			{
				options.ClientCredentials = this.clientCredentials;
			}

			options.ClientCulture ??= this.clientCulture;
			options.ClientUICulture ??= this.clientUICulture;

			return options;
		}
	}
}
