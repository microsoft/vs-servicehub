// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers.GeneratorModels;

internal record EventModel(string DeclaringType, string Name, string DelegateType, string EventArgsType) : FormattableModel
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

	internal static EventModel? Create(IEventSymbol evt, KnownSymbols symbols)
	{
		if (evt.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
		{
			return null;
		}

		return new EventModel(evt.ContainingType.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat), evt.Name, evt.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), invokeMethod.Parameters[1].Type.ToDisplayString(ProxyGenerator.FullyQualifiedWithNullableFormat));
	}
}
