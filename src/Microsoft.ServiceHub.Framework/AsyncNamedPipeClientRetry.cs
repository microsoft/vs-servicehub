namespace Microsoft.ServiceHub.Framework;
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Defines the algorithm and parameters for retrying to connect to the pipe server.
/// </summary>
public class AsyncNamedPipeClientRetry
{
	/// <summary>
	/// Gets or sets the max duration that connection should be attempted.
	/// </summary>
	public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(12);

	/// <summary>
	/// Gets or sets the max number of retries.
	/// </summary>
	public int MaxRetries { get; set; } = 1000;

	/// <summary>
	/// Gets or sets a function that takes as input the number of retries and returns the delay until the next retry should be attempted.
	/// </summary>
	public Func<int, int> DelayBetweenRetriesInMs { get; set; } = (retryNumber) =>
	{
		return Math.Min(retryNumber * 100, 5 * 1000);
	};
}
