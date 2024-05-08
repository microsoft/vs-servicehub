// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Verify = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.ISB002AvoidStoringRentalsInFieldsAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

public class ISB002AvoidStoringRentalsInFieldsAnalyzerTests
{
	private const string Preamble = @"
using System;
using System.Threading.Tasks;
using Microsoft.ServiceHub.Framework;
interface IFoo { }
static class Stock {
    internal static ServiceJsonRpcDescriptor Descriptor => throw new NotImplementedException();
}
";

	[Fact]
	public async Task Rental_AcceptableUse()
	{
		string test = Preamble + @"
class Test {
    ServiceBrokerClient sbClient;

    async Task Foo() {
        using (var rental = await sbClient.GetProxyAsync<IFoo>(Stock.Descriptor))
        {
            if (rental.Proxy is object) {
            }
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Rental_InField()
	{
		string test = Preamble + @"
class Test {
    ServiceBrokerClient.Rental<IFoo> [|rental|];
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task NullableRental_InField()
	{
		string test = Preamble + @"
class Test {
    ServiceBrokerClient.Rental<IFoo>? [|rental|];
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Rental_InProperty()
	{
		string test = Preamble + @"
class Test {
    [|ServiceBrokerClient.Rental<IFoo> Rental { get; set; }|]
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}
}
