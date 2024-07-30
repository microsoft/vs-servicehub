// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using TraceEvents = Microsoft.VisualStudio.Utilities.ServiceBroker.GlobalBrokeredServiceContainer.TraceEvents;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

/// <summary>
/// Brokered service registration information.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
public class ServiceRegistration
{
	private ImmutableArray<Type> additionalServiceInterfacesResolvedTypes;

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceRegistration"/> class.
	/// </summary>
	/// <param name="audience">The audience that may consume this brokered service.</param>
	/// <param name="profferingPackageId">The ID of the brokered service host that may need to be activated in order to proffer the service factory.</param>
	/// <param name="allowGuestClients">A value indicating whether remote guests should be allowed to access this service.</param>
	public ServiceRegistration(ServiceAudience audience, object? profferingPackageId, bool allowGuestClients)
	{
		this.Audience = audience;
		this.ProfferingPackageId = profferingPackageId;
		this.AllowGuestClients = allowGuestClients;
	}

	/// <summary>
	/// Gets the intended audiences for this service.
	/// </summary>
	public ServiceAudience Audience { get; }

	/// <summary>
	/// Gets a value indicating whether this service is exposed to non-Owner clients.
	/// </summary>
	public bool AllowGuestClients { get; }

	/// <summary>
	/// Gets the ID of the package to load so that this service will actually be proffered.
	/// </summary>
	/// <remarks>
	/// If this is null, the <see cref="LoadProfferingPackageAsync(CancellationToken)"/> method can be assumed to be a no-op.
	/// </remarks>
	public object? ProfferingPackageId { get; }

	/// <summary>
	/// Gets a value indicating whether this service is exposed to local clients relative to itself.
	/// </summary>
	public bool IsExposedLocally => (this.Audience & ServiceAudience.Local) != ServiceAudience.None;

	/// <summary>
	/// Gets a value indicating whether this service is exposed to remote clients relative to itself.
	/// </summary>
	public bool IsExposedRemotely => (this.Audience & (ServiceAudience.LiveShareGuest | ServiceAudience.RemoteExclusiveClient | ServiceAudience.RemoteExclusiveServer)) != ServiceAudience.None;

	/// <summary>
	/// Gets a collection of the <see cref="Type.AssemblyQualifiedName">assembly-qualified names</see> for additional service interfaces
	/// that should be exposed to the service broker client.
	/// </summary>
	public ImmutableArray<string> AdditionalServiceInterfaceTypeNames { get; init; } = ImmutableArray<string>.Empty;

	/// <summary>
	/// Gets the string to use in a <see cref="DebuggerDisplayAttribute"/>.
	/// </summary>
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	protected string DebuggerDisplay => $"{this.Audience} ({this.ProfferingPackageId}) [{(this.AllowGuestClients ? "AllowGuestClients" : string.Empty)}]";

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{nameof(ServiceAudience)}: {this.Audience}, {nameof(this.AllowGuestClients)}: {this.AllowGuestClients}, {nameof(this.ProfferingPackageId)}: {this.ProfferingPackageId}";
	}

	/// <summary>
	/// Gets a value indicating whether this service is approved for consuming by a given audience.
	/// </summary>
	/// <param name="consumingAudience">The candidate audience that would like to get this service.</param>
	/// <returns>A value indicating whether the service permits access by the given audience.</returns>
	internal bool IsExposedTo(ServiceAudience consumingAudience) => (consumingAudience & this.Audience) == consumingAudience;

	/// <summary>
	/// Gets the resolved types for the <see cref="AdditionalServiceInterfaceTypeNames"/>.
	/// </summary>
	/// <param name="logger">The logger to use for tracing out any errors when loading the additional types.</param>
	/// <returns>The array of types.</returns>
	internal ImmutableArray<Type> GetAdditionalServiceInterfaceTypes(TraceSource logger)
	{
		if (this.additionalServiceInterfacesResolvedTypes.IsDefault)
		{
			ImmutableArray<Type>.Builder builder = ImmutableArray.CreateBuilder<Type>(this.AdditionalServiceInterfaceTypeNames.Length);
			foreach (string typeName in this.AdditionalServiceInterfaceTypeNames)
			{
				try
				{
					if (Type.GetType(typeName, throwOnError: true) is Type type)
					{
						builder.Add(type);
					}
					else
					{
						logger.TraceEvent(TraceEventType.Error, (int)TraceEvents.AdditionalProxyInterfaceTypeLoadFailure, "Could not resolve additional service interface type '{0}'.", typeName);
					}
				}
				catch (Exception ex)
				{
					logger.TraceEvent(TraceEventType.Error, (int)TraceEvents.AdditionalProxyInterfaceTypeLoadFailure, "Could not resolve additional service interface type '{0}': {1}", typeName, ex);
				}
			}

			this.additionalServiceInterfacesResolvedTypes = builder.Count == builder.Capacity ? builder.MoveToImmutable() : builder.ToImmutable();
		}

		return this.additionalServiceInterfacesResolvedTypes;
	}

	/// <summary>
	/// Triggers the call to <see cref="IBrokeredServiceContainer.Proffer(ServiceRpcDescriptor, BrokeredServiceFactory)"/>
	/// the service represented by this registration if the service has not yet been proffered.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that tracks the async operation.</returns>
	protected internal virtual Task LoadProfferingPackageAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
