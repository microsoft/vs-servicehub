// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Converts between <see cref="CultureInfo"/> and a string representation which is its <see cref="CultureInfo.Name"/>.
/// </summary>
internal class CultureInfoSystemTextJsonConverter : JsonConverter<CultureInfo>
{
	/// <inheritdoc/>
	public override CultureInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new CultureInfo(reader.GetString()!);

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CultureInfo value, JsonSerializerOptions options) => writer.WriteStringValue(value.Name);
}
