// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Runtime.Serialization;
using Nerdbank.Streams;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Activation options that may optionally be supplied to a service when activating it.
/// </summary>
/// <remarks>
/// This type must use only built-in types since every applicable <see cref="IRemoteServiceBroker"/> is required to be able to directly serialize it.
/// </remarks>
[DataContract]
public struct ServiceActivationOptions : IEquatable<ServiceActivationOptions>
{
	/// <summary>
	/// Gets or sets a map of arbitrary data, presumably meaningful to the service.
	/// </summary>
	/// <value>May be null.</value>
	[DataMember]
	public IReadOnlyDictionary<string, string>? ActivationArguments { get; set; }

	/// <summary>
	/// Gets or sets a map that describes the client's identity in terms that an identity or authorization service can interpret.
	/// </summary>
	[DataMember]
	public IReadOnlyDictionary<string, string>? ClientCredentials { get; set; }

	/// <summary>
	/// Gets or sets the client's preferred culture.
	/// </summary>
	[Newtonsoft.Json.JsonConverter(typeof(CultureInfoJsonConverter))]
	[DataMember]
	public CultureInfo? ClientCulture { get; set; }

	/// <summary>
	/// Gets or sets the client's preferred UI culture.
	/// </summary>
	[Newtonsoft.Json.JsonConverter(typeof(CultureInfoJsonConverter))]
	[DataMember]
	public CultureInfo? ClientUICulture { get; set; }

	/// <summary>
	/// Gets or sets an RPC target that the client offers to the requested service so the service can invoke members on the client.
	/// </summary>
	/// <remarks>
	/// This object is never serialized.
	/// If the service is available locally this object is made available directly to the service.
	/// If the service is remote, the remote service broker client such as <see cref="RemoteServiceBroker"/> should set this object as the local RPC target when setting up an RPC connection,
	/// and the service-side should set up a proxy for this object based on the type given in <see cref="ServiceRpcDescriptor.ClientInterface"/>.
	/// </remarks>
	[Newtonsoft.Json.JsonIgnore]
	[IgnoreDataMember]
	public object? ClientRpcTarget { get; set; }

	/// <summary>
	/// Gets or sets the <see cref="Nerdbank.Streams.MultiplexingStream"/> associated with the connection
	/// between the client and the service broker.
	/// This may be used to establish additional channels between client and service.
	/// </summary>
	/// <remarks>
	/// This object is never serialized.
	/// If the service is available locally this object can be ignored by the broker and service because client and service can exchange streams directly.
	/// If the service is remote, the <see cref="IRemoteServiceBroker"/> such as <see cref="MultiplexingRelayServiceBroker"/> should set this property on the activation options
	/// before forwarding the request to the final service broker.
	/// The final service broker should then apply this value to the <see cref="ServiceRpcDescriptor"/> using <see cref="ServiceRpcDescriptor.WithMultiplexingStream(MultiplexingStream)"/>.
	/// </remarks>
	[Newtonsoft.Json.JsonIgnore]
	[IgnoreDataMember]
	public MultiplexingStream? MultiplexingStream { get; set; }

	/// <summary>
	/// Automatically set properties on this type where possible based on the client environment,
	/// if they have not already had values assigned.
	/// </summary>
	public void SetClientDefaults()
	{
		this.ClientCulture ??= CultureInfo.CurrentCulture;
		this.ClientUICulture ??= CultureInfo.CurrentUICulture;
	}

	/// <summary>
	/// Applies the values of <see cref="ClientCulture"/> and <see cref="ClientUICulture"/> to the current <see cref="ExecutionContext"/>, if they have been set on this struct.
	/// </summary>
	/// <returns>A value to dispose of to revert the <see cref="CultureInfo"/> properties to their prior values.</returns>
	/// <remarks>
	/// By surrounding construction of a <see cref="ServiceRpcDescriptor.RpcConnection"/> with the client's applied culture,
	/// that connection is expected to pick it up and dispatch incoming RPC requests using that culture.
	/// </remarks>
	public CultureApplication ApplyCultureToCurrentContext() => new CultureApplication(this.ClientCulture, this.ClientUICulture);

	/// <inheritdoc />
	public bool Equals(ServiceActivationOptions other)
	{
		return EqualityComparer<CultureInfo?>.Default.Equals(this.ClientCulture, other.ClientCulture)
			&& EqualityComparer<CultureInfo?>.Default.Equals(this.ClientUICulture, other.ClientUICulture)
			&& Equals(this.ActivationArguments, other.ActivationArguments)
			&& Equals(this.ClientCredentials, other.ClientCredentials);
	}

	/// <summary>
	/// Compares content equality between two dictionaries.
	/// </summary>
	/// <param name="dictionary1">The first dictionary.</param>
	/// <param name="dictionary2">The second dictionary.</param>
	/// <returns><see langword="true"/> if the two instances are equal; <see langword="false"/> otherwise.</returns>
	private static bool Equals(IReadOnlyDictionary<string, string>? dictionary1, IReadOnlyDictionary<string, string>? dictionary2)
	{
		if (ReferenceEquals(dictionary1, dictionary2))
		{
			return true;
		}

		if (dictionary1 is null || dictionary2 is null)
		{
			return false;
		}

		if (dictionary1.Count != dictionary2.Count)
		{
			return false;
		}

		foreach (KeyValuePair<string, string> pair in dictionary1)
		{
			if (!dictionary2.TryGetValue(pair.Key, out string? value2))
			{
				return false;
			}

			if (pair.Value != value2)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// A disposable struct for applying and reverting changes to <see cref="CultureInfo"/>.
	/// </summary>
	public struct CultureApplication : IDisposable
	{
		private readonly CultureInfo? originalCulture;
		private readonly CultureInfo? originalUICulture;

		/// <summary>
		/// Initializes a new instance of the <see cref="CultureApplication"/> struct and
		/// applies <see cref="CultureInfo"/> as specified by the owner to the calling thread.
		/// </summary>
		/// <param name="newCulture">The new value for <see cref="CultureInfo.CurrentCulture"/>.</param>
		/// <param name="newUICulture">The new value for <see cref="CultureInfo.CurrentUICulture"/>.</param>
		internal CultureApplication(CultureInfo? newCulture, CultureInfo? newUICulture)
		{
			if (newCulture is object)
			{
				(this.originalCulture, CultureInfo.CurrentCulture) = (CultureInfo.CurrentCulture, newCulture);
			}
			else
			{
				this.originalCulture = null;
			}

			if (newUICulture is object)
			{
				(this.originalUICulture, CultureInfo.CurrentUICulture) = (CultureInfo.CurrentUICulture, newUICulture);
			}
			else
			{
				this.originalUICulture = null;
			}
		}

		/// <summary>Reverts changes to <see cref="CultureInfo"/> that were made by the constructor.</summary>
		public void Dispose()
		{
			if (this.originalCulture is object)
			{
				CultureInfo.CurrentCulture = this.originalCulture;
			}

			if (this.originalUICulture is object)
			{
				CultureInfo.CurrentUICulture = this.originalUICulture;
			}
		}
	}
}
