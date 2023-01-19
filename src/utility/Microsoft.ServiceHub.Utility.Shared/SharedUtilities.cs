// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Newtonsoft.Json;

namespace Microsoft.ServiceHub.Utility.Shared;

/// <summary>
/// Utility methods for Microsoft.ServiceHub.Framework.
/// </summary>
internal static class SharedUtilities
{
	/// <summary>
	/// Deserializes a string representing a serialized <see cref="ServiceActivationOptions"/> object.
	/// </summary>
	/// <param name="serializedServiceActivationOptions">Serialized <see cref="ServiceActivationOptions"/>.</param>
	/// <returns>The deserialized <see cref="ServiceActivationOptions"/>.</returns>
	/// <remarks>
	/// This method is invoked through reflection from Microsoft.ServiceHub.HostStub.ServiceManager.StartService.
	/// Having a method specifically for this avoids us having to load Newtonsoft.Json explicitly through reflection.
	/// </remarks>
	internal static ServiceActivationOptions DeserializeServiceActivationOptions(string serializedServiceActivationOptions)
	{
		return JsonConvert.DeserializeObject<ServiceActivationOptions>(serializedServiceActivationOptions);
	}

	/// <summary>
	/// Removes the <see cref="Constants.ServiceHubRemoteServiceBrokerPipeNameActivationArgument"/> from the ActivationArguments of
	/// a <see cref="ServiceActivationOptions"/>.
	/// </summary>
	/// <param name="options">The <see cref="ServiceActivationOptions"/> to remove the service broker pipe name from.</param>
	/// <returns>The updated <see cref="ServiceActivationOptions"/>.</returns>
	internal static ServiceActivationOptions RemoveServiceBrokerPipeNameFromServiceActivationOptions(ServiceActivationOptions options)
	{
		if (options.ActivationArguments != null && options.ActivationArguments.ContainsKey(Constants.ServiceHubRemoteServiceBrokerPipeNameActivationArgument))
		{
			var activationOptions = new Dictionary<string, string>();
			foreach (string option in options.ActivationArguments.Keys)
			{
				if (!option.Equals(Constants.ServiceHubRemoteServiceBrokerPipeNameActivationArgument, StringComparison.OrdinalIgnoreCase)
					&& options.ActivationArguments.TryGetValue(option, out string? value))
				{
					activationOptions.Add(option, value);
				}
			}

			options.ActivationArguments = activationOptions;
		}

		return options;
	}

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
