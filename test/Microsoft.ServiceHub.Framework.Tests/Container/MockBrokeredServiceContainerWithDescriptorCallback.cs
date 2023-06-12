// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Testing;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

internal class MockBrokeredServiceContainerWithDescriptorCallback : MockBrokeredServiceContainer
{
	/// <summary>
	/// Gets or sets the callback to use when <see cref="GlobalBrokeredServiceContainer.ApplyDescriptorSettings(ServiceRpcDescriptor, bool)"/> is called.
	/// </summary>
	public Func<ServiceRpcDescriptor, bool, ServiceRpcDescriptor>? ApplyDescriptorCallback { get; set; }

	/// <inheritdoc />
	protected override ServiceRpcDescriptor ApplyDescriptorSettings(ServiceRpcDescriptor descriptor, bool clientRole)
	{
		if (this.ApplyDescriptorCallback is not null)
		{
			descriptor = this.ApplyDescriptorCallback(descriptor, clientRole);
		}

		return base.ApplyDescriptorSettings(descriptor, clientRole);
	}
}
