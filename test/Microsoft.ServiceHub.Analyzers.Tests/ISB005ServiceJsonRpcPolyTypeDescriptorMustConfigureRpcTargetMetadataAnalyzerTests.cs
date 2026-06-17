// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VerifyCS = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.ISB005ServiceJsonRpcPolyTypeDescriptorMustConfigureRpcTargetMetadataAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;
using VerifyVB = VisualBasicCodeFixVerifier<Microsoft.ServiceHub.Analyzers.ISB005ServiceJsonRpcPolyTypeDescriptorMustConfigureRpcTargetMetadataAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

public class ISB005ServiceJsonRpcPolyTypeDescriptorMustConfigureRpcTargetMetadataAnalyzerTests
{
	private const string CSPreamble = """
		using System;
		using Microsoft.ServiceHub.Framework;

		""";

	private const string VBPreamble = """
		Imports System
		Imports Microsoft.ServiceHub.Framework

		""";

	[Fact]
	public async Task Valid_ImmediateInvocation_CS()
	{
		string test = CSPreamble + """
			class Test
			{
				private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcPolyTypeDescriptor(
					new ServiceMoniker("MyService", new Version(1, 0)),
					ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
					ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
					null!)
					.WithRpcTargetMetadata(default)
					.WithExceptionStrategy(default);
			}
			""";
		await VerifyCS.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingImmediateInvocation_CS()
	{
		string test = CSPreamble + """
			class Test
			{
				private static readonly ServiceRpcDescriptor Descriptor = [|new ServiceJsonRpcPolyTypeDescriptor(
					new ServiceMoniker("MyService", new Version(1, 0)),
					ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
					ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
					null!)|];
			}
			""";
		await VerifyCS.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Valid_MetadataConfiguredLater_CS()
	{
		string test = CSPreamble + """
			class Test
			{
				private static readonly ServiceRpcDescriptor Descriptor = new ServiceJsonRpcPolyTypeDescriptor(
					new ServiceMoniker("MyService", new Version(1, 0)),
					ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
					ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
					null!)
					.WithExceptionStrategy(default)
					.WithRpcTargetMetadata(default);
			}
			""";
		await VerifyCS.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Valid_ImmediateInvocation_VB()
	{
		string test = VBPreamble + """
			Class Test
				Private Shared ReadOnly Descriptor As ServiceRpcDescriptor = New ServiceJsonRpcPolyTypeDescriptor(
					New ServiceMoniker("MyService", New Version(1, 0)),
					ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
					ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
					Nothing).
					WithRpcTargetMetadata(Nothing).
					WithExceptionStrategy(Nothing)
			End Class
			""";
		await VerifyVB.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Invalid_MissingImmediateInvocation_VB()
	{
		string test = VBPreamble + """
			Class Test
				Private Shared ReadOnly Descriptor As ServiceRpcDescriptor = [|New ServiceJsonRpcPolyTypeDescriptor(
					New ServiceMoniker("MyService", New Version(1, 0)),
					ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
					ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
					Nothing)|]
			End Class
			""";
		await VerifyVB.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task Valid_MetadataConfiguredLater_VB()
	{
		string test = VBPreamble + """
			Class Test
				Private Shared ReadOnly Descriptor As ServiceRpcDescriptor = New ServiceJsonRpcPolyTypeDescriptor(
					New ServiceMoniker("MyService", New Version(1, 0)),
					ServiceJsonRpcPolyTypeDescriptor.Formatters.NerdbankMessagePack,
					ServiceJsonRpcPolyTypeDescriptor.MessageDelimiters.BigEndianInt32LengthHeader,
					Nothing).
					WithExceptionStrategy(Nothing).
					WithRpcTargetMetadata(Nothing)
			End Class
			""";
		await VerifyVB.VerifyAnalyzerAsync(test);
	}
}
