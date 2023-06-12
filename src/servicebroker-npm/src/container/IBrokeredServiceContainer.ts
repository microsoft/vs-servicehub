import CancellationToken from 'cancellationtoken'
import { IDisposable } from '../IDisposable'
import { IServiceBroker } from '../IServiceBroker'
import { ServiceActivationOptions } from '../ServiceActivationOptions'
import { ServiceMoniker } from '../ServiceMoniker'
import { ServiceRpcDescriptor } from '../ServiceRpcDescriptor'

type FactoryResult = {} | { dispose?: () => void } | null

export type BrokeredServiceFactory = (
	moniker: ServiceMoniker,
	options: ServiceActivationOptions,
	serviceBroker: IServiceBroker,
	cancellationToken: CancellationToken
) => Promise<FactoryResult> | FactoryResult

/**
 * Provides a means to proffer services into Microsoft.ServiceHub.Framework.IServiceBroker
 * and access to the global Microsoft.ServiceHub.Framework.IServiceBroker.
 */
export interface IBrokeredServiceContainer {
	/**
	 * Proffers an in-proc service for publication via an Microsoft.ServiceHub.Framework.IServiceBroker associated with this container.
	 * @param descriptor The descriptor for the service. The Microsoft.ServiceHub.Framework.ServiceRpcDescriptor.Moniker
	 * is used to match service requests to the factory. The Microsoft.ServiceHub.Framework.ServiceRpcDescriptor.ConstructRpcConnection(System.IO.Pipelines.IDuplexPipe)
	 * method is used to convert the service returned by the factory to a pipe when
	 * the client prefers that.
	 * @param factory The delegate that will create new instances of the service for each client.
	 * @returns A value that can be disposed to remove the proffered service from availability.
	 * @remarks The service identified by the Microsoft.ServiceHub.Framework.ServiceRpcDescriptor.Moniker
	 * must have been pre-registered with a Microsoft.VisualStudio.Shell.ServiceBroker.ServiceAudience
	 * indicating who should have access to it and whether it might be obtained from
	 * a remote machine or user.
	 */
	profferServiceFactory(descriptor: ServiceRpcDescriptor, factory: BrokeredServiceFactory): IDisposable

	/**
	 * Gets an Microsoft.ServiceHub.Framework.IServiceBroker with full access to all
	 * services available to this process with local credentials applied by default
	 * for all service requests. This should *not* be used within a brokered service,
	 * which should instead use the Microsoft.ServiceHub.Framework.IServiceBroker that
	 * is given to its service factory.
	 * @remarks
	 * When a service request is made with an empty set of Microsoft.ServiceHub.Framework.ServiceActivationOptions.ClientCredentials,
	 * local (full) permissions are applied. A service request that includes its own
	 * client credentials may effectively "reduce" permission levels for the requested
	 * service if the service contains authorization checks. It will not remove a service
	 * from availability entirely since the service audience is always to allow all
	 * services to be obtained.
	 * Callers should use the Microsoft.ServiceHub.Framework.IServiceBroker they are
	 * provided via their Microsoft.VisualStudio.Shell.ServiceBroker.BrokeredServiceFactory
	 * instead of using this method to get an Microsoft.ServiceHub.Framework.IServiceBroker
	 * so that they are secure by default. An exception to this rule is when a service
	 * exposed to untrusted users has fully vetted the input for a specific incoming
	 * RPC call and wishes to request other services with full trust in order to accomplish
	 * something the user would otherwise not have permission to do. This should be
	 * done with great care.
	 */
	getFullAccessServiceBroker(): IServiceBroker
}
