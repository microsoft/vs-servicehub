// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

#pragma warning disable SA1302 // Interface names should begin with I

/// <summary>
/// A type to serve as the MEF contract name for importing an <see cref="IServiceBroker"/> that is equivalent
/// to what would have come from a call to <see cref="IBrokeredServiceContainer.GetFullAccessServiceBroker"/> on
/// the <see cref="SVsBrokeredServiceContainer"/>.
/// </summary>
/// <remarks>
/// This can be imported in a MEF part like this:
/// <code><![CDATA[
/// [Import(typeof(SVsFullAccessServiceBroker))]
/// private IServiceBroker serviceBroker;
/// ]]></code>
/// </remarks>
public interface SVsFullAccessServiceBroker
{
}
