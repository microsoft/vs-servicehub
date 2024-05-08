// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.ServiceHub.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public class ISB002AvoidStoringRentalsInFieldsAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "ISB002";

	public static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB002_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB002_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		description: new LocalizableResourceString(nameof(Strings.ISB002_Description), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
		{
			throw new ArgumentNullException(nameof(context));
		}

		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

		context.RegisterCompilationStartAction(compilationStart =>
		{
			INamedTypeSymbol? rentalType = compilationStart.Compilation.GetTypeByMetadataName(Types.ServiceBrokerClient.Rental.MetadataName);
			INamedTypeSymbol? nullableType = compilationStart.Compilation.GetTypeByMetadataName(typeof(Nullable<>).FullName);
			if (rentalType is object)
			{
				compilationStart.RegisterSymbolAction(Utils.DebuggableWrapper(c => this.AnalyzeType(c, rentalType, nullableType)), SymbolKind.NamedType);
			}
		});
	}

	private void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol rentalType, INamedTypeSymbol? nullableType)
	{
		var typeSymbol = (INamedTypeSymbol)context.Symbol;

		foreach (ISymbol? member in typeSymbol.GetMembers())
		{
			switch (member)
			{
				case IFieldSymbol { Type: { } memberType } fieldSymbol:
					InspectSymbol(context, memberType, fieldSymbol.AssociatedSymbol ?? fieldSymbol, rentalType, nullableType);
					break;
			}
		}

		static void InspectSymbol(SymbolAnalysisContext context, ISymbol typeSymbol, ISymbol memberSymbol, INamedTypeSymbol rentalType, INamedTypeSymbol? nullableType)
		{
			if (SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, rentalType) ||
				(SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, nullableType) && typeSymbol is INamedTypeSymbol namedType && SymbolEqualityComparer.Default.Equals(namedType.TypeArguments.FirstOrDefault()?.OriginalDefinition, rentalType)))
			{
				context.ReportDiagnostic(
					Diagnostic.Create(
						Descriptor,
						memberSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken).GetLocation()));
			}
		}
	}
}
