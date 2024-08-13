// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VerifyCS = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.CSharpISB004OptionalInterfacesMustBeImplementedAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;
using VerifyVB = VisualBasicCodeFixVerifier<Microsoft.ServiceHub.Analyzers.VisualBasicISB004OptionalInterfacesMustBeImplementedAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

public class ISB004OptionalInterfacesMustBeImplementedAnalyzerTests
{
	private const string CSPreamble = """
		using System;
		using System.Threading;
		using System.Threading.Tasks;
		using Microsoft.ServiceHub.Framework;
		using Microsoft.VisualStudio.Shell.ServiceBroker;
		
		""";

	private const string VBPreamble = """
		Imports System
		Imports System.Threading
		Imports System.Threading.Tasks
		Imports Microsoft.ServiceHub.Framework
		Imports Microsoft.VisualStudio.Shell.ServiceBroker
		
		""";

	[Fact]
	public async Task Valid_CS()
	{
		string test = CSPreamble + """
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
	public async Task Invalid_MissingInterface_CS()
	{
		string test = CSPreamble + """
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
	public async Task Invalid_MissingInterface_MultipleAttributes_CS()
	{
		string test = CSPreamble + """
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

	[Fact]
	public async Task Valid_VB()
	{
		string test = VBPreamble + """
			<ExportBrokeredService("MyService", "0.1")>
			<ExportBrokeredService("MyService", "1.0", GetType(IOptional))>
			Class MyService
				Implements IExportedBrokeredService, IOptional

				Public ReadOnly Property Descriptor As ServiceRpcDescriptor Implements IExportedBrokeredService.Descriptor
					Get
						Throw New NotImplementedException()
					End Get
				End Property

				Public Function InitializeAsync(cancellationToken As CancellationToken) As Task Implements IExportedBrokeredService.InitializeAsync
					Throw New NotImplementedException()
				End Function
			End Class

			Interface IOptional
			End Interface
			""";
		await VerifyVB.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingInterface_VB()
	{
		string test = VBPreamble + """
			<ExportBrokeredService("MyService", Nothing, [|GetType(IOptional)|])>
			Class MyService
				Implements IExportedBrokeredService

				Public ReadOnly Property Descriptor As ServiceRpcDescriptor Implements IExportedBrokeredService.Descriptor
					Get
						Throw New NotImplementedException()
					End Get
				End Property

				Public Function InitializeAsync(cancellationToken As CancellationToken) As Task Implements IExportedBrokeredService.InitializeAsync
					Throw New NotImplementedException()
				End Function
			End Class
			
			Interface IOptional
			End Interface
			""";
		await VerifyVB.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingInterface_MultipleAttributes_VB()
	{
		string test = VBPreamble + """
			<ExportBrokeredService("MyService", Nothing, GetType(IOptional), [|GetType(IOptional2)|])>
			<ExportBrokeredService("MyService", Nothing, [|GetType(IOptional3)|])>
			Class MyService
				Implements IExportedBrokeredService, IOptional

				Public ReadOnly Property Descriptor As ServiceRpcDescriptor Implements IExportedBrokeredService.Descriptor
					Get
						Throw New NotImplementedException()
					End Get
				End Property

				Public Function InitializeAsync(cancellationToken As CancellationToken) As Task Implements IExportedBrokeredService.InitializeAsync
					Throw New NotImplementedException()
				End Function
			End Class
			
			Interface IOptional
			End Interface

			Interface IOptional2
			End Interface

			Interface IOptional3
			End Interface
			""";
		await VerifyVB.VerifyAnalyzerAsync(test);
	}
}
