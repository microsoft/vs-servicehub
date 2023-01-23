// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Runtime.InteropServices;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;

namespace ServiceBrokerTest;

public class Program
{
	private static async Task<int> Main(string[] args)
	{
		Option<bool> fireEventSwitch = new("--event", "Fire an availability changed event.");
		Option<bool> useNamedPipes = new("--named-pipes", "Offer services over named pipes instead of multiplexing.");

		var rootCommand = new RootCommand()
		{
			fireEventSwitch,
			useNamedPipes,
		};

		rootCommand.SetHandler(Run, fireEventSwitch, useNamedPipes);
		return await rootCommand.InvokeAsync(args);
	}

	private static async Task Run(bool fireAvailabilityChanged, bool useNamedPipes)
	{
		using (Stream input = Console.OpenStandardInput())
		using (Stream output = Console.OpenStandardOutput())
		{
			Stream stdioStream = FullDuplexStream.Splice(input, output);
			ServiceBroker serviceBroker = new();

			// Do NOT arrange to dispose of the multiplexing stream, since we're using stdio pipes for it and they don't honor cancellation,
			// which can result in deadlocks when trying to shut it down.
			MultiplexingStream mxStream = await MultiplexingStream.CreateAsync(stdioStream);
			MultiplexingStream.Channel channel = await mxStream.OfferChannelAsync(string.Empty).ConfigureAwait(false);

			if (useNamedPipes)
			{
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					throw new NotSupportedException("Only supported on Windows.");
				}

				IpcRelayServiceBroker relayBroker = new(serviceBroker);
				FrameworkServices.RemoteServiceBroker.ConstructRpc(relayBroker, channel);

				if (fireAvailabilityChanged)
				{
					await serviceBroker.FireAvailabilityChangedAsync();
				}
			}
			else
			{
				MultiplexingRelayServiceBroker relayBroker = new(serviceBroker, mxStream);
				FrameworkServices.RemoteServiceBroker.ConstructRpc(relayBroker, channel);
				if (fireAvailabilityChanged)
				{
					await serviceBroker.FireAvailabilityChangedAsync();
				}
			}

			await channel.Completion;
		}
	}
}
