// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Extension methods that help convert exceptions to formatted strings.
/// </summary>
internal static class ExceptionFormatter
{
	/// <summary>
	/// Converts the exception to a string, recursively expanding inner exceptions.
	/// </summary>
	/// <param name="exception">The exception to convert.</param>
	/// <returns>The string representation fo the exception.</returns>
	internal static string ToStringWithInnerExceptions(this Exception exception)
	{
		IsolatedUtilities.RequiresNotNull(exception, nameof(exception));

		var sb = new StringBuilder();
		sb.AppendLine(FormatException(exception));

		if (exception is AggregateException aggregateException)
		{
			aggregateException = aggregateException.Flatten();
			foreach (Exception innerException in aggregateException.InnerExceptions)
			{
				sb.AppendLine("===AggregateInnerException===");
				sb.AppendLine(ToStringWithInnerExceptions(innerException));
			}

			sb.AppendLine("===Finished listing Aggregate Exception's inner exceptions===");
		}
		else if (exception.InnerException != null)
		{
			sb.AppendLine("===InnerException===");
			sb.AppendLine(ToStringWithInnerExceptions(exception.InnerException));
		}

		return sb.ToString(0, sb.Length - Environment.NewLine.Length);
	}

	/// <summary>
	/// Gets the exception messages from an exception recursively looking into the inner exceptions.
	/// </summary>
	/// <param name="exception">An exception.</param>
	/// <returns>A formatted string with the exception messages.</returns>
	internal static string GetMessageWithInnerExceptions(this Exception exception)
	{
		IsolatedUtilities.RequiresNotNull(exception, nameof(exception));

		AggregateException? aggregate = exception as AggregateException;
		if (exception.InnerException == null && (aggregate == null || aggregate.InnerExceptions.Count == 0))
		{
			return exception.Message;
		}

		var sb = new StringBuilder();

		sb.Append(exception.Message).Append(" -> ");

		if (aggregate == null)
		{
			if (exception.InnerException != null)
			{
				return sb.Append(GetMessageWithInnerExceptions(exception.InnerException)).ToString();
			}

			return sb.ToString();
		}

		AggregateException flattenedAggregate = aggregate.Flatten();
		bool hasMoreThanOneInnerException = flattenedAggregate.InnerExceptions.Count > 1;
		if (hasMoreThanOneInnerException)
		{
			sb.Append("(");
		}

		sb.Append(string.Join("; ", flattenedAggregate.InnerExceptions.Select(GetMessageWithInnerExceptions)));

		if (hasMoreThanOneInnerException)
		{
			sb.Append(")");
		}

		return sb.ToString();
	}

	private static string FormatException(Exception exception) =>
		string.Format(
			CultureInfo.InvariantCulture,
			"{0}: {1} HResult='{2}' {3}",
			exception.GetType().FullName,
			exception.Message,
			exception.HResult,
			string.IsNullOrEmpty(exception.StackTrace) ? string.Empty : Environment.NewLine + exception.StackTrace);
}
