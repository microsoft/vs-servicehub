import { Formatters, MessageDelimiters } from './constants'
import { ServiceJsonRpcDescriptor } from './ServiceJsonRpcDescriptor'
import { ServiceMoniker } from './ServiceMoniker'

/**
 * Exposes common RPC descriptors to be used with service brokers
 */
export class FrameworkServices {
	/**
	 * A remote service broker to be used to construct RPC objects and request services
	 * Use {@linkcode IRemoteServiceBroker} as the interface for calling or implementing this service.
	 */
	public static remoteServiceBroker = Object.freeze(
		new ServiceJsonRpcDescriptor(ServiceMoniker.create('RemoteServiceBroker'), Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
	)

	/**
	 * An authorization service descriptor, to be used to request an authorization service.
	 * Use {@linkcode IAuthorizationService} as the interface for calling or implementing this service.
	 */
	public static authorization = Object.freeze(
		new ServiceJsonRpcDescriptor(
			ServiceMoniker.create('Microsoft.ServiceHub.Framework.AuthorizationService'),
			Formatters.Utf8,
			MessageDelimiters.HttpLikeHeaders
		)
	)
}
