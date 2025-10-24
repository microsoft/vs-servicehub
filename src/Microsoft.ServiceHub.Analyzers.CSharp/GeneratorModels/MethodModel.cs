// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers.GeneratorModels;

internal record MethodModel(string DeclaringInterfaceName, string Name, string ReturnType, RpcSpecialType ReturnSpecialType, string? ReturnTypeArg, ImmutableEquatableArray<ParameterModel> Parameters) : FormattableModel
{
	internal bool TakesCancellationToken => this.Parameters.Length > 0 && this.Parameters[^1].SpecialType == RpcSpecialType.CancellationToken;

	internal ParameterModel? CancellationToken => this.TakesCancellationToken ? this.Parameters[^1] : null;

	/// <summary>
	/// Gets a span over the parameters that exclude the <see cref="CancellationToken"/>.
	/// </summary>
	internal ReadOnlyMemory<ParameterModel> DataParameters => this.Parameters.AsMemory()[..(this.Parameters.Length - (this.TakesCancellationToken ? 1 : 0))];

	private string? CancellationTokenExpression => this.CancellationToken?.Name;

	internal override void WriteMethods(SourceWriter writer)
	{
		// The possible methods we invoke are as follows:
		// | Return type | Named args | Signature
		// | Task        | Yes        | Task InvokeWithParameterObjectAsync(string targetName, object? argument, IReadOnlyDictionary<string, Type>? argumentDeclaredTypes, CancellationToken cancellationToken)
		// | Task<T>     | Yes        | Task<TResult> InvokeWithParameterObjectAsync<TResult>(string targetName, object? argument, IReadOnlyDictionary<string, Type>? argumentDeclaredTypes, CancellationToken cancellationToken)
		// | void        | Yes        | Task NotifyWithParameterObjectAsync(string targetName, object? argument, IReadOnlyDictionary<string, Type>? argumentDeclaredTypes)
		// | Task        | No         | Task InvokeWithCancellationAsync(string targetName, IReadOnlyList<object?>? arguments, IReadOnlyList<Type> argumentDeclaredTypes, CancellationToken cancellationToken)
		// | Task<T>     | No         | Task<TResult> InvokeWithCancellationAsync<TResult>(string targetName, IReadOnlyList<object?>? arguments, IReadOnlyList<Type>? argumentDeclaredTypes, CancellationToken cancellationToken)
		// | void        | No         | Task NotifyAsync(string targetName, object?[]? arguments, IReadOnlyList<Type>? argumentDeclaredTypes)
		string returnTypeArg = this.ReturnTypeArg is null ? string.Empty :
			this.ReturnSpecialType == RpcSpecialType.IAsyncEnumerable ? $"<{this.ReturnType}>" :
			$"<{this.ReturnTypeArg}>";
		string cancellationArg = this.ReturnSpecialType == RpcSpecialType.Void ? string.Empty : $", {this.CancellationToken?.Name ?? "default"}";
		bool isAsync = this.ReturnSpecialType is RpcSpecialType.Task or RpcSpecialType.ValueTask;
		string asyncKeyword = isAsync ? "async " : string.Empty;
		string awaitExpression = isAsync ? "await " : string.Empty;

		writer.WriteLine($$"""

			{{asyncKeyword}}{{this.ReturnType}} {{this.DeclaringInterfaceName}}.{{this.Name}}({{string.Join(", ", this.Parameters.Select(p => $"{p.Type} {p.Name}"))}})
			{
			""");

		writer.Indentation++;

		// If a CancellationToken appears as the last parameter, consider it immediately and throw instead of anything else.
		// This simulates what would happen if a token were precanceled going into StreamJsonRpc.
		if (this.CancellationTokenExpression is not null)
		{
			writer.WriteLine($"""
				{this.CancellationTokenExpression}.ThrowIfCancellationRequested();
				""");
		}

		writer.WriteLine($$"""
			{{this.DeclaringInterfaceName}} target = ({{this.DeclaringInterfaceName}})this.Target;
			if (target is null) throw new global::System.ObjectDisposedException(this.GetType().FullName);
			try
			{
			""");
		writer.Indentation++;

		string returnKeyword = this.ReturnSpecialType != RpcSpecialType.Void && this.ReturnTypeArg is not null ? "return " : string.Empty;
		writer.WriteLine($$"""
			{{returnKeyword}}{{awaitExpression}}target.{{this.Name}}(
			""");

		writer.Indentation++;

		for (int i = 0; i < this.Parameters.Length; i++)
		{
			bool isLastParameter = i == this.Parameters.Length - 1;
			string name = this.Parameters[i].Name;
			writer.WriteLine(isLastParameter ? $"{name}" : $"{name},");
		}

		writer.WriteLine(");");

		writer.Indentation -= 2;
		writer.WriteLine("""
			}
			catch (global::System.Exception ex)
			{
				throw this.ExceptionHelper(ex);
			}
			""");

		writer.Indentation--;
		writer.WriteLine("""
			}
			""");
	}

	internal static MethodModel Create(IMethodSymbol method, KnownSymbols symbols)
	{
		return new MethodModel(
			method.ContainingType.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat),
			method.Name,
			method.ReturnType.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat),
			ProxyGenerator.ClassifySpecialType(method.ReturnType, symbols),
			method.ReturnType is INamedTypeSymbol { IsGenericType: true, TypeArguments: [ITypeSymbol typeArg] } ? typeArg.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat) : null,
			new([.. method.Parameters.Select(p => ParameterModel.Create(p, symbols))]));
	}
}
