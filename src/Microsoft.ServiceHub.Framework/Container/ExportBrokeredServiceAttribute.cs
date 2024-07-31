// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.Composition;
using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Exports a class as a brokered service.
/// </summary>
/// <remarks>
/// <para>The class that this attribute is applied to must implement <see cref="IExportedBrokeredService"/>.</para>
/// <para>Any other MEF attributes used by the class with this attribute applied should come from the System.ComponentModel.Composition namespace.</para>
/// <para>This attribute may be applied multiple times if multiple versions of the brokered service are supported.</para>
/// <para>
/// Exported brokered services may import any other MEF export from the default scope, along with the following types (with no explicit contract name):
/// <list type="bullet">
/// <item><see cref="IServiceBroker"/></item>
/// <item><see cref="ServiceMoniker"/></item>
/// <item><see cref="ServiceActivationOptions"/></item>
/// <item><see cref="ServiceHub.Framework.Services.AuthorizationServiceClient"/></item>
/// </list>
/// </para>
/// <para>Brokered services may not import other brokered service. They must use <see cref="IServiceBroker"/> to acquire them.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
[MetadataAttribute]
public class ExportBrokeredServiceAttribute : ExportAttribute
{
	private ServiceAudience audience = ServiceAudience.Process;

	/// <inheritdoc cref="ExportBrokeredServiceAttribute(string, string, Type[])"/>
	public ExportBrokeredServiceAttribute(string name, string? version)
		: this(name, version, Type.EmptyTypes)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ExportBrokeredServiceAttribute"/> class.
	/// </summary>
	/// <param name="name">The name of the service (same as <see cref="ServiceMoniker.Name"/>.)</param>
	/// <param name="version">The version of the proffered service (same as <see cref="ServiceMoniker.Version"/>). May be null.</param>
	/// <param name="optionalInterfaces">An array of <em>optional</em> interfaces that the exported brokered service implements and wishes to advertise as available to the client.</param>
	public ExportBrokeredServiceAttribute(string name, string? version, params Type[] optionalInterfaces)
		: base(typeof(IExportedBrokeredService))
	{
		Requires.NotNullOrEmpty(name, nameof(name));
		this.ServiceName = name;
		this.ServiceVersion = version;

		// We must render this as an array of strings so that when the metadata is read back from the MEF cache,
		// it doesn't require loading the assembly that declares the optional interface.
		this.OptionalInterfacesImplemented = optionalInterfaces.Select(t => t.AssemblyQualifiedName!).ToArray();
	}

	/// <summary>
	/// Gets the <see cref="ServiceMoniker.Name"/> of the exported brokered service.
	/// </summary>
	public string ServiceName { get; }

	/// <summary>
	/// Gets the <see cref="ServiceMoniker.Version"/> of the exported brokered service.
	/// </summary>
	public string? ServiceVersion { get; }

	/// <summary>
	/// Gets an array of <see cref="Type.AssemblyQualifiedName">assembly-qualified names</see> of <em>optional</em> interfaces
	/// that the exported brokered service implements.
	/// </summary>
	public string[] OptionalInterfacesImplemented { get; } = [];

	/// <summary>
	/// Gets or sets a value indicating which clients should be allowed to directly acquire this service.
	/// Audiences may be bitwise-OR'd together to expand the set of clients that are allowed to acquire this service.
	/// </summary>
	/// <value>The default value is <see cref="ServiceAudience.Process"/>.</value>
	/// <remarks>
	/// This is an architectural control and not a security boundary, since untrusted parties may acquire a service
	/// that you *do* allow to acquire this service, thus giving indirect access to this service to the untrusted client.
	/// Use <see cref="ServiceHub.Framework.Services.IAuthorizationService"/> (usually via the caching
	/// <see cref="ServiceHub.Framework.Services.AuthorizationServiceClient"/> wrapper) to perform security checks within
	/// your publicly exposed methods to ensure the ultimate client is authorized to perform any operation.
	/// </remarks>
	/// <exception cref="ArgumentException">Thrown when an attempt is made to set this value to <see cref="ServiceAudience.None" />.</exception>
	public ServiceAudience Audience
	{
		get => this.audience;
		set
		{
			Requires.Range(value != ServiceAudience.None, nameof(value));
			this.audience = value;
		}
	}

	/// <summary>
	/// Gets or sets a value indicating whether guest clients are allowed to transitively acquire this service.
	/// By default (<see langword="false"/>), only owners are allowed to access a brokered service. To opt-in to allowing
	/// guests to acquire the proffered service, set this to <see langword="true"/>. By setting this to <see langword="true"/> the service
	/// now has sole responsibility in correctly using <see cref="Microsoft.ServiceHub.Framework.Services.IAuthorizationService"/>
	/// to authorize sensitive operations.
	/// </summary>
	/// <remarks>
	/// <para>Whereas <see cref="Audience"/> is an architectural control, this property defines the security boundary.</para>
	///
	/// <para> Transitive Access Example: Service A performs sensitive operations. It is proffered with <see cref="ServiceAudience.RemoteExclusiveClient"/>
	/// so that it can only be *directly* acquired by owners. However, this is not sufficient to prevent unauthorized access.
	/// If Service B is proffered with <see cref="ServiceAudience.AllClientsIncludingGuests"/>,
	/// it can be *directly* acquired by guests. When Service B internally acquires an instance of Service A, this means that guests now have
	/// *indirect* access to the sensitive operations in Service A. If Service A has not implemented authorization to guard sensitive operations,
	/// this indirect access violates the security boundary.</para>
	///
	/// <para>In order to prevent untrusted parties transitively aquiring a service that should require authorization,
	/// by default all brokered services are only accessible to owners. This is regardless of the value of <see cref="Audience"/>.
	/// In the example above, if Service B has been aquired by a guest, the attempt to acquire Service A will fail.</para>
	///
	/// <para>When a service has implemented authorization to guard sensitive operations, it can opt-in to allowing
	/// guest acquisition by setting this property to <see langword="true"/>.</para>
	/// </remarks>
	public bool AllowTransitiveGuestClients { get; set; }
}

/// <summary>
/// Describes the metadata expected from the <see cref="ExportBrokeredServiceAttribute"/>.
/// </summary>
/// <devremarks>
/// This should stay in sync with the metadata added by that attribute.
/// Each metadata is declared as an array because that attribute has <see cref="AttributeUsageAttribute.AllowMultiple"/>
/// set to <see langword="true"/>.
/// </devremarks>
#pragma warning disable SA1201 // Elements should appear in the correct order
internal interface IBrokeredServicesExportMetadata
#pragma warning restore SA1201 // Elements should appear in the correct order
{
	/// <inheritdoc cref="ExportBrokeredServiceAttribute.ServiceName"/>
	string[] ServiceName { get; }

	/// <inheritdoc cref="ExportBrokeredServiceAttribute.ServiceVersion"/>
	string?[] ServiceVersion { get; }

	/// <inheritdoc cref="ExportBrokeredServiceAttribute.OptionalInterfacesImplemented"/>
	[DefaultValue(null)]
	ImmutableArray<string>[]? OptionalInterfacesImplemented { get; }

	/// <inheritdoc cref="ExportBrokeredServiceAttribute.Audience"/>
	ServiceAudience[] Audience { get; }

	/// <inheritdoc cref="ExportBrokeredServiceAttribute.AllowTransitiveGuestClients"/>
	bool[] AllowTransitiveGuestClients { get; }
}
