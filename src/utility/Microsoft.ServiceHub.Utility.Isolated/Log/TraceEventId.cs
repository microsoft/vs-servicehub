// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// Class containing trace event ID constants.
/// We use a class with int members instead of an enum
/// because then we don't have to typecast the enum to
/// an int every time we want to trace an event.
/// </summary>
internal static class TraceEventId
{
	/// <summary>
	/// An unknown trace event.
	/// </summary>
	public const int Unknown = 0;
}
