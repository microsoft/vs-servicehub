// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Composition;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Provides an <see cref="IServiceBroker"/> implementation for brokered services proffered via MEF
/// and serves as the factory for these services.
/// </summary>
/// <remarks>
/// This factory only creates <em>one</em> brokered service.
/// As this object serves as the entry into a MEF sharing boundary, that means only one brokered service gets an instance of this class.
/// This is as desired, since each consumer of an <see cref="IServiceBroker"/> should ideally have its own copy.
/// </remarks>
[Export]
[Export(typeof(IServiceBroker))]
[Shared(ExportedBrokeredServiceSharingBoundary)]
internal class ServiceBrokerForExportedBrokeredServices : IServiceBroker, IDisposable
{
	internal const string ExportedBrokeredServiceSharingBoundary = "Microsoft.VisualStudio.ExportedBrokeredServiceScope";

	private readonly object syncObject = new();
	private IServiceBroker? innerServiceBroker;
	private EventHandler<BrokeredServicesChangedEventArgs>? availabilityChanged;

	event EventHandler<BrokeredServicesChangedEventArgs>? IServiceBroker.AvailabilityChanged
	{
		add
		{
			lock (this.syncObject)
			{
				if (this.availabilityChanged is null && this.InnerServiceBroker is not null)
				{
					this.InnerServiceBroker.AvailabilityChanged += this.ServiceBroker_AvailabilityChanged;
				}

				this.availabilityChanged += value;
			}
		}

		remove
		{
			lock (this.syncObject)
			{
				this.availabilityChanged -= value;

				if (this.availabilityChanged is null && this.InnerServiceBroker is not null)
				{
					this.InnerServiceBroker.AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
				}
			}
		}
	}

	/// <summary>
	/// Gets or sets the <see cref="IServiceBroker"/> that this instance forwards all calls to.
	/// </summary>
	/// <remarks>
	/// This should be set early during MEF brokered service activation so that it can be imported by the brokered service once activated.
	/// This should be set to an object that is scoped for the particular activated brokered service the same way the object passed to
	/// <see cref="BrokeredServiceFactory"/> would be.
	/// </remarks>
	internal IServiceBroker? InnerServiceBroker
	{
		get => this.innerServiceBroker;
		set
		{
			lock (this.syncObject)
			{
				Verify.Operation(this.innerServiceBroker is null, "Already set.");
				this.innerServiceBroker = value;
				if (this.innerServiceBroker is not null && this.availabilityChanged is not null)
				{
					this.innerServiceBroker.AvailabilityChanged += this.ServiceBroker_AvailabilityChanged;
				}
			}
		}
	}

	/// <summary>
	/// Gets or sets the <see cref="ServiceActivationOptions"/> for the particular brokered service this object in created to serve.
	/// </summary>
	/// <remarks>
	/// This should be set early during MEF brokered service activation so that it can be imported by the brokered service once activated.
	/// </remarks>
	[Export]
	internal ServiceActivationOptions ServiceActivationOptions { get; set; }

	/// <summary>
	/// Gets or sets the <see cref="ServiceMoniker"/> for the particular brokered service this object in created to serve.
	/// </summary>
	/// <remarks>
	/// This should be set early during MEF brokered service activation so that it can be imported by the brokered service once activated.
	/// </remarks>
	[Export]
	internal ServiceMoniker? ActivatedMoniker { get; set; }

	/// <summary>
	/// Gets or sets the <see cref="ServiceHub.Framework.Services.AuthorizationServiceClient"/> to be exported for optional use by the brokered service.
	/// </summary>
	[Export]
	internal AuthorizationServiceClient? AuthorizationServiceClient { get; set; }

	/// <summary>
	/// Gets an enumerable of the metadata exported from all MEF-based brokered services.
	/// </summary>
	/// <remarks>
	/// This is used for brokered service registration and proffering by the <see cref="ServiceBrokerOfExportedServices.Initialize"/> method.
	/// </remarks>
	internal IEnumerable<IBrokeredServicesExportMetadata> ExportedServiceMetadata => this.Helper.ExportedBrokeredServices.Select(e => e.Metadata);

	/// <summary>
	/// Gets or sets a helper class that contains MefV1 imports.
	/// </summary>
	[Import]
	private MefV1Helper Helper { get; set; } = null!;

	ValueTask<IDuplexPipe?> IServiceBroker.GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
	{
		Assumes.Present(this.InnerServiceBroker);
		return this.InnerServiceBroker.GetPipeAsync(serviceMoniker, options, cancellationToken);
	}

	ValueTask<T?> IServiceBroker.GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
		where T : class
	{
		Assumes.Present(this.InnerServiceBroker);
		return this.InnerServiceBroker.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken);
	}

	public void Dispose()
	{
		this.AuthorizationServiceClient?.Dispose();
	}

	/// <summary>
	/// Activates the one brokered service indicated by the <see cref="ActivatedMoniker"/> property.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The activated service. The caller should invoke <see cref="IExportedBrokeredService.InitializeAsync(CancellationToken)"/> on the result.</returns>
	internal IExportedBrokeredService? CreateBrokeredService(CancellationToken cancellationToken)
	{
		Verify.Operation(this.ActivatedMoniker is not null, "Exporting properties must be set first.");

		string? requiredVersion = this.ActivatedMoniker.Version?.ToString();
		Lazy<IExportedBrokeredService, IBrokeredServicesExportMetadata>? nullVersionedFactory = null;

		// First search for an exact version match.
		foreach (Lazy<IExportedBrokeredService, IBrokeredServicesExportMetadata> factory in this.Helper.ExportedBrokeredServices)
		{
			for (int i = 0; i < factory.Metadata.ServiceName.Length; i++)
			{
				if (this.ActivatedMoniker.Name == factory.Metadata.ServiceName[i])
				{
					if (requiredVersion == factory.Metadata.ServiceVersion[i])
					{
						Verify.Operation(!factory.IsValueCreated, "This method should only be called once.");
						return factory.Value;
					}

					if (factory.Metadata.ServiceVersion[i] is null)
					{
						// Remember this in case we don't find an exact version match.
						nullVersionedFactory = factory;
					}
				}
			}
		}

		// Fallback to the catch-all version if we found one.
		if (nullVersionedFactory is not null)
		{
			Verify.Operation(!nullVersionedFactory.IsValueCreated, "This method should only be called once.");
			return nullVersionedFactory.Value;
		}

		return null;
	}

	private void ServiceBroker_AvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs e) => this.availabilityChanged?.Invoke(this, e);

	/// <summary>
	/// A class with MEFv1 attributes to force brokered service NonShared construction.
	/// </summary>
	/// <remarks>
	/// Our parent class must use MEFv2 attributes because only they can express sharing boundaries with <see cref="SharedAttribute"/>.
	/// But only MEFv1 attributes allow setting <see cref="System.ComponentModel.Composition.ImportManyAttribute.RequiredCreationPolicy"/>
	/// as required to activate a new brokered service for each client.
	/// We bridge this feature gap between the two sets of attributes via this helper class, so that the outer class can use MEFv2
	/// and the nested class can use MEFv1, thereby making both sets of features available as required.
	/// </remarks>
	[System.ComponentModel.Composition.Export]
	[System.ComponentModel.Composition.PartCreationPolicy(System.ComponentModel.Composition.CreationPolicy.NonShared)]
	private class MefV1Helper
	{
		/// <summary>
		/// Gets or sets the collection of all brokered services, from which only one is ever activated (for a given instance of this class).
		/// </summary>
		[System.ComponentModel.Composition.ImportMany(RequiredCreationPolicy = System.ComponentModel.Composition.CreationPolicy.NonShared)]
		internal List<Lazy<IExportedBrokeredService, IBrokeredServicesExportMetadata>> ExportedBrokeredServices { get; set; } = null!;
	}
}
