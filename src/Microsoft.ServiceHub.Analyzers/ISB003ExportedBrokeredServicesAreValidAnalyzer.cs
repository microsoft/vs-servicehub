// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.ServiceHub.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public class ISB003ExportedBrokeredServicesAreValidAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "ISB003";

	public static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB003_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB003_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		description: new LocalizableResourceString(nameof(Strings.ISB003_Description), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

		context.RegisterCompilationStartAction(context =>
		{
			if (context.Compilation.ReferencedAssemblyNames.Any(id => string.Equals(id.Name, "Microsoft.ServiceHub.Framework", StringComparison.OrdinalIgnoreCase)))
			{
				INamedTypeSymbol? attType = context.Compilation.GetTypeByMetadataName("Microsoft.VisualStudio.Shell.ServiceBroker.ExportBrokeredServiceAttribute");
				INamedTypeSymbol? ifaceType = context.Compilation.GetTypeByMetadataName("Microsoft.VisualStudio.Shell.ServiceBroker.IExportedBrokeredService");
				if (attType is object && ifaceType is object)
				{
					context.RegisterSymbolAction(context => this.AnalyzeSymbol(context, attType, ifaceType), SymbolKind.NamedType);
				}
			}
		});
	}

	private void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol attType, INamedTypeSymbol ifaceType)
	{
		if (context.Symbol is not INamedTypeSymbol namedType)
		{
			return;
		}

		if (context.Symbol.GetAttributes().Any(att => att.AttributeClass?.Equals(attType, SymbolEqualityComparer.Default) is true))
		{
			if (!namedType.AllInterfaces.Contains(ifaceType))
			{
				context.ReportDiagnostic(Diagnostic.Create(Descriptor, namedType.Locations[0], namedType.Name));
			}
		}
	}
}
