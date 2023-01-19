// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public abstract class RpcTestBase<TInterface, TServiceMock> : TestBase, IAsyncLifetime
	where TInterface : class
	where TServiceMock : TInterface, new()
{
#pragma warning disable CS8618 // We initialize non-nullable fields in InitializeAsync
	public RpcTestBase(ITestOutputHelper logger, ServiceRpcDescriptor serviceRpcDescriptor)
#pragma warning restore CS8618 // We initialize non-nullable fields in InitializeAsync
		: base(logger)
	{
		this.Descriptor = serviceRpcDescriptor ?? throw new ArgumentNullException(nameof(serviceRpcDescriptor));
		this.Service = new TServiceMock();
	}

	public ServiceRpcDescriptor Descriptor { get; }

	public TServiceMock Service { get; protected set; }

	public TInterface ClientProxy { get; protected set; }

	public virtual async Task InitializeAsync()
	{
		Func<string, TraceSource> traceSourceFactory = name =>
			new TraceSource(name)
			{
				Switch = { Level = SourceLevels.Verbose },
				Listeners =
				{
					new XunitTraceListener(this.Logger),
				},
			};

		(Stream, Stream) underlyingStreams = FullDuplexStream.CreatePair();
		Task<MultiplexingStream> mxStreamTask1 = MultiplexingStream.CreateAsync(
			underlyingStreams.Item1,
			new MultiplexingStream.Options
			{
				TraceSource = traceSourceFactory("Client mxstream"),
				DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => traceSourceFactory($"Client mxstream {id} (\"{name}\")"),
			},
			this.TimeoutToken);
		Task<MultiplexingStream> mxStreamTask2 = MultiplexingStream.CreateAsync(
			underlyingStreams.Item2,
			new MultiplexingStream.Options
			{
				TraceSource = traceSourceFactory("Server mxstream"),
				DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => traceSourceFactory($"Server mxstream {id} (\"{name}\")"),
			},
			this.TimeoutToken);
		MultiplexingStream[] mxStreams = await Task.WhenAll(mxStreamTask1, mxStreamTask2);

		Task<MultiplexingStream.Channel> offerTask = mxStreams[0].OfferChannelAsync(string.Empty, this.TimeoutToken);
		Task<MultiplexingStream.Channel> acceptTask = mxStreams[1].AcceptChannelAsync(string.Empty, this.TimeoutToken);
		MultiplexingStream.Channel[] channels = await Task.WhenAll(offerTask, acceptTask);

#pragma warning disable CS0618 // Type or member is obsolete
		this.ClientProxy = this.Descriptor
			.WithTraceSource(traceSourceFactory("Client RPC"))
			.WithMultiplexingStream(mxStreams[0])
			.ConstructRpc<TInterface>(channels[0]);
		this.Descriptor
			.WithTraceSource(traceSourceFactory("Server RPC"))
			.WithMultiplexingStream(mxStreams[1])
			.ConstructRpc(this.Service, channels[1]);
#pragma warning restore CS0618 // Type or member is obsolete
	}

	public virtual Task DisposeAsync()
	{
		return Task.CompletedTask;
	}
}
