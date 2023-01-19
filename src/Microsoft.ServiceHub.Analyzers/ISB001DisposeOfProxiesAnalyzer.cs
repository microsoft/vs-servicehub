// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.ServiceHub.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public class ISB001DisposeOfProxiesAnalyzer : DiagnosticAnalyzer
{
	public const string Id = "ISB001";

	public static readonly DiagnosticDescriptor NonDisposalDescriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB001_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB001_DisposeOfAcquiredProxy_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		description: new LocalizableResourceString(nameof(Strings.ISB001_DisposeOfAcquiredProxy_Description), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Reliability",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor OverwrittenMemberDescriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB001_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB001_DisposeOfProxyBeforeReplacingReference_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		description: new LocalizableResourceString(nameof(Strings.ISB001_DisposeOfProxyBeforeReplacingReference_Description), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Reliability",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor ProxyMemberMustBeDisposedInDisposeMethodDescriptor = new DiagnosticDescriptor(
		id: Id,
		title: new LocalizableResourceString(nameof(Strings.ISB001_Title), Strings.ResourceManager, typeof(Strings)),
		messageFormat: new LocalizableResourceString(nameof(Strings.ISB001_ProxyMemberMustBeDisposedInDisposeMethod_MessageFormat), Strings.ResourceManager, typeof(Strings)),
		helpLinkUri: Utils.GetHelpLink(Id),
		category: "Reliability",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		NonDisposalDescriptor,
		OverwrittenMemberDescriptor,
		ProxyMemberMustBeDisposedInDisposeMethodDescriptor);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

		context.RegisterCompilationStartAction(compilationContext =>
		{
			INamedTypeSymbol? idisposable = compilationContext.Compilation.GetTypeByMetadataName(typeof(IDisposable).FullName);
			IMethodSymbol? disposeMethod = idisposable?.GetMembers(nameof(IDisposable.Dispose)).Single() as IMethodSymbol;
			INamedTypeSymbol? isbType = compilationContext.Compilation.GetTypeByMetadataName(Types.IServiceBroker.FullName);
			if (disposeMethod is object && isbType is object)
			{
				INamedTypeSymbol? sbe = compilationContext.Compilation.GetTypeByMetadataName(Types.ServiceBrokerExtensions.FullName);
				INamedTypeSymbol? sbc = compilationContext.Compilation.GetTypeByMetadataName(Types.ServiceBrokerClient.FullName);
				ImmutableArray<ISymbol> getProxyAsyncMethods = ImmutableArray.CreateRange<ISymbol>(
					isbType.GetMembers(nameof(Types.IServiceBroker.GetProxyAsync))
					.Concat(sbe?.GetMembers(nameof(Types.IServiceBroker.GetProxyAsync)) ?? ImmutableArray<ISymbol>.Empty));

				compilationContext.RegisterSymbolStartAction(
					symbolStartContext =>
					{
						var symbol = (INamedTypeSymbol)symbolStartContext.Symbol;
						var undisposedMembers = new HashSet<ISymbol>(symbol.GetMembers().Where(m => m is IFieldSymbol || m is IPropertySymbol));

						// Arrange to find GetProxyAsync calls.
						var membersThatMustBeDisposed = new HashSet<ISymbol>();
						symbolStartContext.RegisterOperationAction(Utils.DebuggableWrapper(c => this.AnalyzeInvocation(c, getProxyAsyncMethods, membersThatMustBeDisposed, disposeMethod)), OperationKind.Invocation);

						// Arrange to study all instantiations of ServiceBrokerClient.
						if (sbc is object)
						{
							symbolStartContext.RegisterOperationAction(Utils.DebuggableWrapper(c => this.AnalyzeObjectCreation(c, sbc, membersThatMustBeDisposed, disposeMethod)), OperationKind.ObjectCreation);
						}

						// Arrange to study Dispose methods.
						if (symbol.AllInterfaces.Contains(idisposable!))
						{
							symbolStartContext.RegisterOperationAction(Utils.DebuggableWrapper(c => this.AnalyzeDisposeMethods(c, undisposedMembers, disposeMethod)), OperationKind.MethodBody);
						}

						// Arrange to reconcile the two sets and raise diagnostics where fields aren't disposed.
						symbolStartContext.RegisterSymbolEndAction(symbolEndContext =>
						{
							undisposedMembers.IntersectWith(membersThatMustBeDisposed);
							foreach (ISymbol undisposedProxy in undisposedMembers)
							{
								symbolEndContext.ReportDiagnostic(
									Diagnostic.Create(
										ProxyMemberMustBeDisposedInDisposeMethodDescriptor,
										undisposedProxy.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(symbolEndContext.CancellationToken).GetLocation(),
										undisposedProxy.ContainingType.Name,
										undisposedProxy.Name));
							}
						});
					},
					SymbolKind.NamedType);
			}
		});
	}

	private static bool IsTryWithDisposalInFinally(IOperation? operation, ISymbol symbolToDispose, IMethodSymbol disposeMethod)
	{
		return operation is ITryOperation { Finally: { Operations: { } operations } }
			&& operations.Any(op => IsDisposalOf(op, symbolToDispose, disposeMethod));
	}

	private static bool IsDisposeMethod(IMethodSymbol invokedMethod, IMethodSymbol disposableDisposeMethod)
	{
		return Equals(invokedMethod, disposableDisposeMethod)
			|| (invokedMethod?.Name == nameof(IDisposable.Dispose) && invokedMethod.Parameters.Length == 0 && invokedMethod.DeclaredAccessibility == Accessibility.Public);
	}

	private static bool IsDisposalOf(IOperation? operation, ISymbol symbol, IMethodSymbol disposeMethod)
	{
		bool CheckDisposal(IInvocationOperation invocation, IOperation operand)
		{
			if (IsDisposeMethod(invocation.TargetMethod, disposeMethod))
			{
				if (operand is ILocalReferenceOperation local)
				{
					return Equals(local.Local, symbol);
				}

				if (operand is IMemberReferenceOperation member)
				{
					return Equals(member.Member, symbol);
				}
			}

			return false;
		}

		switch (operation)
		{
			case IExpressionStatementOperation { Operation: IConditionalAccessOperation { Operation: IConversionOperation { Operand: { } operand }, WhenNotNull: IInvocationOperation invocation } }:
				// (proxy as IDisposable)?.Dispose();
				return CheckDisposal(invocation, operand);
			case IExpressionStatementOperation { Operation: IInvocationOperation { Instance: IConversionOperation { Operand: { } operand } } invocation }:
				// ((IDisposable)proxy).Dispose();
				return CheckDisposal(invocation, operand);
			case IExpressionStatementOperation { Operation: IConditionalAccessOperation { Operation: { } operand, WhenNotNull: IInvocationOperation invocation } }:
				// serviceBrokerClient?.Dispose();
				return CheckDisposal(invocation, operand);
			case IUsingOperation { Resources: IConversionOperation { Operand: ILocalReferenceOperation { Local: { } local } } }:
				// using (proxy as IDisposable)
				return Equals(local, symbol);
			default:
				return false;
		}
	}

	private static ISymbol? IsDisposalOfAny(IOperation? operation, HashSet<ISymbol> symbols, IMethodSymbol disposeMethod)
	{
		ISymbol? CheckDisposal(IInvocationOperation invocation, IOperation? receiver = null)
		{
			receiver ??= invocation.Instance;
			if (IsDisposeMethod(invocation.TargetMethod, disposeMethod))
			{
				if (receiver is ILocalReferenceOperation local)
				{
					lock (symbols)
					{
						if (symbols.Contains(local.Local))
						{
							return local.Local;
						}
					}
				}
				else if (receiver is IMemberReferenceOperation member)
				{
					lock (symbols)
					{
						if (symbols.Contains(member.Member))
						{
							return member.Member;
						}
					}
				}
			}

			return null;
		}

		switch (operation)
		{
			case IExpressionStatementOperation { Operation: IConditionalAccessOperation { Operation: IConversionOperation { Operand: { } operand }, WhenNotNull: IInvocationOperation invocation } }:
				// (proxy as IDisposable)?.Dispose();
				return CheckDisposal(invocation, operand);
			case IExpressionStatementOperation { Operation: IInvocationOperation { Instance: IConversionOperation { Operand: { } operand } } invocation }:
				// ((IDisposable)proxy).Dispose();
				return CheckDisposal(invocation, operand);
			case IExpressionStatementOperation { Operation: IConditionalAccessOperation { Operation: { } operand, WhenNotNull: IInvocationOperation invocation } }:
				// serviceBrokerClient?.Dispose();
				return CheckDisposal(invocation, operand);
			case IExpressionStatementOperation { Operation: IInvocationOperation invocation }:
				// serviceBrokerClient.Dispose();
				return CheckDisposal(invocation);
			default:
				return null;
		}
	}

	private static void EnsureAssignedValueIsDisposed(OperationAnalysisContext context, HashSet<ISymbol> membersThatMustBeDisposed, IMethodSymbol disposeMethod, IOperation operation)
	{
		// Look for an assignment to a local variable or member.
		IAssignmentOperation? assignmentOperation = Utils.FindAncestors<IAssignmentOperation>(operation).FirstOrDefault();
		IVariableInitializerOperation? varInitializerOperation = Utils.FindAncestors<IVariableInitializerOperation>(operation).FirstOrDefault();
		if (assignmentOperation is null && varInitializerOperation is null)
		{
			context.ReportDiagnostic(Diagnostic.Create(NonDisposalDescriptor, operation.Syntax.GetLocation(), "proxy"));
			return;
		}

		ILocalSymbol? assignedLocal = (assignmentOperation?.Target as ILocalReferenceOperation)?.Local ?? (varInitializerOperation?.Parent as IVariableDeclaratorOperation)?.Symbol;
		if (assignedLocal is object)
		{
			// The disposal must be within this same method. And it must be one of these:
			// 1. The very next statement.
			IOperation? statementOperation = Utils.FindStatementParent(operation);
			IOperation? nextStatement = Utils.GetNextOperation(statementOperation);
			if (IsDisposalOf(nextStatement, assignedLocal, disposeMethod))
			{
				return;
			}

			// 2. In a finally block, whose try starts immediately after this assignment.
			if (IsTryWithDisposalInFinally(nextStatement, assignedLocal, disposeMethod))
			{
				return;
			}

			// 3. In a finally block for a try statement that the assigning statement belonged to.
			if (Utils.FindAncestors<ITryOperation>(statementOperation).Any(t => IsTryWithDisposalInFinally(t, assignedLocal, disposeMethod)))
			{
				return;
			}

			// 4. Inside the resource expression of a using block.
			if (Utils.FindAncestors<IUsingOperation>(operation).FirstOrDefault()?.Resources.Descendants().Contains(operation) ?? false)
			{
				return;
			}

			// No match with a recognized and acceptable pattern was found. Report the diagnostic.
			context.ReportDiagnostic(Diagnostic.Create(NonDisposalDescriptor, operation.Syntax.GetLocation(), assignedLocal.Name));
		}
		else if (assignmentOperation?.Target is IMemberReferenceOperation { Member: { } assignedMember } memberReference)
		{
			if (!(context.ContainingSymbol is IMethodSymbol methodSymbol && methodSymbol.Name == System.Reflection.ConstructorInfo.ConstructorName))
			{
				// The assignment must either be...
				// 1. within a block whose condition guarantees the member is null
				bool conditioned = false;
				foreach (IConditionalOperation conditionBlock in Utils.FindAncestors<IConditionalOperation>(operation))
				{
					// Is this a "proxy == null" check?
					if (conditionBlock.Condition is IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } binaryOperation)
					{
						if (IsNullConstant(binaryOperation.LeftOperand) && binaryOperation.RightOperand is IConversionOperation { Operand: IMemberReferenceOperation { Member: { } memberRight } })
						{
							conditioned |= Equals(memberRight, assignedMember);
						}
						else if (IsNullConstant(binaryOperation.RightOperand) && binaryOperation.LeftOperand is IConversionOperation { Operand: IMemberReferenceOperation { Member: { } memberLeft } })
						{
							conditioned |= Equals(memberLeft, assignedMember);
						}
					}
					else if (conditionBlock.Condition is IIsPatternOperation { Pattern: { Type: null }, Value: IMemberReferenceOperation { Member: { } member } })
					{
						conditioned |= Equals(member, assignedMember);
					}

					if (conditioned)
					{
						break;
					}
				}

				if (!conditioned)
				{
					// 2. preceded by disposal of the prior value, if any.
					IOperation? statementOperation = Utils.FindStatementParent(operation);
					IOperation? priorStatement = Utils.GetPriorOperation(statementOperation);
					if (!IsDisposalOf(priorStatement, assignedMember, disposeMethod))
					{
						context.ReportDiagnostic(Diagnostic.Create(OverwrittenMemberDescriptor, memberReference.Syntax.GetLocation(), memberReference.Syntax));
					}
				}
			}

			// Verify that the member is disposed of by a Dispose() method.
			lock (membersThatMustBeDisposed)
			{
				membersThatMustBeDisposed.Add(assignedMember);
			}
		}
	}

	private static bool IsNullConstant(IOperation operation) => operation is { ConstantValue: { HasValue: true, Value: null } };

	private void AnalyzeObjectCreation(OperationAnalysisContext context, INamedTypeSymbol serviceBrokerClientSymbol, HashSet<ISymbol> membersThatMustBeDisposed, IMethodSymbol disposeMethod)
	{
		var operation = (IObjectCreationOperation)context.Operation;

		// Is this "new ServiceBrokerClient"?
		if (Equals(operation.Type, serviceBrokerClientSymbol))
		{
			EnsureAssignedValueIsDisposed(context, membersThatMustBeDisposed, disposeMethod, operation);
		}
	}

	private void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<ISymbol> getProxyAsyncMethods, HashSet<ISymbol> membersThatMustBeDisposed, IMethodSymbol disposeMethod)
	{
		var operation = (IInvocationOperation)context.Operation;
		if (getProxyAsyncMethods.Contains(operation.TargetMethod.OriginalDefinition))
		{
			EnsureAssignedValueIsDisposed(context, membersThatMustBeDisposed, disposeMethod, operation);
		}
	}

	private void AnalyzeDisposeMethods(OperationAnalysisContext context, HashSet<ISymbol> undisposedMembers, IMethodSymbol disposeMethod)
	{
		static bool IsDisposeBoolMethod(IMethodSymbol methodSymbol) => methodSymbol.Name == "Dispose" && methodSymbol.Parameters.Length == 1 && methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_Boolean && methodSymbol.ReturnType?.SpecialType == SpecialType.System_Void;
		static bool IsDisposeMethod(IMethodSymbol methodSymbol) => methodSymbol.Name == "Dispose" && methodSymbol.Parameters.Length == 0 && methodSymbol.ReturnType?.SpecialType == SpecialType.System_Void;

		var operation = (IMethodBodyOperation)context.Operation;
		if (context.ContainingSymbol is IMethodSymbol methodSymbol && (IsDisposeMethod(methodSymbol) || IsDisposeBoolMethod(methodSymbol) || methodSymbol.ExplicitInterfaceImplementations.Contains(disposeMethod)))
		{
			foreach (IOperation? op in operation.Descendants())
			{
				if (undisposedMembers.Count == 0)
				{
					break;
				}

				if (IsDisposalOfAny(op, undisposedMembers, disposeMethod) is { } disposedMember)
				{
					lock (undisposedMembers)
					{
						undisposedMembers.Remove(disposedMember);
					}
				}
			}
		}
	}
}
