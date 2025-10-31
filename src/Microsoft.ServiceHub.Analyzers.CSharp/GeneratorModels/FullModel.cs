// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers.GeneratorModels;

internal record FullModel(ImmutableEquatableArray<ProxyModel> Proxies)
{
	internal required bool PublicProxies { get; init; }

	internal void GenerateSource(SourceProductionContext context)
	{
		try
		{
			foreach (ProxyModel proxy in this.Proxies)
			{
				proxy.GenerateSource(context, this.PublicProxies);
			}
		}
		catch (Exception) when (Utils.LaunchDebuggerExceptionFilter())
		{
			throw;
		}
	}
}
