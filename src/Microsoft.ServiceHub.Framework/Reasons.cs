// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// String constants for use in <see cref="RequiresUnreferencedCodeAttribute"/> and <see cref="RequiresDynamicCodeAttribute"/> attributes.
/// </summary>
internal static class Reasons
{
#pragma warning disable SA1600 // Elements should be documented
	internal const string Formatters = "This API may create formatters that require this functionality.";
	internal const string DynamicProxy = "This API creates dynamic proxies that require this functionality.";
	internal const string TypeLoad = "This API loads types by name.";
}
