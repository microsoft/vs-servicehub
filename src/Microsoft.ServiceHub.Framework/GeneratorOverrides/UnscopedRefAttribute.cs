// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Used to indicate a byref escapes and is not scoped.
/// </summary>
/// <devremarks>
/// Prevents conflict between PolySharp and CsWin generation of UnscopedRefAttribute.
/// </devremarks>
[AttributeUsageAttribute(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class UnscopedRefAttribute
	: Attribute
{
}
