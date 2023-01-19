// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Service identifier for <see cref="AsyncLazy{IServiceBroker}"/> instance that is owned by the service hub host and
/// returned from <see cref="IServiceProvider"/> collection.
/// </summary>
[Guid("70d9e3aa-438d-42e2-9faa-7132796068c6")]
#pragma warning disable SA1302 // Interface names should begin with I
public interface SHostServiceBrokerFactory
#pragma warning restore SA1302 // Interface names should begin with I
{
}
