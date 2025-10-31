// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Analyzers.GeneratorModels;

namespace Microsoft.ServiceHub.Analyzers;

[Generator(LanguageNames.CSharp)]
public class ProxyGenerator : IIncrementalGenerator
{
	/// <summary>
	/// The namespace under which proxies (and interceptors) are generated.
	/// </summary>
	public const string GenerationNamespace = "Microsoft.ServiceHub.Framework.Generated";

	internal static readonly SymbolDisplayFormat FullyQualifiedWithNullableFormat = new(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	internal static readonly SymbolDisplayFormat FullyQualifiedNoGlobalWithNullableFormat = new(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<ImmutableEquatableArray<ProxyModel>> rpcContractProxyProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
			Types.JsonRpcContractAttribute.FullName,
			(n, ct) => true,
			PrepareProxy);

		IncrementalValueProvider<bool> publicProxy = context.CompilationProvider.Select((c, token) => this.AreProxiesPublic(c));

		IncrementalValueProvider<FullModel> fullModel = rpcContractProxyProvider.Collect().Combine(publicProxy).Select(
			(combined, ct) =>
			{
				ImmutableArray<ImmutableEquatableArray<ProxyModel>> allProxies = combined.Left;
				bool publicProxies = combined.Right;
				return new FullModel([.. allProxies.SelectMany(p => p)])
				{
					PublicProxies = publicProxies,
				};
			});

		context.RegisterSourceOutput(fullModel, (context, model) => model.GenerateSource(context));

		ImmutableEquatableArray<ProxyModel> PrepareProxy(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
		{
			if (context.TargetSymbol is not INamedTypeSymbol iface)
			{
				return [];
			}

			KnownSymbols symbols = new(context.SemanticModel.Compilation);
			if (!symbols.HasRequiredReferences)
			{
				return [];
			}

			// Skip inaccessible interfaces.
			if (!context.SemanticModel.Compilation.IsSymbolAccessibleWithin(context.TargetSymbol, context.SemanticModel.Compilation.Assembly))
			{
				// Reported by StreamJsonRpc0001
				return [];
			}

			bool hasOptionalInterfaces = iface.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, symbols.RpcMarshalableOptionalInterface));

			IEnumerable<ProxyModel> proxies = ExpandInterfaceToGroups(iface, symbols)
				.Select(group => new ProxyModel(
					[.. group.Select(i => InterfaceModel.Create(i, symbols, declaredInThisCompilation: true, cancellationToken))])
				{
					HasOptionalInterfaces = hasOptionalInterfaces,
				});

			return [.. proxies];
		}
	}

	internal static RpcSpecialType ClassifySpecialType(ITypeSymbol type, KnownSymbols symbols)
	{
		return type as INamedTypeSymbol switch
		{
			{ SpecialType: SpecialType.System_Void } => RpcSpecialType.Void,
			{ IsGenericType: true } namedType when Equal(namedType.ConstructedFrom, symbols.TaskOfT) => RpcSpecialType.Task,
			{ IsGenericType: true } namedType when Equal(namedType.ConstructedFrom, symbols.ValueTaskOfT) => RpcSpecialType.ValueTask,
			{ IsGenericType: true } namedType when Equal(namedType.ConstructedFrom, symbols.IAsyncEnumerableOfT) => RpcSpecialType.IAsyncEnumerable,
			{ IsGenericType: false } namedType when Equal(type, symbols.Task) => RpcSpecialType.Task,
			{ IsGenericType: false } namedType when Equal(type, symbols.ValueTask) => RpcSpecialType.ValueTask,
			{ IsGenericType: false } namedType when Equal(type, symbols.CancellationToken) => RpcSpecialType.CancellationToken,
			_ => RpcSpecialType.Other,
		};

		static bool Equal(ITypeSymbol candidate, ITypeSymbol? standard) => standard is not null && SymbolEqualityComparer.Default.Equals(candidate, standard);
	}

	/// <summary>
	/// Expands the specified interface into groups based on the presence of the JsonRpcProxyInterfaceGroupAttribute.
	/// Each group consists of the primary interface and any additional interfaces defined by the attribute.
	/// </summary>
	/// <remarks>This method inspects the attributes of the primary interface to determine groupings. If the
	/// JsonRpcProxyInterfaceGroupAttribute is present and specifies additional interfaces, each group will include the
	/// primary interface followed by those interfaces. If the attribute is not present, the primary interface is
	/// returned as its own group.</remarks>
	/// <param name="primary">
	/// The primary interface symbol to expand into groups. This symbol is always included as the first element in each group.
	/// </param>
	/// <param name="symbols">
	/// A container for well-known symbols, including the JsonRpcProxyInterfaceGroupAttribute used to identify interface groups.
	/// </param>
	/// <returns>
	/// An enumerable collection of interface groups, where each group is an array of INamedTypeSymbol. If no groups are
	/// defined, a single group containing only the primary interface is returned.
	/// </returns>
	private static IEnumerable<INamedTypeSymbol[]> ExpandInterfaceToGroups(INamedTypeSymbol primary, KnownSymbols symbols)
	{
		bool anyGroupsDefined = false;
		List<INamedTypeSymbol> optionalMarshalableInterfaces = [];
		foreach (AttributeData att in primary.GetAttributes())
		{
			if (SymbolEqualityComparer.Default.Equals(att.AttributeClass, symbols.RpcMarshalableOptionalInterface) && att.ConstructorArguments is [_, { Value: INamedTypeSymbol optionalInterface }])
			{
				optionalMarshalableInterfaces.Add(optionalInterface);
			}

			if (!SymbolEqualityComparer.Default.Equals(att.AttributeClass, symbols.JsonRpcProxyInterfaceGroupAttribute))
			{
				continue;
			}

			if (att.ConstructorArguments is not [{ Kind: TypedConstantKind.Array, Values: ImmutableArray<TypedConstant> additionalInterfaces }])
			{
				continue;
			}

			anyGroupsDefined = true;
			yield return [primary, .. additionalInterfaces.Select(v => v.Value).OfType<INamedTypeSymbol>()];
		}

		if (!anyGroupsDefined)
		{
			// No groups defined, so just return the primary interface as its own group.
			yield return [primary];

			// And if RpcMarshalable optional interfaces were specified, add them to another group.
			if (optionalMarshalableInterfaces.Count > 0)
			{
				yield return [primary, .. optionalMarshalableInterfaces];
			}
		}
	}

	private bool AreProxiesPublic(Compilation compilation)
		=> compilation.Assembly.GetAttributes().Any(a => a is { AttributeClass: { Name: Types.ExportRpcContractProxiesAttribute.Name, ContainingNamespace: { Name: "StreamJsonRpc", ContainingNamespace.IsGlobalNamespace: true } } });
}
