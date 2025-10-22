// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1202 // Elements should be ordered by access - because field initializer depend on each other

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Testing;

internal static class ReferencesHelper
{
	private static readonly string NuGetConfigPath = FindNuGetConfigPath();

	public static readonly ReferenceAssemblies DefaultReferences = ReferenceAssemblies.Net.Net80
		.WithNuGetConfigFilePath(NuGetConfigPath)
		.WithPackages(ImmutableArray.Create(
			new PackageIdentity("System.ComponentModel.Composition", "8.0.0"),
			new PackageIdentity("System.Threading.Tasks.Extensions", "4.5.4"),
			new PackageIdentity("Microsoft.VisualStudio.Threading", "17.13.2"),
			new PackageIdentity("Microsoft.VisualStudio.Validation", "17.8.8")));

	private static string FindNuGetConfigPath()
	{
		string? path = AppContext.BaseDirectory;
		while (path is not null)
		{
			string candidate = Path.Combine(path, "nuget.config");
			if (File.Exists(candidate))
			{
				return candidate;
			}

			path = Path.GetDirectoryName(path);
		}

		throw new InvalidOperationException("Could not find NuGet.config by searching up from " + AppContext.BaseDirectory);
	}
}
