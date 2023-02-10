/** Policies that may apply to how to treat credentials. */
export enum ClientCredentialsPolicy {
	/** If the service request carries client credentials with it, use that instead of what this filter would apply. */
	requestOverridesDefault,
	/** Always replace the client credentials on a request with the set specified on this filter. */
	filterOverridesRequest,
}
