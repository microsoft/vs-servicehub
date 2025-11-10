// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

public class MockMissingServiceDiagnosticsService : IMissingServiceDiagnosticsService
{
	internal ServiceMoniker? LastReceivedMoniker { get; set; }

	public Task<MissingServiceAnalysis> AnalyzeMissingServiceAsync(ServiceMoniker missingServiceMoniker, CancellationToken cancellationToken)
	{
		this.LastReceivedMoniker = missingServiceMoniker;
		return Task.FromResult(new MissingServiceAnalysis(MissingBrokeredServiceErrorCode.NotLocallyRegistered, ServiceSource.TrustedServer));
	}
}
