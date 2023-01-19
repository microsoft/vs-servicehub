// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Globalization;

using static System.FormattableString;

namespace Microsoft.ServiceHub.Utility;

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
		IsolatedUtilities.RequiresNotNull(exception, nameof(exception));
		logger.TraceError(exception.ToStringWithInnerExceptions());
	}

	/// <summary>
	/// Traces an exception to the trace source as an error level message.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="exception">An exception.</param>
	/// <param name="format">Additional string to write out.</param>
	/// <param name="args">Arguments to be used in the format string.</param>
	internal static void TraceException(this TraceSource logger, Exception exception, string format, params object[] args)
	{
		logger.TraceError(FormatException(exception, format, args));
	}

	/// <summary>
	/// Traces an exception to the trace source as an information level message.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="exception">An exception.</param>
	/// <param name="format">Additional string to trace.</param>
	/// <param name="args">Arguments to be used in the format string.</param>
	internal static void TraceExceptionAsInformation(this TraceSource logger, Exception exception, string format, params object[] args)
	{
		logger.TraceInformation(FormatException(exception, format, args));
	}

	/// <summary>
	/// Traces an error to the trace source.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="message">The message to trace.</param>
	internal static void TraceError(this TraceSource logger, string message)
	{
		logger.TraceEvent(TraceEventType.Error, TraceEventId.Unknown, message);
	}

	/// <summary>
	/// Traces an error to the trace source.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="format">String to trace.</param>
	/// <param name="args">Arguments to be used in the format string.</param>
	internal static void TraceError(this TraceSource logger, string format, params object[] args)
	{
		logger.TraceEvent(TraceEventType.Error, TraceEventId.Unknown, format, args);
	}

	/// <summary>
	/// Traces a warning to the trace source.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="message">The message to trace.</param>
	internal static void TraceWarning(this TraceSource logger, string message)
	{
		logger.TraceEvent(TraceEventType.Warning, TraceEventId.Unknown, message);
	}

	/// <summary>
	/// Traces a warning to the trace source.
	/// </summary>
	/// <param name="logger">A trace source.</param>
	/// <param name="format">String to trace.</param>
	/// <param name="args">Arguments to be used in the format string.</param>
	internal static void TraceWarning(this TraceSource logger, string format, params object[] args)
	{
		logger.TraceEvent(TraceEventType.Warning, TraceEventId.Unknown, format, args);
	}

	/// <summary>
	/// Format an exception into a readable string.
	/// </summary>
	/// <param name="exception">The exception to format.</param>
	/// <param name="format">An additional string message to include in the string.</param>
	/// <param name="args">Arguments to be used in the format string.</param>
	/// <returns>A formatted string representing the exception.</returns>
	internal static string FormatException(Exception exception, string format, params object[] args)
	{
		IsolatedUtilities.RequiresNotNull(exception, nameof(exception));
		IsolatedUtilities.RequiresNotNullOrEmpty(format, nameof(format));

		return Invariant($"{FormatInvariant(format, args)}: {exception.ToStringWithInnerExceptions()}");
	}

	private static string FormatInvariant(string format, object[] args)
	{
		return args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
	}
}
