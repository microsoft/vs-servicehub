/** Defines the several reasons a brokered service might not be obtained. */
export enum MissingBrokeredServiceErrorCode {
	/**
	 * Nothing could be found wrong to explain the missing service.
	 * It may be available now.
	 */
	noExplanation,

	/**
	 * The requested service had no match in the local service registry.
	 * @remarks All services, whether local or remote, must be in the local registry in order to be acquired locally.
	 */
	notLocallyRegistered,

	/**
	 * Special resiliency testing configuration is in place and denied access to this service.
	 */
	chaosConfigurationDeniedRequest,

	/**
	 * The service is not exposed to the audience making the request.
	 */
	serviceAudienceMismatch,

	/**
	 * The service is registered but no factory has been loaded for it.
	 */
	serviceFactoryNotProffered,

	/**
	 * The service factory returned null instead of an instance of the service.
	 */
	serviceFactoryReturnedNull,

	/**
	 * The service factory threw an exception.
	 */
	serviceFactoryFault,

	/**
	 * The service is expected to come from a remote server
	 * but the connection is not ready yet or the server does not offer it.
	 * A locally proffered service is not available when it also can come remotely and a remote connection exists or is expected.
	 */
	localServiceHiddenOnRemoteClient,
}
