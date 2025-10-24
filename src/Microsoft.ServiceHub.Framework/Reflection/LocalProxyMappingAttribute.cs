// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;

namespace Microsoft.ServiceHub.Framework.Reflection;

/// <summary>
/// An attribute that is used by our source generator to map an RPC interface to a
/// source generated proxy class that emulates StreamJsonRpc proxy behavior.
/// </summary>
/// <param name="proxyClass">
/// The source generated proxy class.
/// This must implement the interface the attribute is applied to,
/// derive from <see cref="ProxyBase"/>,
/// and declare a public constructor with a particular signature.
/// </param>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
[EditorBrowsable(EditorBrowsableState.Never)]
public class LocalProxyMappingAttribute(
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] Type proxyClass) : Attribute
{
	/// <summary>
	/// Gets the class that implements a local proxy for the attributed interface.
	/// </summary>
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
	public Type ProxyClass => proxyClass;
}
