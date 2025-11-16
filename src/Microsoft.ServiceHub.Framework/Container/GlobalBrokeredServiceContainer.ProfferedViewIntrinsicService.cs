// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Nerdbank.Streams;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// A delegate that creates new instances of a service to be exposed by an <see cref="IServiceBroker" />.
	/// </summary>
	/// <param name="view">The view that this service is being activated within.</param>
	/// <param name="moniker">The identifier for the service that is requested.</param>
	/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor.</param>
	/// <param name="serviceBroker">The service broker that the service returned from this delegate should use to obtain any of its own dependencies.</param>
	/// <param name="cancellationToken">A token to indicate that the caller has lost interest in the result.</param>
	/// <returns>A unique instance of the service. If the value implements <see cref="IDisposable" />, the value will be disposed when the client disconnects.</returns>
	/// <seealso cref="IBrokeredServiceContainer.Proffer(ServiceRpcDescriptor, BrokeredServiceFactory)"/>
	/// <remarks>
	/// This delegate is modeled after <see cref="ProfferedServiceFactory"/> but adds the <see cref="View"/> parameter.
	/// </remarks>
	protected delegate ValueTask<object?> ViewIntrinsicBrokeredServiceFactory(View view, ServiceMoniker moniker, ServiceActivationOptions options, IServiceBroker serviceBroker, CancellationToken cancellationToken);

	/// <summary>
	/// Services a brokered service that is proffered via an in-proc factory.
	/// </summary>
	[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
	[RequiresUnreferencedCode(Reasons.TypeLoad)]
	protected class ProfferedViewIntrinsicService : ProfferedServiceFactory
	{
		private readonly ViewIntrinsicBrokeredServiceFactory factory;

		internal ProfferedViewIntrinsicService(GlobalBrokeredServiceContainer container, ServiceRpcDescriptor descriptor, ViewIntrinsicBrokeredServiceFactory factory)
			: base(container, descriptor, (mk, options, sb, ct) => throw new NotSupportedException())
		{
			this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
		}

		/// <inheritdoc/>
		[Obsolete("Use the overload that takes a View instead.", error: true)]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
		public override ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
		{
			throw new NotSupportedException();
		}

		/// <inheritdoc/>
		[Obsolete("Use the overload that takes a View instead.", error: true)]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
		public override ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
			where T : class
		{
			throw new NotSupportedException();
		}

#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
		/// <inheritdoc cref="GetPipeAsync(ServiceMoniker, ServiceActivationOptions, CancellationToken)"/>
		/// <param name="view">The view used to request this service.</param>
		public async ValueTask<IDuplexPipe?> GetPipeAsync(View view, ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		{
			(IDuplexPipe, IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();

			ServiceRpcDescriptor descriptor = this.Descriptor is ServiceJsonRpcDescriptor serviceJsonRpcDescriptor
				 ? serviceJsonRpcDescriptor.MultiplexingStreamOptions is object ? this.Descriptor :

					// We encourage users to migrate to descriptors configured with ServiceJsonRpcDescriptor.WithMultiplexingStream(MultiplexingStream.Options).
#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
					this.Descriptor.WithMultiplexingStream(options.MultiplexingStream)
#pragma warning restore CS0618 // Type or member is obsolete
				 : this.Descriptor;

			IServiceBroker serviceBroker = this.Container.GetSecureServiceBroker(options);
			descriptor = await this.Container.ApplyDescriptorSettingsInternalAsync(descriptor, serviceBroker, options, clientRole: false, cancellationToken).ConfigureAwait(false);

			using (options.ApplyCultureToCurrentContext())
			{
				ServiceRpcDescriptor.RpcConnection connection = descriptor.ConstructRpcConnection(pipePair.Item1);

				// If the service needs to be able to call back to the client, arrange for it.
				// If the client is remote, we need to create an RPC proxy back to the client.
				// If the client is local, it should provide itself as the RPC target. So we only provide one if the client hasn't already done so.
				// FWIW: it would be pretty odd if (ClientRpcTarget != null) since they're asking for a pipe so presumably they are remote.
				if (descriptor.ClientInterface != null && options.ClientRpcTarget == null)
				{
					options.ClientRpcTarget = connection.ConstructRpcClient(descriptor.ClientInterface);
				}

				object? server = await this.factory(view, descriptor.Moniker, options, serviceBroker, cancellationToken).ConfigureAwait(false);
				if (server != null)
				{
					connection.AddClientLocalRpcTarget(server);
					connection.StartListening();
					return pipePair.Item2;
				}
				else
				{
					connection.Dispose();
				}

				return null;
			}
		}

		/// <inheritdoc cref="GetProxyAsync{T}(ServiceRpcDescriptor, ServiceActivationOptions, CancellationToken)"/>
		/// <param name="view">The view used to request this service.</param>
		public async ValueTask<T?> GetProxyAsync<T>(View view, ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			IServiceBroker serviceBroker = this.Container.GetSecureServiceBroker(options);
			serviceDescriptor = await this.Container.ApplyDescriptorSettingsInternalAsync(serviceDescriptor, serviceBroker, options, clientRole: false, cancellationToken).ConfigureAwait(false);
			var liveObject = (T?)await this.factory(view, serviceDescriptor.Moniker, options, serviceBroker, cancellationToken).ConfigureAwait(false);
			return serviceDescriptor.ConstructLocalProxy(liveObject);
		}
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)

		internal Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(View view, ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken = default)
		{
			return this.RemoteServiceBrokerWrapper.RequestServiceChannelAsync(
				() => this.GetPipeAsync(view, serviceMoniker, serviceActivationOptions, cancellationToken),
				serviceMoniker,
				serviceActivationOptions,
				cancellationToken);
		}
	}
}
