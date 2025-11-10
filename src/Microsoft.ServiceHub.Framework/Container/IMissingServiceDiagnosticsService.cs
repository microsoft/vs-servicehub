// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using PolyType;
using StreamJsonRpc;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

/// <summary>
/// Provides diagnostics to understand why brokered services are not activatable.
/// </summary>
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
[JsonRpcContract]
public partial interface IMissingServiceDiagnosticsService
{
	/// <summary>
	/// Analyzes possible explanations for why a brokered service could not be acquired.
	/// </summary>
	/// <param name="missingServiceMoniker">The moniker of the missing brokered service.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An analysis describing the first problem encountered while looking for the brokered service.</returns>
	Task<MissingServiceAnalysis> AnalyzeMissingServiceAsync(ServiceMoniker missingServiceMoniker, CancellationToken cancellationToken);
}
