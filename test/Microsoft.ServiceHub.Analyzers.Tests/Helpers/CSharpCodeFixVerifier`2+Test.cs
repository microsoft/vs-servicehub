// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Microsoft.CodeAnalysis.Text;
using Microsoft.ServiceHub.Framework;

public static partial class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
{
	public class Test : CSharpCodeFixTest<TAnalyzer, TCodeFix, XUnitVerifier>
	{
		public Test()
		{
			this.ReferenceAssemblies = ReferencesHelper.DefaultReferences;

			this.SolutionTransforms.Add((solution, projectId) =>
			{
				var parseOptions = (CSharpParseOptions?)solution.GetProject(projectId)?.ParseOptions;
				solution = solution.WithProjectParseOptions(projectId, parseOptions!.WithLanguageVersion(LanguageVersion.CSharp7_3))
					.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(IServiceBroker).Assembly.Location));

				return solution;
			});

			this.TestState.AdditionalFilesFactories.Add(() =>
			{
				const string additionalFilePrefix = "AdditionalFiles.";
				return from resourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames()
					   where resourceName.StartsWith(additionalFilePrefix, StringComparison.Ordinal)
					   let content = ReadManifestResource(Assembly.GetExecutingAssembly(), resourceName)
					   select (filename: resourceName.Substring(additionalFilePrefix.Length), SourceText.From(content));
			});
		}

		internal DiagnosticDescriptor? ExpectedDescriptor { get; set; }

		protected override DiagnosticDescriptor? GetDefaultDiagnostic(DiagnosticAnalyzer[] analyzers)
		{
			return this.ExpectedDescriptor ?? base.GetDefaultDiagnostic(analyzers);
		}

		private static string ReadManifestResource(Assembly assembly, string resourceName)
		{
			using (var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException("No resource stream by that name.", resourceName)))
			{
				return reader.ReadToEnd();
			}
		}
	}
}
