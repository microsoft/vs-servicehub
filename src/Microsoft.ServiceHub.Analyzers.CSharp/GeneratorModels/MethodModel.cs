// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers.GeneratorModels;

internal record MethodModel(string DeclaringInterfaceName, string Name, string ReturnType, RpcSpecialType ReturnSpecialType, string? ReturnTypeArg, ImmutableEquatableArray<ParameterModel> Parameters, bool IsAsyncDispose, bool IsObserverSubscription) : FormattableModel
{
	internal bool SupportsResilientProxy => this.ReturnSpecialType is RpcSpecialType.Task or RpcSpecialType.ValueTask or RpcSpecialType.IAsyncEnumerable or RpcSpecialType.Void
		|| this.IsObserverSubscription;

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

	internal override void WriteResilientMethods(SourceWriter writer)
	{
		if (this.IsAsyncDispose)
		{
			return;
		}

		string cancellationToken = this.CancellationToken?.Name ?? "default";
		string arguments = string.Join(", ", this.Parameters.Select(p => p.Name));
		writer.WriteLine($$"""

			{{this.ReturnType}} {{this.DeclaringInterfaceName}}.{{this.Name}}({{string.Join(", ", this.Parameters.Select(p => $"{p.Type} {p.Name}"))}})
			{
			""");
		writer.Indentation++;

		if (this.CancellationTokenExpression is not null
			&& this.ReturnSpecialType is not RpcSpecialType.Task and not RpcSpecialType.ValueTask)
		{
			writer.WriteLine($"{this.CancellationTokenExpression}.ThrowIfCancellationRequested();");
		}

		switch (this.ReturnSpecialType)
		{
			case RpcSpecialType.Task:
			case RpcSpecialType.ValueTask:
				if (this.IsObserverSubscription)
				{
					string helperName = this.ReturnSpecialType == RpcSpecialType.Task ? "InvokeTaskSubscriptionAsync" : "InvokeValueTaskSubscriptionAsync";
					string targetName = this.GetUniqueParameterName("__resilientTarget");
					string reattachCancellationTokenName = this.GetUniqueParameterName("__resilientCancellationToken");
					string reattachArguments = string.Join(", ", this.Parameters.Select(
						(parameter, index) => this.TakesCancellationToken && index == this.Parameters.Length - 1 ? reattachCancellationTokenName : parameter.Name));
					writer.WriteLine($"return this.{helperName}({targetName} => (({this.DeclaringInterfaceName}){targetName}).{this.Name}({arguments}), ({targetName}, {reattachCancellationTokenName}) => (({this.DeclaringInterfaceName}){targetName}).{this.Name}({reattachArguments}), {cancellationToken});");
					break;
				}

				writer.WriteLine($$"""
					return InvokeAsync();

					async {{this.ReturnType}} InvokeAsync()
					{
					""");
				writer.Indentation++;
				if (this.CancellationTokenExpression is not null)
				{
					writer.WriteLine($"{this.CancellationTokenExpression}.ThrowIfCancellationRequested();");
				}

				writer.WriteLine($"using (ProxyRental rental = await this.RentProxyAsync({cancellationToken}).ConfigureAwait(false))");
				writer.WriteLine("{");
				writer.Indentation++;
				string returnKeyword = this.ReturnTypeArg is null ? string.Empty : "return ";
				writer.WriteLine($"{returnKeyword}await (({this.DeclaringInterfaceName})rental.Proxy).{this.Name}({arguments}).ConfigureAwait(false);");
				writer.Indentation--;
				writer.WriteLine("}");
				writer.Indentation--;
				writer.WriteLine("""
					}
					""");
				break;
			case RpcSpecialType.IAsyncEnumerable:
				writer.WriteLine($"return this.InvokeAsyncEnumerableAsync(target => (({this.DeclaringInterfaceName})target).{this.Name}({arguments}), {cancellationToken});");
				break;
			case RpcSpecialType.Void:
				writer.WriteLine($"this.InvokeNotification(target => (({this.DeclaringInterfaceName})target).{this.Name}({arguments}), {cancellationToken});");
				break;
			default:
				writer.WriteLine("""throw new global::System.NotSupportedException("Resilient proxies require asynchronous RPC contract methods.");""");
				break;
		}

		writer.Indentation--;
		writer.WriteLine("}");
	}

	internal static MethodModel Create(IMethodSymbol method, KnownSymbols symbols)
	{
		var parameters = new ImmutableEquatableArray<ParameterModel>([.. method.Parameters.Select(p => ParameterModel.Create(p, symbols))]);
		RpcSpecialType returnSpecialType = ProxyGenerator.ClassifySpecialType(method.ReturnType, symbols);
		string? returnTypeArg = method.ReturnType is INamedTypeSymbol { IsGenericType: true, TypeArguments: [ITypeSymbol typeArg] }
			? typeArg.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat)
			: null;
		bool isObserverSubscription = returnSpecialType is RpcSpecialType.Task or RpcSpecialType.ValueTask
			&& method.ReturnType is INamedTypeSymbol { TypeArguments: [ITypeSymbol subscriptionType] }
			&& SymbolEqualityComparer.Default.Equals(subscriptionType, symbols.IDisposable)
			&& parameters.Any(parameter => parameter.SpecialType == RpcSpecialType.IObserver)
			&& parameters is [.., { SpecialType: RpcSpecialType.CancellationToken }];
		return new MethodModel(
			method.ContainingType.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat),
			method.Name,
			method.ReturnType.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat),
			returnSpecialType,
			returnTypeArg,
			parameters,
			symbols.IAsyncDisposable is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.IAsyncDisposable),
			isObserverSubscription);
	}

	private string GetUniqueParameterName(string baseName)
	{
		string name = baseName;
		while (this.Parameters.Any(parameter => parameter.Name == name))
		{
			name += "_";
		}

		return name;
	}
}
