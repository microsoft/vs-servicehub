// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An identifier for an activatable service.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
[TypeConverter(typeof(ServiceMonikerTypeConverter))]
[JsonObject]
[DataContract]
public class ServiceMoniker : IEquatable<ServiceMoniker>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceMoniker"/> class.
	/// </summary>
	/// <param name="name">The name of the service.</param>
	public ServiceMoniker(string name)
	{
		Requires.NotNullOrEmpty(name, nameof(name));

		this.Name = name;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceMoniker"/> class.
	/// </summary>
	/// <param name="name">The name of the service.</param>
	/// <param name="version">The version of the service or expected by the client. May be null.</param>
	[JsonConstructor]
	public ServiceMoniker(string name, Version? version)
		: this(name)
	{
		this.Version = version;
	}

	/// <summary>
	/// Gets the name of the service.
	/// </summary>
	[DataMember]
	public string Name { get; }

	/// <summary>
	/// Gets the version of the service or the version expected by the client.
	/// </summary>
	[JsonConverter(typeof(VersionConverter))]
	[DataMember]
	public Version? Version { get; }

	/// <summary>
	/// Gets a string for the debugger to display for this struct.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private string DebuggerDisplay => this.ToString();

	/// <summary>
	/// Equality comparison operator.
	/// </summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns><see langword="true"/> if the left and right operand are equivalent.</returns>
	public static bool operator ==(ServiceMoniker? left, ServiceMoniker? right)
	{
		return left is null ? right is null : left.Equals(right);
	}

	/// <summary>
	/// Inequality comparison operator.
	/// </summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns><see langword="true"/> if the left and right operand are different.</returns>
	public static bool operator !=(ServiceMoniker? left, ServiceMoniker? right)
	{
		return !(left == right);
	}

	/// <inheritdoc />
	public bool Equals(ServiceMoniker? other)
	{
		return !(other is null)
			&& this.Name == other.Name
			&& EqualityComparer<Version?>.Default.Equals(this.Version, other.Version);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj) => this.Equals(obj as ServiceMoniker);

	/// <inheritdoc />
	public override int GetHashCode() => this.Name.GetHashCode() + (this.Version?.GetHashCode() ?? 0);

	/// <inheritdoc />
	public override string ToString() => this.Name + (this.Version != null ? $" ({this.Version})" : string.Empty);

	/// <summary>
	/// This converter allows Newtonsoft.Json to use <see cref="ServiceMoniker"/> as a key in a dictionary.
	/// </summary>
	private class ServiceMonikerTypeConverter : TypeConverter
	{
		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) => sourceType == typeof(string);

		public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) => destinationType == typeof(string);

		public override object? ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object? value)
		{
			if (value is null)
			{
				return null;
			}

			string nameAndVersion = (string)value;
			int slashIndex = nameAndVersion.IndexOf('/');
			string name = slashIndex >= 0 ? nameAndVersion.Substring(0, slashIndex) : nameAndVersion;
			Version? version = slashIndex >= 0 ? Version.Parse(nameAndVersion.Substring(slashIndex + 1)) : null;
			return version is null ? new ServiceMoniker(name) : new ServiceMoniker(name, version);
		}

		public override object? ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object? value, Type destinationType)
		{
			Requires.NotNull(destinationType, nameof(destinationType));
			if (destinationType != typeof(string))
			{
				throw new NotSupportedException();
			}

			if (value is null)
			{
				return null;
			}

			var moniker = (ServiceMoniker)value;
			return moniker.Version is null ? moniker.Name : $"{moniker.Name}/{moniker.Version}";
		}
	}
}
