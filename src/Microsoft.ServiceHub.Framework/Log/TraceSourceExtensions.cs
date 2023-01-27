// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A class containing extension methods for <see cref="TraceSource"/>.
/// </summary>
internal static class TraceSourceExtensions
{
	/// <summary>
	/// Traces an exception to the trace source as an error level message.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="exception">An exception.</param>
	internal static void TraceException(this TraceSource logger, Exception exception)
	{
		Requires.NotNull(exception, nameof(exception));
		logger.TraceEvent(TraceEventType.Error, 0, exception.ToStringWithInnerExceptions());
	}

	/// <summary>
	/// Traces an exception to the trace source as an error level message.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="exception">An exception.</param>
	/// <param name="format">Additional string to write out.</param>
	/// <param name="args">Arguments to be used in the format string.</param>
	internal static void TraceException(this TraceSource logger, Exception exception, string format, params object?[]? args)
	{
		logger.TraceEvent(TraceEventType.Error, 0, ExceptionFormatter.FormatException(exception, format, args));
	}
}
