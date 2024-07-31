// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.VisualStudio.Composition;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

internal class MefHost(bool serializedCatalog = false)
{
	private static readonly AsyncLazy<ComposableCatalog> Catalog = new(() => CreateCatalogAsync(serialized: false, CancellationToken.None));
	private static readonly AsyncLazy<ComposableCatalog> SerializedCatalog = new(() => CreateCatalogAsync(serialized: true, CancellationToken.None));

	private static readonly AsyncLazy<IExportProviderFactory> ExportProviderFactory = new(() => CreateExportProviderFactoryAsync(serialized: false, CancellationToken.None), null);
	private static readonly AsyncLazy<IExportProviderFactory> SerializedExportProviderFactory = new(() => CreateExportProviderFactoryAsync(serialized: true, CancellationToken.None), null);

	internal GlobalBrokeredServiceContainer? BrokeredServiceContainer { get; set; }

	internal async ValueTask<ExportProvider> CreateExportProviderAsync(CancellationToken cancellationToken = default)
	{
		IExportProviderFactory exportProviderFactory = await (serializedCatalog ? SerializedExportProviderFactory : ExportProviderFactory).GetValueAsync(cancellationToken);
		ExportProvider exportProvider = exportProviderFactory.CreateExportProvider();

		// Bootstrap brokered services
		if (this.BrokeredServiceContainer is object)
		{
			MockServiceBrokerOfExportedServices mockBrokeredServices = exportProvider.GetExportedValue<MockServiceBrokerOfExportedServices>();
			mockBrokeredServices.Container = this.BrokeredServiceContainer;
			await mockBrokeredServices.RegisterAndProfferServicesAsync(cancellationToken);
		}

		return exportProvider;
	}

	private static async Task<IExportProviderFactory> CreateExportProviderFactoryAsync(bool serialized, CancellationToken cancellationToken)
	{
		if (serialized)
		{
			ComposableCatalog catalog = await SerializedCatalog.GetValueAsync(cancellationToken);
			CompositionConfiguration configuration = CompositionConfiguration.Create(catalog);
			CachedComposition cachedComposition = new();
			MemoryStream ms = new();
			await cachedComposition.SaveAsync(configuration, ms, cancellationToken);
			ms.Position = 0;
			return await cachedComposition.LoadExportProviderFactoryAsync(ms, Resolver.DefaultInstance, cancellationToken);
		}
		else
		{
			ComposableCatalog catalog = await Catalog.GetValueAsync(cancellationToken);
			CompositionConfiguration configuration = CompositionConfiguration.Create(catalog);
			configuration.ThrowOnErrors();
			return configuration.CreateExportProviderFactory();
		}
	}

	private static async Task<ComposableCatalog> CreateCatalogAsync(bool serialized, CancellationToken cancellationToken)
	{
		PartDiscovery discovery = PartDiscovery.Combine(
			new AttributedPartDiscovery(Resolver.DefaultInstance, isNonPublicSupported: true),
			new AttributedPartDiscoveryV1(Resolver.DefaultInstance));
		Assembly[] catalogAssemblies = new[]
		{
			typeof(GlobalBrokeredServiceContainer).Assembly,
			typeof(MefHost).Assembly,
		};
		DiscoveredParts parts = await discovery.CreatePartsAsync(catalogAssemblies, cancellationToken: cancellationToken);
		ComposableCatalog catalog = ComposableCatalog.Create(Resolver.DefaultInstance).AddParts(parts);

		if (serialized)
		{
			CachedCatalog cachedCatalog = new CachedCatalog();
			MemoryStream ms = new();
			await cachedCatalog.SaveAsync(catalog, ms, cancellationToken);
			ms.Position = 0;
			catalog = await cachedCatalog.LoadAsync(ms, Resolver.DefaultInstance, cancellationToken);
		}

		return catalog;
	}
}
