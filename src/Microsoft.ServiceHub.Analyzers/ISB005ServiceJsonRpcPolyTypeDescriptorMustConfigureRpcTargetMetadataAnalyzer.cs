// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.ServiceHub.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public class ISB005ServiceJsonRpcPolyTypeDescriptorMustConfigureRpcTargetMetadataAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "ISB005";

	public static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB005_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB005_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		description: new LocalizableResourceString(nameof(Strings.ISB005_Description), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptor];

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
			INamedTypeSymbol? descriptorType = compilationStart.Compilation.GetTypeByMetadataName(Types.ServiceJsonRpcPolyTypeDescriptor.FullName);
			if (descriptorType is object)
			{
				compilationStart.RegisterOperationAction(Utils.DebuggableWrapper(c => AnalyzeObjectCreation(c, descriptorType)), OperationKind.ObjectCreation);
			}
		});
	}

	private static void AnalyzeObjectCreation(OperationAnalysisContext context, INamedTypeSymbol descriptorType)
	{
		var creation = (IObjectCreationOperation)context.Operation;
		if (!SymbolEqualityComparer.Default.Equals(creation.Type, descriptorType) || IsCopyConstructor(creation, descriptorType) || HasWithRpcTargetMetadataInFluentChain(creation, descriptorType))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Descriptor, creation.Syntax.GetLocation()));
	}

	private static bool IsCopyConstructor(IObjectCreationOperation creation, INamedTypeSymbol descriptorType)
		=> creation.Constructor is { Parameters.Length: 1 } constructor && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, descriptorType);

	private static bool HasWithRpcTargetMetadataInFluentChain(IObjectCreationOperation creation, INamedTypeSymbol descriptorType)
	{
		IOperation current = creation;
		while (true)
		{
			while (current.Parent is IConversionOperation parentConversion)
			{
				current = parentConversion;
			}

			if (current.Parent is not IInvocationOperation invocation || invocation.Instance != current)
			{
				return false;
			}

			if (string.Equals(invocation.TargetMethod.Name, Types.ServiceJsonRpcPolyTypeDescriptor.WithRpcTargetMetadata, StringComparison.Ordinal) &&
				SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, descriptorType))
			{
				return true;
			}

			current = invocation;
		}
	}
}
