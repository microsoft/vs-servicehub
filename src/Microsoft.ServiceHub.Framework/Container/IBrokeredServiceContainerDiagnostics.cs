// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Allows for retrieval or export of manifest, registration and runtime data that can be useful in diagnosing a service acquisition failure.
/// </summary>
public interface IBrokeredServiceContainerDiagnostics
{
	/// <summary>
	/// Writes a bunch of diagnostic data to a JSON file.
	/// </summary>
	/// <param name="filePath">The path to the JSON file to be written. If it already exists it will be overwritten.</param>
	/// <param name="serviceAudience">The audience to consider is querying for services.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes when the writing is done.</returns>
	Task ExportDiagnosticsAsync(string filePath, ServiceAudience serviceAudience, CancellationToken cancellationToken = default);
}
