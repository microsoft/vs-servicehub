// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Verify = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.ISB003ExportedBrokeredServicesAreValidAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

public class ISB003ExportedBrokeredServicesAreValidAnalyzerTests
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
			[ExportBrokeredService("MyService", null)]
			class MyService : IExportedBrokeredService
			{
				public ServiceRpcDescriptor Descriptor => throw new NotImplementedException();
				public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
			}
			""";
		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingInterface()
	{
		string test = Preamble + """
			[ExportBrokeredService("MyService", null)]
			class [|MyService|]
			{
			}
			""";
		await Verify.VerifyAnalyzerAsync(test);
	}
}
