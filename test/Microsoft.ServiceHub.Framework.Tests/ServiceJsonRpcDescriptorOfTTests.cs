// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Xunit;

public class ServiceJsonRpcDescriptorOfTTests
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("Some name");

	[Fact]
	public void Ctor_ValidatesInputs()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceJsonRpcDescriptor<IDisposable>(null!, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader));
	}

	[Fact]
	public void Ctor_SetsProperties()
	{
		var descriptor = new ServiceJsonRpcDescriptor<IDisposable>(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
		Assert.Same(SomeMoniker, descriptor.Moniker);
		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.MessagePack, descriptor.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, descriptor.MessageDelimiter);
	}
}
