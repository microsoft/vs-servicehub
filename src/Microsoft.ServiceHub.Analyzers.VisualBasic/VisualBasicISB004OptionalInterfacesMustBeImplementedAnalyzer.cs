// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers;

[DiagnosticAnalyzer(LanguageNames.VisualBasic)]
public class VisualBasicISB004OptionalInterfacesMustBeImplementedAnalyzer : AbstractISB004OptionalInterfacesMustBeImplementedAnalyzer
{
	protected override SyntaxNode? FindArgumentSyntax(SyntaxNode? attributeSyntax, int position)
	{
		if (attributeSyntax is AttributeSyntax csAttributeSyntax)
		{
			if (csAttributeSyntax.ArgumentList?.Arguments.Count > position)
			{
				return csAttributeSyntax.ArgumentList.Arguments[position];
			}
		}

		return null;
	}
}
