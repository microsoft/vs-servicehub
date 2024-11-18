// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using Newtonsoft.Json;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Converts between <see cref="CultureInfo"/> and a string representation which is its <see cref="CultureInfo.Name"/>.
/// </summary>
internal class CultureInfoJsonConverter : JsonConverter
{
	/// <inheritdoc/>
	public override bool CanConvert(Type objectType) => objectType == typeof(CultureInfo);

	/// <inheritdoc/>
	public override object? ReadJson(JsonReader reader, Type? objectType, object? existingValue, JsonSerializer serializer)
	{
		switch (reader.TokenType)
		{
			case JsonToken.Null: return null;
			case JsonToken.String:
				{
					try
					{
						return new CultureInfo((string)reader.Value!);
					}
					catch (CultureNotFoundException)
					{
						// The DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 environment variable causes this.
						return null;
					}
				}

			default: throw new JsonSerializationException($"Error parsing {nameof(CultureInfo)}. Unexpected token type: {reader.TokenType}.");
		}
	}

	/// <inheritdoc/>
	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		switch (value)
		{
			case null:
				writer.WriteNull();
				break;
			case CultureInfo culture:
				writer.WriteValue(culture.Name);
				break;
			default:
				throw new JsonSerializationException($"Error serializing {nameof(CultureInfo)} from unexpected type: {value.GetType().FullName}.");
		}
	}
}
