// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework.Extensions
{
	/// <summary>
	/// Contains utility methods to be used by ServiceHub.
	/// </summary>
	internal static class ServiceHubUtilities
	{
		/// <summary>
		/// Gets the pipe name that the <see cref="IRemoteServiceBroker"/> is available over from the <see cref="ServiceActivationOptions"/>.
		/// </summary>
		/// <param name="options">The <see cref="ServiceActivationOptions"/> to get the pipe name from.</param>
		/// <returns>The pipe name or an empty string if there isn't one.</returns>
		internal static string GetServiceBrokerServerPipeName(this ServiceActivationOptions options)
		{
			if (options.ActivationArguments != null &&
				options.ActivationArguments.TryGetValue(Constants.ServiceHubRemoteServiceBrokerPipeNameActivationArgument, out string? pipeName))
			{
				return pipeName;
			}

			return string.Empty;
		}
	}
}
