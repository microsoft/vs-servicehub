// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Xunit;

public class ServiceCompositionExceptionTests
{
	[Fact]
	public void Ctor_Default()
	{
		var ex = new ServiceCompositionException();
		Assert.Null(ex.InnerException);
	}
}
