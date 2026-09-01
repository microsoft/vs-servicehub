// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// A view-intrinsic brokered service that can analyze why that particular <see cref="View"/> is incapable of producing some requested service.
	/// This service is accessible via <see cref="MissingServiceDiagnostics"/>.
	/// </summary>
	private class MissingServiceDiagnosticsService : IMissingServiceDiagnosticsService
	{
		private readonly View view;

		internal MissingServiceDiagnosticsService(View view)
		{
			this.view = view;
		}

		/// <inheritdoc />
		public async Task<MissingServiceAnalysis> AnalyzeMissingServiceAsync(ServiceMoniker missingServiceMoniker, CancellationToken cancellationToken)
		{
			(IProffered? profferingSource, MissingBrokeredServiceErrorCode errorCode) = await this.view.TryGetProfferingSourceAsync(missingServiceMoniker, isRemoteRequest: false, cancellationToken).ConfigureAwait(false);

			if (profferingSource is null)
			{
				return new MissingServiceAnalysis(errorCode, null);
			}

			// Try activating the service to see if the factory returns a non-null value.
			try
			{
				IDuplexPipe? pipe = await profferingSource.GetPipeAsync(missingServiceMoniker, cancellationToken).ConfigureAwait(false);
				if (pipe is null)
				{
					return new MissingServiceAnalysis(MissingBrokeredServiceErrorCode.ServiceFactoryReturnedNull, profferingSource.Source);
				}

				// Close the pipe, as we don't have a use for the service once we've acquired it.
				await pipe.Input.CompleteAsync().ConfigureAwait(false);
				await pipe.Output.CompleteAsync().ConfigureAwait(false);

				// Everything checks out. Transient problem perhaps?
				return new MissingServiceAnalysis(MissingBrokeredServiceErrorCode.NoExplanation, profferingSource.Source);
			}
			catch (ServiceCompositionException)
			{
				// We consider an error the same as a missing service and wait for a relevant AvailabilityChanged event to try again.
				return new MissingServiceAnalysis(MissingBrokeredServiceErrorCode.ServiceFactoryFault, profferingSource.Source);
			}
		}
	}
}
