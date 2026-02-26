// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public partial class GlobalBrokeredServiceContainer
{
	private ChaosMonkey? chaosMonkeyConfiguration;

	private enum ChaosBrokeredServiceAvailability
	{
		/// <summary>
		/// No services will be artificially denied.
		/// </summary>
		AllowAll,

		/// <summary>
		/// All service requests will be denied.
		/// </summary>
		DenyAll,

		/// <summary>
		/// All requests will be denied if they would be fulfilled by a remote connection.
		/// </summary>
		DenyRemote,

		/// <summary>
		/// All requests will be denied if they originate from a remote consumer.
		/// </summary>
		DenyFromRemote,
	}

	/// <summary>
	/// Loads and applies the content of a chaos monkey configuration.
	/// </summary>
	/// <param name="chaosMonkeyConfigurationPath">The path to a chaos monkey configuration file.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that represents the async operation.</returns>
	[Obsolete("This API is reserved for Visual Studio internal use and may change or be removed in a future version.")]
	protected async Task ApplyChaosMonkeyConfigurationAsync(string chaosMonkeyConfigurationPath, CancellationToken cancellationToken)
	{
		await TaskScheduler.Default;
		using var configReader = new JsonTextReader(new StreamReader(new FileStream(chaosMonkeyConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true)));
		JObject configurationJson = await JObject.LoadAsync(configReader, cancellationToken).ConfigureAwait(false);
		var serializer = JsonSerializer.Create(new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
		});
		this.chaosMonkeyConfiguration = configurationJson.ToObject<ChaosMonkey>(serializer) ?? throw new InvalidOperationException("Configuration deserialized to a null value.");

		this.traceSource.TraceEvent(System.Diagnostics.TraceEventType.Information, 0, $"Chaos monkey configuration loaded from \"{Path.GetFullPath(chaosMonkeyConfigurationPath)}\".");

		// Take a snapshot of the registeredServices in case it changes while iterating through the chaosMonkeyConfiguration services
		System.Collections.Immutable.ImmutableDictionary<ServiceMoniker, ServiceRegistration> services = this.registeredServices;

		// Warn of any configurations that don't match with known brokered services.
		IEnumerable<ServiceMoniker>? invalidEntries = this.chaosMonkeyConfiguration.BrokeredServices?.Keys.Where(k => !services.ContainsKey(k));
		if (invalidEntries?.Any() ?? false)
		{
			this.traceSource.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, $"Chaos monkey configuration found for non-registered brokered services: {string.Join(", ", invalidEntries)}");
		}
	}

	private class ServiceMonikerKeyDictionaryConverter<T> : JsonConverter<Dictionary<ServiceMoniker, T>?>
	{
		public override Dictionary<ServiceMoniker, T>? ReadJson(JsonReader reader, Type objectType, Dictionary<ServiceMoniker, T>? existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			var map = new Dictionary<ServiceMoniker, T>();

			while (true)
			{
				if (!reader.Read())
				{
					throw new EndOfStreamException();
				}

				if (reader.TokenType == JsonToken.EndObject)
				{
					break;
				}

				string? nameAndVersion = (string?)reader.Value;
				Assumes.NotNull(nameAndVersion);
				int slashIndex = nameAndVersion.IndexOf('/');
				string name = slashIndex >= 0 ? nameAndVersion.Substring(0, slashIndex) : nameAndVersion;
				Version? version = slashIndex >= 0 ? Version.Parse(nameAndVersion.Substring(slashIndex + 1)) : null;
				ServiceMoniker moniker = version is null ? new ServiceMoniker(name) : new ServiceMoniker(name, version);

				if (!reader.Read())
				{
					throw new EndOfStreamException();
				}

				T? value = serializer.Deserialize<T>(reader) ?? throw new InvalidOperationException("Value deserialized to null.");

				map.Add(moniker, value);
			}

			return map;
		}

		public override void WriteJson(JsonWriter writer, Dictionary<ServiceMoniker, T>? value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}

	/// <summary>
	/// The data object to deserialize from a chaos monkey configuration file.
	/// </summary>
	/// <remarks>
	/// See the ChaosMonkey.schema.json file for the full schema.
	/// Sample JSON:
	/// <code><![CDATA[
	/// {
	///   "brokeredServices": {
	///     "monikerName/1.2": {
	///       "availability": "localOnly"
	///     }
	///   }
	/// }
	/// ]]></code>
	/// </remarks>
	[DataContract]
	private class ChaosMonkey
	{
		[DataMember]
		[JsonConverter(typeof(ServiceMonikerKeyDictionaryConverter<ChaosBrokeredService>))]
		internal Dictionary<ServiceMoniker, ChaosBrokeredService>? BrokeredServices { get; set; }
	}

	[DataContract]
	private class ChaosBrokeredService
	{
		[DataMember]
		[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
		internal ChaosBrokeredServiceAvailability Availability { get; set; }
	}
}
