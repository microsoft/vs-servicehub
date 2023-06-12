// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

/// <summary>
/// Contains the result of a missing service analysis as returned from <see cref="IMissingServiceDiagnosticsService.AnalyzeMissingServiceAsync(ServiceMoniker, CancellationToken)"/>.
/// </summary>
public class MissingServiceAnalysis
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MissingServiceAnalysis"/> class.
	/// </summary>
	/// <param name="errorCode">The error code explaining why the service could not be obtained.</param>
	/// <param name="expectedSource">The source that the service was expected to come from.</param>
	internal MissingServiceAnalysis(MissingBrokeredServiceErrorCode errorCode, ServiceSource? expectedSource)
	{
		this.ErrorCode = errorCode;
		this.ExpectedSource = expectedSource;
	}

	/// <summary>
	/// Gets the error code explaining why the service could not be obtained.
	/// </summary>
	public MissingBrokeredServiceErrorCode ErrorCode { get; }

	/// <summary>
	/// Gets the source that the service was expected to come from.
	/// </summary>
	public ServiceSource? ExpectedSource { get; }
}
