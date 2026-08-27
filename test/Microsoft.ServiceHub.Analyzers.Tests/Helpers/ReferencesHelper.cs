// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1202 // Elements should be ordered by access - because field initializer depend on each other

using System.Collections.Immutable;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using PolyType;
using StreamJsonRpc;

internal static class ReferencesHelper
{
	private static readonly string NuGetConfigPath = FindNuGetConfigPath();

	public static readonly ReferenceAssemblies References =
#if NETFRAMEWORK
		ReferenceAssemblies.NetFramework.Net472.Default
#else
		ReferenceAssemblies.Net.Net80
#endif
		.WithNuGetConfigFilePath(NuGetConfigPath)
		.WithPackages(
		[
#if NETFRAMEWORK
			new PackageIdentity("Microsoft.Bcl.AsyncInterfaces", "10.0.10"),
#endif
			new PackageIdentity("System.ComponentModel.Composition", "10.0.9"),
			new PackageIdentity("System.Threading.Tasks.Extensions", "4.5.4"),
			new PackageIdentity("Microsoft.VisualStudio.Threading", "18.7.57"),
			new PackageIdentity("Microsoft.VisualStudio.Validation", "18.7.19"),
		]);

	/// <summary>Replaces the .NET 8 reference assembly for System.Collections.Immutable with the version used by this test project.</summary>
	/// <param name="solution">The test solution.</param>
	/// <param name="projectId">The test project to update.</param>
	/// <returns>The updated test solution.</returns>
	internal static Solution UseCurrentImmutableCollections(Solution solution, ProjectId projectId)
	{
		foreach (MetadataReference reference in solution.GetProject(projectId)!.MetadataReferences.Where(
			reference => string.Equals(Path.GetFileName(reference.Display), "System.Collections.Immutable.dll", StringComparison.OrdinalIgnoreCase)))
		{
			solution = solution.RemoveMetadataReference(projectId, reference);
		}

		return solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(ImmutableArray<>).Assembly.Location));
	}

	internal static IEnumerable<MetadataReference> GetReferences()
	{
		yield return MetadataReference.CreateFromFile(typeof(JsonRpc).Assembly.Location);
		yield return MetadataReference.CreateFromFile(typeof(GenerateShapeAttribute).Assembly.Location);
		yield return MetadataReference.CreateFromFile(typeof(IServiceBroker).Assembly.Location);
		yield return MetadataReference.CreateFromFile(typeof(IDisposableObservable).Assembly.Location);
	}

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
