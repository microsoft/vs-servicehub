// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// This delegate is modeled after <see cref="ProfferedServiceFactory"/> but adds the <see cref="View"/> parameter.
	/// </summary>
	private delegate ValueTask<object?> ViewIntrinsicBrokeredServiceFactory(View view, ServiceMoniker moniker, ServiceActivationOptions options, IServiceBroker serviceBroker, CancellationToken cancellationToken);

	private class ProfferedViewIntrinsicService : ProfferedServiceFactory
	{
		private readonly ViewIntrinsicBrokeredServiceFactory factory;

		internal ProfferedViewIntrinsicService(GlobalBrokeredServiceContainer container, ServiceRpcDescriptor descriptor, ViewIntrinsicBrokeredServiceFactory factory)
			: base(container, descriptor, (mk, options, sb, ct) => throw new NotSupportedException())
		{
			this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
		}

		public override ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public override ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			throw new NotSupportedException();
		}

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
			descriptor = descriptor
				.WithTraceSource(await this.Container.GetTraceSourceForConnectionAsync(serviceBroker, serviceMoniker, options, clientRole: false, cancellationToken).ConfigureAwait(false));

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
					connection.AddLocalRpcTarget(server);
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

		public async ValueTask<T?> GetProxyAsync<T>(View view, ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			IServiceBroker serviceBroker = this.Container.GetSecureServiceBroker(options);
			serviceDescriptor = serviceDescriptor
				.WithTraceSource(await this.Container.GetTraceSourceForConnectionAsync(serviceBroker, serviceDescriptor.Moniker, options, clientRole: false, cancellationToken).ConfigureAwait(false));
			var liveObject = (T?)await this.factory(view, serviceDescriptor.Moniker, options, serviceBroker, cancellationToken).ConfigureAwait(false);
			return serviceDescriptor.ConstructLocalProxy(liveObject);
		}

		public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(View view, ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken = default)
		{
			return this.RemoteServiceBrokerWrapper.RequestServiceChannelAsync(
				() => this.GetPipeAsync(view, serviceMoniker, serviceActivationOptions, cancellationToken),
				serviceMoniker,
				serviceActivationOptions,
				cancellationToken);
		}

		private Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}
	}
}
