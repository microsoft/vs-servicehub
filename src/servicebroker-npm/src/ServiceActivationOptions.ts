import { MultiplexingStream } from 'nerdbank-streams'

/**
 * Describes the settings to use when activating a service
 */
export interface ServiceActivationOptions {
	/**
	 * The client's preferred culture
	 *
	 * @remarks
	 * Should be in form languagecode2-country/regioncode2 (i.e. "en-US")
	 * See [CultureInfo.Name Property](https://docs.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.name?view=netframework-4.8) for more
	 * information about client culture formatting
	 */
	clientCulture?: string

	/**
	 * The client's preferred UI culture
	 *
	 * @remarks
	 * Should be in form languagecode2-country/regioncode2 (i.e. "en-US")
	 * See [CultureInfo.Name Property](https://docs.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.name?view=netframework-4.8) for more
	 * information about client culture formatting
	 */
	clientUICulture?: string

	/**
	 * A map that identifies the client and communicates permissions they have
	 */
	clientCredentials?: { [key: string]: string }

	/**
	 * A map of data that may be useful to the service in its activation
	 */
	activationArguments?: { [key: string]: string }

	/**
	 * The RPC target to offer the remote service for callbacks.
	 */
	clientRpcTarget?: object

	/**
	 * The multiplexing stream associated with the connection
	 * between the client and the service broker. This may be used to establish additional
	 * channels between client and service.
	 * @remarks
	 * This object is never serialized. If the service is available locally this object
	 * can be ignored by the broker and service because client and service can exchange
	 * streams directly. If the service is remote, the Microsoft.ServiceHub.Framework.IRemoteServiceBroker
	 * such as Microsoft.ServiceHub.Framework.MultiplexingRelayServiceBroker should
	 * set this property on the activation options before forwarding the request to
	 * the final service broker. The final service broker should then apply this value
	 * to the ServiceRpcDescriptor using ServiceRpcDescriptor.WithMultiplexingStream(Nerdbank.Streams.MultiplexingStream).
	 */
	multiplexingStream?: MultiplexingStream
}
