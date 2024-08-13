// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.CSharpISB004OptionalInterfacesMustBeImplementedAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;
using VerifyVB = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.VisualBasicISB004OptionalInterfacesMustBeImplementedAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

public class ISB004OptionalInterfacesMustBeImplementedAnalyzerTests
{
	private const string Preamble = """
		using System;
		using System.Threading;
		using System.Threading.Tasks;
		using Microsoft.ServiceHub.Framework;
		using Microsoft.VisualStudio.Shell.ServiceBroker;
		
		""";

	[Fact]
	public async Task Valid()
	{
		string test = Preamble + """
			[ExportBrokeredService("MyService", "0.1")]
			[ExportBrokeredService("MyService", "1.0", typeof(IOptional))]
			class MyService : IExportedBrokeredService, IOptional
			{
				public ServiceRpcDescriptor Descriptor => throw new NotImplementedException();
				public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
			}

			interface IOptional { }
			""";
		await VerifyCS.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingInterface()
	{
		string test = Preamble + """
			[ExportBrokeredService("MyService", null, [|typeof(IOptional)|])]
			class MyService : IExportedBrokeredService
			{
				public ServiceRpcDescriptor Descriptor => throw new NotImplementedException();
				public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
			}
			
			interface IOptional { }
			""";
		await VerifyCS.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingInterface_MultipleAttributes()
	{
		string test = Preamble + """
			[ExportBrokeredService("MyService", null, typeof(IOptional), [|typeof(IOptional2)|])]
			[ExportBrokeredService("MyService", null, [|typeof(IOptional3)|])]
			class MyService : IExportedBrokeredService, IOptional
			{
				public ServiceRpcDescriptor Descriptor => throw new NotImplementedException();
				public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
			}
			
			interface IOptional { }
			interface IOptional2 { }
			interface IOptional3 { }
			""";
		await VerifyCS.VerifyAnalyzerAsync(test);
	}
}
