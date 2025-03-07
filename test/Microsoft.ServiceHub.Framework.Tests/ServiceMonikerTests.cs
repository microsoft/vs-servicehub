// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Reflection;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Newtonsoft.Json.Linq;

public class ServiceMonikerTests
{
	private readonly ITestOutputHelper logger;

	public ServiceMonikerTests(ITestOutputHelper logger)
	{
		this.logger = logger;
	}

	[Fact]
	public void Ctor_ValidatesInputs()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceMoniker(null!));
		Assert.Throws<ArgumentException>(() => new ServiceMoniker(string.Empty));
	}

	[Fact]
	public void Ctor_NameOnly()
	{
		var moniker = new ServiceMoniker("SomeName");
		Assert.Equal("SomeName", moniker.Name);
		Assert.Null(moniker.Version);
	}

	[Fact]
	public void Ctor_AcceptsNullVersion()
	{
		var moniker = new ServiceMoniker("SomeName", version: null);
		Assert.Equal("SomeName", moniker.Name);
		Assert.Null(moniker.Version);
	}

	[Fact]
	public void Ctor_WithVersion()
	{
		Version version = new Version(1, 2);
		var moniker = new ServiceMoniker("SomeName", version: version);
		Assert.Equal("SomeName", moniker.Name);
		Assert.Same(version, moniker.Version);
	}

	[Fact]
	public void ToString_IsName()
	{
		var moniker = new ServiceMoniker("SomeName");
		Assert.Equal(moniker.Name, moniker.ToString());
	}

	[Fact]
	public void ToString_IsNameAndVersion()
	{
		var moniker = new ServiceMoniker("SomeName", new Version(1, 2));
		Assert.Equal("SomeName (1.2)", moniker.ToString());
	}

	[Fact]
	public void Equality_Name()
	{
		var moniker1a = new ServiceMoniker("A");
		var moniker1b = new ServiceMoniker("A");
		var moniker2a = new ServiceMoniker("a");
		var moniker3a = new ServiceMoniker("B");

		Assert.Equal(moniker1a, moniker1b);
		Assert.NotEqual(moniker2a, moniker1a);
		Assert.NotEqual(moniker3a, moniker1a);
	}

	[Fact]
	public void Equality_Version()
	{
		var moniker1a = new ServiceMoniker("A", new Version(1, 0));
		var moniker1b = new ServiceMoniker("A", new Version(1, 0));
		var moniker2 = new ServiceMoniker("A", new Version(1, 1));

		Assert.Equal(moniker1a, moniker1b);
		Assert.NotEqual(moniker1a, moniker2);
	}

	[Fact]
	public void Equals_Null()
	{
		Assert.False(new ServiceMoniker("A").Equals((object?)null));
		Assert.False(new ServiceMoniker("A").Equals((ServiceMoniker?)null));
		Assert.False(new ServiceMoniker("A").Equals("hi"));
	}

	[Fact]
	public void EqualityOperator_Name()
	{
		var moniker1a = new ServiceMoniker("A");
		var moniker1b = new ServiceMoniker("A");
		var moniker2a = new ServiceMoniker("a");
		var moniker3a = new ServiceMoniker("B");

		Assert.True(moniker1a == moniker1b);
		Assert.False(moniker2a == moniker1a);
		Assert.False(moniker3a == moniker1a);

		Assert.False(moniker1a != moniker1b);
		Assert.True(moniker2a != moniker1a);
		Assert.True(moniker3a != moniker1a);
	}

	[Fact]
	public void EqualityOperator_Version()
	{
		var moniker1a = new ServiceMoniker("A", new Version(1, 0));
		var moniker1b = new ServiceMoniker("A", new Version(1, 0));
		var moniker2 = new ServiceMoniker("A", new Version(1, 1));

		Assert.True(moniker1a == moniker1b);
		Assert.False(moniker1a == moniker2);

		Assert.False(moniker1a != moniker1b);
		Assert.True(moniker1a != moniker2);
	}

	[Fact]
	public void EqualityOperator_Null()
	{
		Assert.False(new ServiceMoniker("A") == null);
		Assert.True(new ServiceMoniker("A") != null);

#pragma warning disable SA1131 // Use readable conditions
		Assert.False(null == new ServiceMoniker("A"));
		Assert.True(null != new ServiceMoniker("A"));
#pragma warning restore SA1131 // Use readable conditions
	}

	[Fact]
	public void GetHashCode_Unique()
	{
		ServiceMoniker[] monikers = new[]
		{
			new ServiceMoniker("A"),
			new ServiceMoniker("a"),
			new ServiceMoniker("B"),
		};

		var hashCodes = new HashSet<int>(monikers.Select(m => m.GetHashCode()));
		Assert.Equal(monikers.Length, hashCodes.Count);
	}

	[Fact]
	public void GetHashCode_SameForEqualInstances()
	{
		var moniker1a = new ServiceMoniker("A");
		var moniker1b = new ServiceMoniker("A");
		Assert.Equal(moniker1a.GetHashCode(), moniker1b.GetHashCode());
	}

	[Fact]
	public void GetHashCode_NotEqualForVersionVariance()
	{
		var moniker1a = new ServiceMoniker("A", new Version(1, 1));
		var moniker1b = new ServiceMoniker("A", new Version(1, 0));
		Assert.NotEqual(moniker1a.GetHashCode(), moniker1b.GetHashCode());
	}

	[Fact]
	public void JsonSerialization()
	{
		var moniker = new ServiceMoniker("Abc", new Version(1, 0));
		var json = JToken.FromObject(moniker);
		this.logger.WriteLine(json.ToString());
		Assert.Equal(JTokenType.Object, json.Type);
		Assert.Equal("Abc", json.Value<string>("Name"));
		Assert.Equal("1.0", json.Value<string>("Version"));

		ServiceMoniker moniker2 = json.ToObject<ServiceMoniker>()!;
		Assert.Equal(moniker.Name, moniker2.Name);
		Assert.Equal(moniker.Version, moniker2.Version);
	}

	[Fact]
	public void MessagePackSerialization()
	{
		var moniker = new ServiceMoniker("Abc", new Version(1, 0));
		byte[] bytes = MessagePack.MessagePackSerializer.Serialize(moniker, cancellationToken: TestContext.Current.CancellationToken);
		ServiceMoniker moniker2 = MessagePack.MessagePackSerializer.Deserialize<ServiceMoniker>(bytes, cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal(moniker, moniker2);
	}

	[Fact]
	public void MessagePackSerialization_AsDictionaryKey()
	{
		var moniker = new ServiceMoniker("Abc", new Version(1, 0));
		var dict = new Dictionary<ServiceMoniker, bool>
		{
			{ moniker, true },
		};
		byte[] bytes = MessagePack.MessagePackSerializer.Serialize(dict, cancellationToken: TestContext.Current.CancellationToken);
		Dictionary<ServiceMoniker, bool> dict2 = MessagePack.MessagePackSerializer.Deserialize<Dictionary<ServiceMoniker, bool>>(bytes, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(dict2.ContainsKey(moniker));
		Assert.True(dict2.TryGetValue(moniker, out bool value));
		Assert.True(value);
	}

	[Fact]
	public void TypeConverter_CanConvertTo()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		Assert.True(typeConverter.CanConvertTo(typeof(string)));
		Assert.False(typeConverter.CanConvertTo(typeof(int)));
	}

	[Fact]
	public void TypeConverter_CanConvertFrom()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		Assert.True(typeConverter.CanConvertFrom(typeof(string)));
		Assert.False(typeConverter.CanConvertFrom(typeof(int)));
	}

	[Fact]
	public void TypeConverter_ConvertFrom_NoVersion()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		ServiceMoniker moniker = Assert.IsType<ServiceMoniker>(typeConverter.ConvertFrom("Abc"));
		Assert.Equal("Abc", moniker.Name);
		Assert.Null(moniker.Version);
	}

	[Fact]
	public void TypeConverter_ConvertFrom_WithVersion()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		ServiceMoniker moniker = Assert.IsType<ServiceMoniker>(typeConverter.ConvertFrom("Abc/1.0"));
		Assert.Equal("Abc", moniker.Name);
		Assert.Equal("1.0", moniker.Version!.ToString());
	}

	[Fact]
	public void TypeConverter_ConvertFrom_Null()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		Assert.Null(typeConverter.ConvertFrom(null!));
	}

	[Fact]
	public void TypeConverter_ConvertTo_NullType()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		var moniker = new ServiceMoniker("Abc");
		Assert.Throws<ArgumentNullException>(() => typeConverter.ConvertTo(moniker, null!));
	}

	[Fact]
	public void TypeConverter_ConvertTo_UnsupportedType()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		var moniker = new ServiceMoniker("Abc");
		Assert.Throws<NotSupportedException>(() => typeConverter.ConvertTo(moniker, typeof(int)));
	}

	[Fact]
	public void TypeConverter_ConvertTo_NoVersion()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		var moniker = new ServiceMoniker("Abc");
		string monikerString = Assert.IsType<string>(typeConverter.ConvertTo(moniker, typeof(string)));
		Assert.Equal("Abc", monikerString);
	}

	[Fact]
	public void TypeConverter_ConvertTo_WithVersion()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		var moniker = new ServiceMoniker("Abc", new Version(1, 0));
		string monikerString = Assert.IsType<string>(typeConverter.ConvertTo(moniker, typeof(string)));
		Assert.Equal("Abc/1.0", monikerString);
	}

	[Fact]
	public void TypeConverter_ConvertTo_Null()
	{
		TypeConverter typeConverter = this.GetTypeConverter();
		Assert.Null(typeConverter.ConvertTo(null, typeof(string)));
	}

	private TypeConverter GetTypeConverter()
	{
		TypeConverterAttribute? typeConverterAttribute = typeof(ServiceMoniker).GetCustomAttribute<TypeConverterAttribute>();
		Assumes.NotNull(typeConverterAttribute);
		var typeConverterType = Type.GetType(typeConverterAttribute.ConverterTypeName, throwOnError: true);
		Assumes.NotNull(typeConverterType);
		var typeConverterInstance = (TypeConverter?)Activator.CreateInstance(typeConverterType);
		Assumes.NotNull(typeConverterInstance);
		return typeConverterInstance;
	}
}
