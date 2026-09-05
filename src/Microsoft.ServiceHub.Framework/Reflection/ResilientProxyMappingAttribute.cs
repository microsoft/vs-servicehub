// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;

namespace Microsoft.ServiceHub.Framework.Reflection;

/// <summary>
/// Maps an RPC interface to a source-generated resilient proxy class.
/// </summary>
/// <param name="proxyClass">The source-generated resilient proxy class.</param>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ResilientProxyMappingAttribute(
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] Type proxyClass) : Attribute
{
	/// <summary>
	/// Gets the class that implements a resilient proxy for the attributed interface.
	/// </summary>
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
	public Type ProxyClass => proxyClass;
}
