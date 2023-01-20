// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.ServiceHub.Analyzers
{
	internal static class Utils
	{
		internal static string GetHelpLink(string analyzerId)
		{
			return $"https://dev.azure.com/devdiv/DevDiv/_git/DevCore?path=%2Fdoc%2Fanalyzers%2F{analyzerId}.md&version=GC{ThisAssembly.GitCommitId}";
		}

		internal static IEnumerable<TOperation> FindAncestors<TOperation>(IOperation? operation)
			where TOperation : class, IOperation
		{
			do
			{
				operation = operation?.Parent;
				if (operation is TOperation op)
				{
					yield return op;
				}
			}
			while (operation is object);
		}

		/// <summary>
		/// Returns the ancestor of some operation that is an immediate child of an <see cref="IBlockOperation"/>.
		/// </summary>
		/// <param name="operation">The operation to search ancestors of.</param>
		/// <returns>The parent statement, if it can be found.</returns>
		internal static IOperation? FindStatementParent(IOperation operation)
		{
			IOperation? operationLocal = operation;
			do
			{
				if (operationLocal.Parent is IBlockOperation)
				{
					return operationLocal;
				}

				operationLocal = operationLocal.Parent;
			}
			while (operationLocal is object);

			return null;
		}

		internal static IOperation? GetNextOperation(IOperation? operation)
		{
			if (operation?.Parent is null)
			{
				return null;
			}

			bool found = false;
			foreach (IOperation? op in operation.Parent.Children)
			{
				if (found)
				{
					return op;
				}

				found = op == operation;
			}

			return null;
		}

		internal static IOperation? GetPriorOperation(IOperation? operation)
		{
			if (operation?.Parent is null)
			{
				return null;
			}

			IOperation? priorOperation = null;
			foreach (IOperation? op in operation.Parent.Children)
			{
				if (op == operation)
				{
					return priorOperation;
				}

				priorOperation = op;
			}

			return null;
		}

		internal static Action<OperationAnalysisContext> DebuggableWrapper(Action<OperationAnalysisContext> handler)
		{
			return ctxt =>
			{
				try
				{
					handler(ctxt);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex) when (LaunchDebuggerExceptionFilter())
				{
					throw new Exception($"Analyzer failure while processing syntax at {ctxt.Operation.Syntax.SyntaxTree.FilePath}({ctxt.Operation.Syntax.GetLocation()?.GetLineSpan().StartLinePosition.Line + 1},{ctxt.Operation.Syntax.GetLocation()?.GetLineSpan().StartLinePosition.Character + 1}): {ex.GetType()} {ex.Message}. Syntax: {ctxt.Operation.Syntax}", ex);
				}
			};
		}

		internal static Action<SymbolAnalysisContext> DebuggableWrapper(Action<SymbolAnalysisContext> handler)
		{
			return ctxt =>
			{
				try
				{
					handler(ctxt);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex) when (LaunchDebuggerExceptionFilter())
				{
					SyntaxNode? syntax = ctxt.Symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
					throw new Exception($"Analyzer failure while processing symbol {ctxt.Symbol.Name} at {syntax?.GetLocation()?.GetLineSpan().StartLinePosition.Line + 1},{syntax?.GetLocation()?.GetLineSpan().StartLinePosition.Character + 1}): {ex.GetType()} {ex.Message}. Syntax: {syntax}", ex);
				}
			};
		}

		private static bool LaunchDebuggerExceptionFilter()
		{
#if DEBUG
			System.Diagnostics.Debugger.Launch();
#endif
			return true;
		}
	}
}
