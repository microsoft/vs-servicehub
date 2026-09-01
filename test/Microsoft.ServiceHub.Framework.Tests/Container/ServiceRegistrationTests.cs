// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Utilities.ServiceBroker;

public class ServiceRegistrationTests
{
	[Fact]
	public void Equality_ConsidersAudienceAllowGuestClientsAndProfferingPackageId()
	{
		ServiceRegistration baseline = new(ServiceAudience.Process, "package", allowGuestClients: true);
		ServiceRegistration equal = new(ServiceAudience.Process, "package", allowGuestClients: true);
		ServiceRegistration differentAudience = new(ServiceAudience.Local, "package", allowGuestClients: true);
		ServiceRegistration differentAllowGuestClients = new(ServiceAudience.Process, "package", allowGuestClients: false);
		ServiceRegistration differentProfferingPackageId = new(ServiceAudience.Process, "other-package", allowGuestClients: true);

		Assert.Equal(baseline, equal);
		Assert.Equal(baseline.GetHashCode(), equal.GetHashCode());
		Assert.NotEqual(baseline, differentAudience);
		Assert.NotEqual(baseline, differentAllowGuestClients);
		Assert.NotEqual(baseline, differentProfferingPackageId);
	}

	[Fact]
	public void Equality_ConsidersAdditionalServiceInterfaceTypeNames()
	{
		ImmutableArray<string> interfaceTypes = [typeof(IDisposable).AssemblyQualifiedName!, typeof(ICloneable).AssemblyQualifiedName!];
		ServiceRegistration baseline = new(ServiceAudience.Process, null, allowGuestClients: false)
		{
			AdditionalServiceInterfaceTypeNames = interfaceTypes,
		};
		ServiceRegistration equal = new(ServiceAudience.Process, null, allowGuestClients: false)
		{
			AdditionalServiceInterfaceTypeNames = interfaceTypes,
		};
		ServiceRegistration differentInterfaceSet = new(ServiceAudience.Process, null, allowGuestClients: false)
		{
			AdditionalServiceInterfaceTypeNames = [typeof(IDisposable).AssemblyQualifiedName!],
		};
		ServiceRegistration differentInterfaceOrder = new(ServiceAudience.Process, null, allowGuestClients: false)
		{
			AdditionalServiceInterfaceTypeNames = [typeof(ICloneable).AssemblyQualifiedName!, typeof(IDisposable).AssemblyQualifiedName!],
		};

		Assert.Equal(baseline, equal);
		Assert.Equal(baseline.GetHashCode(), equal.GetHashCode());
		Assert.NotEqual(baseline, differentInterfaceSet);
		Assert.NotEqual(baseline, differentInterfaceOrder);
	}
}
