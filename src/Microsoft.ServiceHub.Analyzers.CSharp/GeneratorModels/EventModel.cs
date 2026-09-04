// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers.GeneratorModels;

internal record EventModel(string DeclaringType, string Name, string DelegateType, string EventArgsType, bool SupportsResilientProxy) : FormattableModel
{
	internal override void WriteEvents(SourceWriter writer)
	{
		writer.WriteLine($$"""

			public event {{this.DelegateType}}? {{this.Name}}
			{
				add
				{
					if (this.TargetOrNull is {{this.DeclaringType}} target)
					{
						target.{{this.Name}} += value;
					}

				}

				remove
				{
					if (this.TargetOrNull is {{this.DeclaringType}} target)
					{
						target.{{this.Name}} -= value;
					}
				}
			}
			""");
	}

	internal override void WriteResilientConstructorStatements(SourceWriter writer)
	{
		writer.WriteLine($$"""
			this.{{this.Name}}Subscription = this.CreateEvent<{{this.DelegateType}}>(
				(sender, args) => this.{{this.Name}}Handlers?.Invoke(this, args),
				() => this.{{this.Name}}Handlers is not null,
				(proxy, handler) => (({{this.DeclaringType}})proxy).{{this.Name}} += handler,
				(proxy, handler) => (({{this.DeclaringType}})proxy).{{this.Name}} -= handler);
			""");
	}

	internal override void WriteResilientEvents(SourceWriter writer)
	{
		writer.WriteLine($$"""

			public event {{this.DelegateType}}? {{this.Name}}
			{
				add
				{
					UpdateEventHandlers(ref this.{{this.Name}}Handlers, value, add: true);
					this.{{this.Name}}Subscription.UpdateActiveState();
				}

				remove
				{
					UpdateEventHandlers(ref this.{{this.Name}}Handlers, value, add: false);
					this.{{this.Name}}Subscription.UpdateActiveState();
				}
			}
			""");
	}

	internal override void WriteResilientFields(SourceWriter writer)
	{
		writer.WriteLine($$"""
			private {{this.DelegateType}}? {{this.Name}}Handlers;
			private readonly ResilientEvent<{{this.DelegateType}}> {{this.Name}}Subscription;
			""");
	}

	internal static EventModel? Create(IEventSymbol evt, KnownSymbols symbols)
	{
		if (evt.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
		{
			return null;
		}

		bool supportsResilientProxy = invokeMethod.ReturnsVoid
			&& invokeMethod.Parameters.Length == 2
			&& invokeMethod.Parameters[0].RefKind == RefKind.None
			&& invokeMethod.Parameters[0].Type.SpecialType == SpecialType.System_Object
			&& invokeMethod.Parameters[1].RefKind == RefKind.None;
		string eventArgsType = invokeMethod.Parameters.Length > 1
			? invokeMethod.Parameters[1].Type.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat)
			: string.Empty;
		return new EventModel(
			evt.ContainingType.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat),
			evt.Name,
			evt.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			eventArgsType,
			supportsResilientProxy);
	}
}
