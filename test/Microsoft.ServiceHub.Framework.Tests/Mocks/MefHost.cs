// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.VisualStudio.Composition;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

internal class MefHost
{
	private static readonly AsyncLazy<ComposableCatalog> Catalog = new(CreateCatalogAsync);
	private static readonly AsyncLazy<IExportProviderFactory> ExportProviderFactory = new(() => CreateExportProviderFactoryAsync(CancellationToken.None), null);

	internal GlobalBrokeredServiceContainer? BrokeredServiceContainer { get; set; }

	internal static Task<ComposableCatalog> GetCatalogAsync(CancellationToken cancellationToken) => Catalog.GetValueAsync(cancellationToken);

	internal async ValueTask<ExportProvider> CreateExportProviderAsync(CancellationToken cancellationToken = default)
	{
		IExportProviderFactory exportProviderFactory = await ExportProviderFactory.GetValueAsync(cancellationToken);
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

	private static async Task<IExportProviderFactory> CreateExportProviderFactoryAsync(CancellationToken cancellationToken)
	{
		ComposableCatalog catalog = await Catalog.GetValueAsync(cancellationToken);
		CompositionConfiguration configuration = CompositionConfiguration.Create(catalog);
		configuration.ThrowOnErrors();
		return configuration.CreateExportProviderFactory();
	}

	private static async Task<ComposableCatalog> CreateCatalogAsync()
	{
		PartDiscovery discovery = PartDiscovery.Combine(
			new AttributedPartDiscovery(Resolver.DefaultInstance, isNonPublicSupported: true),
			new AttributedPartDiscoveryV1(Resolver.DefaultInstance));
		Assembly[] catalogAssemblies = new[]
		{
			typeof(GlobalBrokeredServiceContainer).Assembly,
			typeof(MefHost).Assembly,
		};
		DiscoveredParts parts = await discovery.CreatePartsAsync(catalogAssemblies);
		ComposableCatalog catalog = ComposableCatalog.Create(Resolver.DefaultInstance).AddParts(parts);
		return catalog;
	}
}
