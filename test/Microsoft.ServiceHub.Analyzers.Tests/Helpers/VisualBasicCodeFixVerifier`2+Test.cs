// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Testing;
using Microsoft.ServiceHub.Framework;

public static partial class VisualBasicCodeFixVerifier<TAnalyzer, TCodeFix>
{
	public class Test : VisualBasicCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
	{
		public Test()
		{
			this.ReferenceAssemblies = ReferencesHelper.References;

			this.SolutionTransforms.Add((solution, projectId) =>
			{
				var parseOptions = (VisualBasicParseOptions?)solution.GetProject(projectId)?.ParseOptions;
				solution = solution.WithProjectParseOptions(projectId, parseOptions!.WithLanguageVersion(LanguageVersion.Latest))
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
