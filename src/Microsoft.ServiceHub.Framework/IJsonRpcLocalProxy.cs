// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An interface that local proxy objects may implement to generate proxies for other types.
/// </summary>
public interface IJsonRpcLocalProxy
{
	/// <summary>
	/// Creates a local proxy for a new type that targets the same underlying object as the current proxy.
	/// </summary>
	/// <typeparam name="T">Type of the interface to create a proxy for.</typeparam>
	/// <returns>An instance of T or null if the underlying object does not implement T.</returns>
	T? ConstructLocalProxy<T>()
		where T : class;
}
