// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.ServiceHub.Framework;
using Xunit;

public class ServiceActivationOptionsTests
{
	[Fact]
	public void SetClientDefaults()
	{
		var options = default(ServiceActivationOptions);
		options.SetClientDefaults();
		Assert.Same(CultureInfo.CurrentCulture, options.ClientCulture);
		Assert.Same(CultureInfo.CurrentUICulture, options.ClientUICulture);
	}

	[Fact]
	public void SetClientDefaults_ClientCulturePreset()
	{
		var options = default(ServiceActivationOptions);
		CultureInfo expected = new CultureInfo("es");
		options.ClientCulture = expected;
		options.SetClientDefaults();
		Assert.Same(expected, options.ClientCulture);
		Assert.Same(CultureInfo.CurrentUICulture, options.ClientUICulture);
	}

	[Fact]
	public void SetClientDefaults_ClientUICulturePreset()
	{
		var options = default(ServiceActivationOptions);
		CultureInfo expected = new CultureInfo("es");
		options.ClientUICulture = expected;
		options.SetClientDefaults();
		Assert.Same(expected, options.ClientUICulture);
		Assert.Same(CultureInfo.CurrentCulture, options.ClientCulture);
	}

	[Fact]
	public void Equality()
	{
		var emptyDict = ImmutableDictionary.Create<string, string>();
		ImmutableDictionary<string, string> dict1a = ImmutableDictionary.Create<string, string>().Add("key1", "value1");
		ImmutableDictionary<string, string> dict1b = ImmutableDictionary.Create<string, string>().Add("key1", "value1");
		ImmutableDictionary<string, string> dict2a = ImmutableDictionary.Create<string, string>().Add("key2", "value2");
		ImmutableDictionary<string, string> dict2b = ImmutableDictionary.Create<string, string>().Add("key2", "value2");

		var options1 = default(ServiceActivationOptions);
		var options2 = default(ServiceActivationOptions);
		Assert.Equal(options1, options2);

		// Empty dictionaries should NOT be considered equal to null dictionaries
		// (since an empty one might indicate overriding a default with nothing).
		options1.ActivationArguments = emptyDict;
		Assert.NotEqual(options1, options2);

		options2.ActivationArguments = dict1a;
		Assert.NotEqual(options1, options2);

		options1.ActivationArguments = dict1b;
		Assert.Equal(options1, options2);

		options1.ClientCredentials = dict1a;
		Assert.NotEqual(options1, options2);

		options2.ClientCredentials = dict1b;
		Assert.Equal(options1, options2);

		options1.ClientCulture = CultureInfo.CurrentCulture;
		Assert.NotEqual(options1, options2);

		options2.ClientCulture = new CultureInfo(CultureInfo.CurrentCulture.Name);
		Assert.Equal(options1, options2);

		options1.ClientUICulture = CultureInfo.CurrentUICulture;
		Assert.NotEqual(options1, options2);

		options2.ClientUICulture = new CultureInfo(CultureInfo.CurrentUICulture.Name);
		Assert.Equal(options1, options2);
	}
}
