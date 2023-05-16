import * as immutable from 'immutable'
import type { BrokeredServiceFactory, IBrokeredServiceContainer } from './IBrokeredServiceContainer'
import type { ServiceRegistration } from './ServiceRegistration'
import { ServiceSource } from './ServiceSource'
import type { IProffered } from './IProffered'
import { View } from './View'
import { ProfferedServiceFactory } from './ProfferedServiceFactory'
import { ServiceAudience } from './ServiceAudience'
import { MissingBrokeredServiceErrorCode } from './MissingBrokeredServiceErrorCode'
import { ClientCredentialsPolicy } from './ClientCredentialsPolicy'
import { MultiplexingStream } from 'nerdbank-streams'
import { ProfferedRemoteServiceBroker } from './ProfferedRemoteServiceBroker'
import { ProfferedServiceBroker } from './ProfferedServiceBroker'
import { ServiceMonikerValue } from './ServiceMonikerValue'
import { IDisposable } from '../IDisposable'
import { IRemoteServiceBroker } from '../IRemoteServiceBroker'
import { IServiceBroker } from '../IServiceBroker'
import { ServiceActivationOptions } from '../ServiceActivationOptions'
import { ServiceMoniker } from '../ServiceMoniker'
import { ServiceRpcDescriptor } from '../ServiceRpcDescriptor'

export class GlobalBrokeredServiceContainer implements IBrokeredServiceContainer {
	/** Defines the order of sources to check for remote services. */
	private static readonly preferredSourceOrderForRemoteServices = [ServiceSource.trustedServer, ServiceSource.untrustedServer]

	/** Defines the order of sources to check for locally proffered services. */
	private static readonly preferredSourceOrderForLocalServices = [ServiceSource.sameProcess, ServiceSource.otherProcessOnSameMachine]

	private remoteSources = immutable.Map<ServiceSource, IProffered>()
	private profferedServiceIndex = immutable.Map<ServiceSource, immutable.Map<ServiceMonikerValue, IProffered>>()
	private registeredServices = immutable.Map<ServiceMonikerValue, ServiceRegistration>()

	private readonly localUserCredentials = immutable.Map<string, string>()

	constructor() {}

	register(
		services: {
			moniker: ServiceMoniker
			registration: ServiceRegistration
		}[]
	): IDisposable {
		let registeredServices = this.registeredServices
		const services2 = services.map(o => {
			return { moniker: ServiceMonikerValue.from(o.moniker), registration: o.registration }
		})
		services2.forEach(service => {
			if (this.registeredServices.has(service.moniker)) {
				throw new Error(`${ServiceMoniker.toString(service.moniker)} is already registered.`)
			}

			registeredServices = registeredServices.set(service.moniker, service.registration)
		})

		this.registeredServices = registeredServices

		let removed = false
		return {
			dispose: () => {
				if (!removed) {
					services2.forEach(service => (this.registeredServices = this.registeredServices.delete(service.moniker)))
					removed = true
				}
			},
		}
	}

	profferServiceFactory(descriptor: ServiceRpcDescriptor, factory: BrokeredServiceFactory): IDisposable {
		return this.profferInternal(new ProfferedServiceFactory(this, descriptor, factory))
	}

	profferServiceBroker(serviceBroker: IServiceBroker, serviceMonikers: readonly ServiceMoniker[]): IDisposable {
		return this.profferInternal(new ProfferedServiceBroker(this, serviceBroker, ServiceSource.sameProcess, serviceMonikers))
	}

	profferRemoteServiceBroker(
		serviceBroker: IRemoteServiceBroker,
		multiplexingStream: MultiplexingStream | null,
		source: ServiceSource,
		serviceMonikers: Readonly<ServiceMoniker[]> | null
	): IDisposable {
		if (source === ServiceSource.sameProcess) {
			throw new Error('Use the proffer method for local services when they are in the same process.')
		}

		return this.profferInternal(
			new ProfferedRemoteServiceBroker(
				this,
				serviceBroker,
				multiplexingStream,
				source,
				this.getAllowedMonikers(source, serviceMonikers?.map(ServiceMonikerValue.from) ?? null)
			)
		)
	}

	getFullAccessServiceBroker(): IServiceBroker & IRemoteServiceBroker {
		return new View(this, ServiceAudience.process, this.localUserCredentials, ClientCredentialsPolicy.requestOverridesDefault)
	}

	/**
	 * Gets a service broker that targets an out of proc and/or less trusted consumer.
	 * @param audience The architectural position of the consumer.
	 * @param clientCredentials The client credentials to associate with this consumer, if less trusted.
	 * @param credentialPolicy How to apply the client credentials to individual service requests.
	 * @returns The custom service broker.
	 */
	getLimitedAccessServiceBroker(
		audience: ServiceAudience,
		clientCredentials: immutable.Map<string, string>,
		credentialPolicy: ClientCredentialsPolicy
	): IServiceBroker & IRemoteServiceBroker {
		return new View(this, audience, clientCredentials, credentialPolicy)
	}

	private static isLocalConsumer(filter: ServiceAudience) {
		return (filter & ~ServiceAudience.local) === ServiceAudience.none
	}

	getSecureServiceBroker(options?: ServiceActivationOptions): IServiceBroker {
		return new View(
			this,
			ServiceAudience.process,
			options?.clientCredentials ? immutable.Map(options.clientCredentials) : immutable.Map(),
			ClientCredentialsPolicy.requestOverridesDefault
		)
	}

	getServiceRegistration(moniker: ServiceMonikerValue): { registration: ServiceRegistration; matchingMoniker: ServiceMonikerValue } | null {
		let match = this.registeredServices.get(moniker)
		if (match) {
			return { registration: match, matchingMoniker: moniker }
		}

		if (moniker.version) {
			const versionlessMoniker = new ServiceMonikerValue(moniker.name)
			return this.getServiceRegistration(versionlessMoniker)
		}

		return null
	}

	async getProfferingSource(
		serviceMoniker: ServiceMoniker,
		consumingAudience: ServiceAudience
	): Promise<{ proffered?: IProffered; errorCode: MissingBrokeredServiceErrorCode }> {
		const serviceMonikerValue = ServiceMonikerValue.from(serviceMoniker)
		const { registration, matchingMoniker } = this.getServiceRegistration(serviceMonikerValue) || {}
		if (!registration || !matchingMoniker) {
			return {
				errorCode: MissingBrokeredServiceErrorCode.notLocallyRegistered,
			}
		}

		// If the consumer is local, we're willing to provide services to them from remote sources.
		// Specifically: We don't provide remote consumers with remote services.
		// We do NOT check whether the consuming (local) audience should have visibility into the remotely acquired services
		// because the audience check was performed and services filtered when the remote service broker was originally proffered.
		let anyRemoteSourceExists = false
		if (GlobalBrokeredServiceContainer.isLocalConsumer(consumingAudience)) {
			// TODO: search remote sources
		}

		// For locally proffered services, we first check that the consuming audience is allowed to see it.
		// We only reach this far if the requested service was not expected to come from a remote source that we checked above.
		if (!registration.isExposedTo(consumingAudience)) {
			return {
				errorCode: MissingBrokeredServiceErrorCode.serviceAudienceMismatch,
			}
		}

		if (registration.profferCallback) {
			// We should only invoke a registration callback *once*,
			// but if it returns a promise, we and all subsequent consumers must wait for it to resolve.
			if (typeof registration.profferCallback === 'function') {
				const callbackResult = registration.profferCallback(this, serviceMoniker)
				registration.profferCallback = callbackResult ? callbackResult : undefined
			}

			await registration.profferCallback
		}

		for (const source of GlobalBrokeredServiceContainer.preferredSourceOrderForLocalServices) {
			const proffersFromSource = this.profferedServiceIndex.get(source)
			if (proffersFromSource) {
				const proffered = proffersFromSource.get(matchingMoniker)
				if (proffered) {
					return {
						proffered,
						errorCode: MissingBrokeredServiceErrorCode.noExplanation,
					}
				}
			}
		}

		return {
			errorCode: MissingBrokeredServiceErrorCode.serviceFactoryNotProffered,
		}
	}

	removeRegistrations(proffered: IProffered) {
		const oldIndex = this.profferedServiceIndex
		switch (proffered.source) {
			case ServiceSource.sameProcess:
			case ServiceSource.otherProcessOnSameMachine:
				let profferedServices = this.profferedServiceIndex.get(proffered.source)
				if (profferedServices) {
					profferedServices = profferedServices.removeAll(proffered.monikers)
					this.profferedServiceIndex = this.profferedServiceIndex.set(proffered.source, profferedServices)
				}

				break

			default:
				// Non-local sources are only allowed to proffer one thing, so remove that.
				this.profferedServiceIndex = this.profferedServiceIndex.remove(proffered.source)
				this.remoteSources = this.remoteSources.remove(proffered.source)
				break
		}

		this.onAvailabilityChanged(oldIndex, proffered)
	}

	onAvailabilityChanged(
		old: immutable.Map<ServiceSource, immutable.Map<ServiceMoniker, IProffered>> | null,
		proffered: IProffered,
		impactedServices?: ServiceMoniker[]
	) {
		// TODO: code here
	}

	/**
	 * Filters the registered service monikers to those that should be visible to our process from a given source
	 * then optionally intersects that set with another set.
	 * @param source The source of services that we should filter our registered list of services to.
	 * @param serviceMonikers The set of monikers to optionally intersect the filtered registered services with.
	 * @returns The filtered, intersected set of monikers.
	 */
	private getAllowedMonikers(source: ServiceSource, serviceMonikers: readonly ServiceMonikerValue[] | null) {
		// Determine what service audience would be required on any service registration
		// in order for us to be willing to consume the proffered service, considering our position
		// relative to the service source.
		// For example if the services are coming from a Codespace Server,
		// then we would only consume services from it that we know are exposed to a Codespace Client.
		const ourAudienceRelativeToSource = GlobalBrokeredServiceContainer.convertRemoteSourceToLocalAudience(source)

		// We need to look up the monikers for all the services that are allowed to come from this source.
		// We consider the service consumable from the remote if and only if
		// we would expose it over the same kind of connection if we were on the proffering side.
		// When "remote" is just another process on this same machine (e.g. ServiceHub),
		// we index the service even if this process can't consume it so we can expose it remotely.
		// TryGetProfferingSource will prevent *this* process from consuming when Local isn't an audience.
		const allowedMonikers: ServiceMonikerValue[] = []
		if (serviceMonikers === null) {
			for (let [moniker, registration] of this.registeredServices) {
				// We consider the service consumable from the remote if and only if
				// we would expose it over the same kind of connection if we were on the proffering side.
				// When "remote" is just another process on this same machine (e.g. ServiceHub),
				// we index the service even if this process can't consume it so we can expose it remotely.
				// TryGetProfferingSource will prevent *this* process from consuming when Local isn't an audience.
				if (
					(registration.audience & ourAudienceRelativeToSource) === ourAudienceRelativeToSource ||
					source === ServiceSource.otherProcessOnSameMachine
				) {
					allowedMonikers.push(moniker)
				}
			}
		} else {
			for (let moniker of serviceMonikers) {
				const registration = this.registeredServices.get(moniker)
				if (
					registration &&
					((registration.audience & ourAudienceRelativeToSource) === ourAudienceRelativeToSource ||
						source === ServiceSource.otherProcessOnSameMachine)
				) {
					allowedMonikers.push(moniker)
				}
			}
		}

		return allowedMonikers
	}

	private static convertRemoteSourceToLocalAudience(source: ServiceSource): ServiceAudience {
		switch (source) {
			case ServiceSource.otherProcessOnSameMachine:
				return ServiceAudience.local
			case ServiceSource.trustedServer:
				return ServiceAudience.liveShareGuest
			case ServiceSource.untrustedServer:
				return ServiceAudience.liveShareGuest
			default:
				throw new Error(`Source not recognized: ${source}`)
		}
	}

	private profferInternal(proffered: IProffered): IDisposable {
		const oldIndex = this.profferedServiceIndex

		if (proffered.source > ServiceSource.otherProcessOnSameMachine) {
			this.remoteSources.set(proffered.source, proffered)
		}

		let monikerAndProffer: immutable.Map<ServiceMonikerValue, IProffered> = this.profferedServiceIndex.get(proffered.source) ?? immutable.Map()
		const unregisteredMonikers: ServiceMoniker[] = []
		proffered.monikers.forEach(moniker => {
			if (!this.registeredServices.has(moniker)) {
				unregisteredMonikers.push(moniker)
			}

			if (monikerAndProffer.has(moniker)) {
				throw new Error(`${ServiceMoniker.toString(moniker)} is already proffered.`)
			}

			monikerAndProffer = monikerAndProffer.set(moniker, proffered)
		})

		if (unregisteredMonikers.length > 0) {
			throw new Error(`Cannot proffer unregistered service(s): ${unregisteredMonikers.map(m => ServiceMoniker.toString(m)).join(', ')}`)
		}

		// Index each service, and also validate that all proffered services are registered.
		this.profferedServiceIndex = this.profferedServiceIndex.set(proffered.source, monikerAndProffer)

		this.onAvailabilityChanged(oldIndex, proffered)

		return proffered
	}
}
