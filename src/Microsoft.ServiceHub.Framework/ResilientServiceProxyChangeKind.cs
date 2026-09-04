// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes how the backing service of a resilient proxy changed.
/// </summary>
public enum ResilientServiceProxyChangeKind
{
	/// <summary>
	/// A backing service became available after the resilient proxy had none.
	/// </summary>
	Gained,

	/// <summary>
	/// The backing service became unavailable and no replacement was acquired.
	/// </summary>
	Lost,

	/// <summary>
	/// One backing service was replaced directly by another.
	/// </summary>
	Replaced,
}
