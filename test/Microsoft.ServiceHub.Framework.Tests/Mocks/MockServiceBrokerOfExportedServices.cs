// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Composition;
using Microsoft;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

[Export]
internal class MockServiceBrokerOfExportedServices : ServiceBrokerOfExportedServices
{
	internal GlobalBrokeredServiceContainer? Container { get; set; }

	protected override Task<GlobalBrokeredServiceContainer> GetBrokeredServiceContainerAsync(CancellationToken cancellationToken)
	{
		Verify.Operation(this.Container is object, "The container hasn't been set in the test yet.");
		return Task.FromResult(this.Container);
	}
}
