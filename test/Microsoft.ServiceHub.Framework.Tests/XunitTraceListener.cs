// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text;
using Microsoft;
using Xunit.Abstractions;

internal class XunitTraceListener : TraceListener
{
	private readonly List<string> tracedMessages = new List<string>();
	private readonly object syncRoot = new object();
	private readonly ITestOutputHelper xunitLogger;
	private readonly StringBuilder lineBuffer = new StringBuilder();

	public XunitTraceListener(ITestOutputHelper xunitLogger)
	{
		Requires.NotNull(xunitLogger, nameof(xunitLogger));
		this.xunitLogger = xunitLogger;
		this.Filter = new EventTypeFilter(SourceLevels.Verbose);
	}

	internal IEnumerable<string> TracedMessages => this.tracedMessages;

	public override void Write(string? message)
	{
		lock (this.syncRoot)
		{
			this.lineBuffer.Append(message);
		}
	}

	public override void WriteLine(string? message)
	{
		lock (this.syncRoot)
		{
			message = this.lineBuffer.ToString() + message;
			this.lineBuffer.Clear();
		}

		this.xunitLogger.WriteLine(message);
		this.tracedMessages.Add(message);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			lock (this.syncRoot)
			{
				if (this.lineBuffer.Length > 0)
				{
					this.xunitLogger.WriteLine(this.lineBuffer.ToString());
					this.lineBuffer.Clear();
				}
			}
		}

		base.Dispose(disposing);
	}
}
