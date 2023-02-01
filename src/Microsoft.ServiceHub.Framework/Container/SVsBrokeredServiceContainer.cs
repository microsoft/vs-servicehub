// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

#pragma warning disable SA1302 // Interface names should begin with I

/// <summary>
/// The service ID for the <see cref="IBrokeredServiceContainer">brokered service container</see>.
/// </summary>
[Guid("893F2DE0-EA58-49C1-ACBA-CE6B16236ABF")]
public interface SVsBrokeredServiceContainer
{
}
