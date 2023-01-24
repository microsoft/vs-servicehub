// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Newtonsoft.Json;

namespace Microsoft.ServiceHub.Framework.Shared;

/// <summary>
/// Utility methods for Microsoft.ServiceHub.Framework.
/// </summary>
internal static class SharedUtilities
{
	/// <summary>
	/// Adds the key and value to the ActivationArguments of a <see cref="ServiceActivationOptions"/> provided the key does not already exist within the ActivationArguments.
	/// </summary>
	/// <param name="key">The key of the argument being added to the dictionary.</param>
	/// <param name="value">The value of the argument being added to the dictionary.</param>
	/// <param name="options">The <see cref="ServiceActivationOptions"/> object that the key and value are being added to.</param>
	/// <returns>The updated <see cref="ServiceActivationOptions"/>.</returns>
	internal static ServiceActivationOptions AddEntryToActivationArguments(string key, string value, ServiceActivationOptions options)
	{
		if (options.ActivationArguments == null || !options.ActivationArguments.ContainsKey(key))
		{
			var activationArguments = new Dictionary<string, string>();

			if (options.ActivationArguments != null)
			{
				foreach (string item in options.ActivationArguments.Keys)
				{
					if (options.ActivationArguments.TryGetValue(item, out string? itemValue))
					{
						activationArguments.Add(item, itemValue);
					}
				}
			}

			activationArguments.Add(key, value);

			options.ActivationArguments = activationArguments;
		}

		return options;
	}
}
