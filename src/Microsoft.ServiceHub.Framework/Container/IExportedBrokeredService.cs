// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using StreamJsonRpc;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// An interface that must be implemented by a brokered service that is exported to MEF
/// via the <see cref="ExportBrokeredServiceAttribute"/>.
/// </summary>
public interface IExportedBrokeredService
{
	/// <summary>
	/// Gets the <see cref="ServiceRpcDescriptor"/> to be used when activating the service.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When a brokered service supports multiple versions in their <see cref="ServiceMoniker"/>,
	/// it may be important to consider the version being activated to know which <see cref="ServiceRpcDescriptor"/> to return
	/// from this property.
	/// This <see cref="ServiceMoniker"/> may be imported via MEF in the same MEF part that implements this interface
	/// in order to check the value before returning from this property getter.
	/// </para>
	/// <para>
	/// This property may return <see langword="null"/> when the brokered service does not support the version of the service requested by the client.
	/// This will result in the client receiving <see langword="null" /> instead of a service instance.
	/// </para>
	/// </remarks>
	ServiceRpcDescriptor? Descriptor { get; }

	/// <summary>
	/// Initializes the brokered service before returning the new instance to its client.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes with initialization.</returns>
	/// <remarks>
	/// <para>This method offers the brokered service an <em>optional</em> opportunity to do async initialization,
	/// similar to what <see cref="BrokeredServiceFactory"/> would have allowed for when proffering a non-MEF
	/// brokered service with <see cref="IBrokeredServiceContainer.Proffer(ServiceRpcDescriptor, BrokeredServiceFactory)"/>.
	/// Empty methods may simply return <see cref="Task.CompletedTask"/>.</para>
	/// <para>Throwing from this method will lead to the client experiencing a <see cref="ServiceActivationFailedException"/>
	/// with the exception thrown from here preserved as an inner exception.</para>
	/// </remarks>
	[JsonRpcIgnore]
	Task InitializeAsync(CancellationToken cancellationToken);
}
