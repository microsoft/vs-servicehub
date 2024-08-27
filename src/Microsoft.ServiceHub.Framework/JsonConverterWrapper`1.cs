// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Wraps a built-in converter with a custom one to workaround <see href="https://github.com/dotnet/runtime/issues/85981">this bug in System.Text.Json</see>.
/// </summary>
/// <typeparam name="T">The type the converter supports.</typeparam>
/// <remarks>
/// We should be able to remove this class and its users once we upgrade to System.Text.Json 8.0.0.
/// </remarks>
internal sealed class JsonConverterWrapper<T> : JsonConverter<T>
{
	/// <summary>
	/// The singleton instance of the converter.
	/// </summary>
	internal static readonly JsonConverter<T> Instance = new JsonConverterWrapper<T>();

	private static readonly JsonConverter<T> BuiltInConverter = (JsonConverter<T>)JsonSerializerOptions.Default.GetTypeInfo(typeof(T)).Converter;

	private JsonConverterWrapper()
	{
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		=> BuiltInConverter.Write(writer, value, options);

	/// <inheritdoc/>
	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> BuiltInConverter.Read(ref reader, typeToConvert, options);

	/// <inheritdoc/>
	public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> BuiltInConverter.ReadAsPropertyName(ref reader, typeToConvert, options);

	/// <inheritdoc/>
	public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] T value, JsonSerializerOptions options)
		=> BuiltInConverter.WriteAsPropertyName(writer, value, options);
}
