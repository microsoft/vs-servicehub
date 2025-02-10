﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Testing;

internal static class ReferencesHelper
{
	public static readonly ReferenceAssemblies DefaultReferences = ReferenceAssemblies.Net.Net80
		.WithPackages(ImmutableArray.Create(
			new PackageIdentity("System.ComponentModel.Composition", "8.0.0"),
			new PackageIdentity("System.Threading.Tasks.Extensions", "4.5.4"),
			new PackageIdentity("Microsoft.VisualStudio.Threading", "17.12.19"),
			new PackageIdentity("Microsoft.VisualStudio.Validation", "17.8.8")));
}
