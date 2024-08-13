// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public abstract class AbstractISB004OptionalInterfacesMustBeImplementedAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "ISB004";

	public static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB004_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB004_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		description: new LocalizableResourceString(nameof(Strings.ISB004_Description), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

		context.RegisterCompilationStartAction(context =>
		{
			if (context.Compilation.ReferencedAssemblyNames.Any(id => string.Equals(id.Name, "Microsoft.ServiceHub.Framework", StringComparison.OrdinalIgnoreCase)))
			{
				INamedTypeSymbol? attType = context.Compilation.GetTypeByMetadataName("Microsoft.VisualStudio.Shell.ServiceBroker.ExportBrokeredServiceAttribute");
				if (attType is object)
				{
					context.RegisterSymbolAction(context => this.AnalyzeSymbol(context, attType), SymbolKind.NamedType);
				}
			}
		});
	}

	protected abstract SyntaxNode? FindArgumentSyntax(SyntaxNode? attributeSyntax, int position);

	private void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol attType)
	{
		if (context.Symbol is not INamedTypeSymbol namedType)
		{
			return;
		}

		foreach (AttributeData exportAttribute in context.Symbol.GetAttributes().Where(att => att.AttributeClass?.Equals(attType, SymbolEqualityComparer.Default) is true))
		{
			if (exportAttribute.ConstructorArguments.Length < 3)
			{
				continue;
			}

			int ifaceIndex = -1;
			foreach (TypedConstant optionalInterface in exportAttribute.ConstructorArguments[2].Values)
			{
				ifaceIndex++;
				if (optionalInterface.Value is not INamedTypeSymbol ifaceType)
				{
					continue;
				}

				if (!namedType.AllInterfaces.Contains(ifaceType))
				{
					// Start with a fallback location.
					Location location = namedType.Locations[0];

					// Try to use a location that is specific to this interface.
					if (this.FindArgumentSyntax(exportAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken), 2 + ifaceIndex) is SyntaxNode argumentSyntax)
					{
						location = argumentSyntax.GetLocation();
					}

					context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, ifaceType.Name));
				}
			}
		}
	}
}
