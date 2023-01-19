// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An RPC descriptor for services that support JSON-RPC.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public partial class ServiceJsonRpcDescriptor : ServiceRpcDescriptor, IEquatable<ServiceJsonRpcDescriptor>
{
	/// <inheritdoc cref="ServiceJsonRpcDescriptor(ServiceMoniker, Type?, Formatters, MessageDelimiters)" />
	public ServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter)
		: this(serviceMoniker, clientInterface: null, formatter, messageDelimiter)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcDescriptor"/> class and no support for opening additional streams except by relying on the underlying service broker to provide one.
	/// </summary>
	/// <inheritdoc cref="ServiceJsonRpcDescriptor(ServiceMoniker, Type?, Formatters, MessageDelimiters, MultiplexingStream.Options?)" />
	public ServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface, Formatters formatter, MessageDelimiters messageDelimiter)
		: base(serviceMoniker, clientInterface)
	{
		this.Formatter = formatter;
		this.MessageDelimiter = messageDelimiter;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcDescriptor"/> class and does support for opening additional streams with <see cref="ServiceJsonRpcDescriptor.MultiplexingStreamOptions"/>.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="clientInterface">The interface type that the client's "callback" RPC target is expected to implement. May be null if the service does not invoke methods on the client.</param>
	/// <param name="formatter">The formatter to use for the JSON-RPC message.</param>
	/// <param name="messageDelimiter">The message delimiter scheme to use.</param>
	/// <param name="multiplexingStreamOptions">The options that a <see cref="MultiplexingStream" /> may be created with. A <see langword="null"/> value will prevent a <see cref="MultiplexingStream" /> from being created for the RPC connection.</param>
	public ServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface, Formatters formatter, MessageDelimiters messageDelimiter, MultiplexingStream.Options? multiplexingStreamOptions)
		: base(serviceMoniker, clientInterface)
	{
		this.Formatter = formatter;
		this.MessageDelimiter = messageDelimiter;
		this.MultiplexingStreamOptions = multiplexingStreamOptions?.GetFrozenCopy();
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcDescriptor"/> class and initializes all fields based on a template instance.
	/// </summary>
	/// <param name="copyFrom">The instance to copy all fields from.</param>
	protected ServiceJsonRpcDescriptor(ServiceJsonRpcDescriptor copyFrom)
		: base(copyFrom)
	{
		this.Formatter = copyFrom.Formatter;
		this.MessageDelimiter = copyFrom.MessageDelimiter;
		this.MultiplexingStreamOptions = copyFrom.MultiplexingStreamOptions;
		this.ExceptionStrategy = copyFrom.ExceptionStrategy;
	}

	/// <summary>
	/// The formats that JSON-RPC can be serialized to.
	/// </summary>
	public enum Formatters
	{
		/// <summary>
		/// Format messages with UTF-8 text for a human readable JSON representation.
		/// </summary>
		UTF8,

		/// <summary>
		/// Format messages with MessagePack for a high throughput, compact binary representation.
		/// </summary>
		MessagePack,
	}

	/// <summary>
	/// The various headers that introduce a JSON-RPC message.
	/// </summary>
	public enum MessageDelimiters
	{
		/// <summary>
		/// Adds an HTTP-like header in front of each JSON-RPC message that describes its encoding and length.
		/// </summary>
		HttpLikeHeaders,

		/// <summary>
		/// Adds a big endian 32-bit integer before each JSON-RPC message describing its length.
		/// </summary>
		BigEndianInt32LengthHeader,
	}

	/// <inheritdoc />
	public override string Protocol => "json-rpc";

	/// <summary>
	/// Gets the formatting used by the service.
	/// </summary>
	public Formatters Formatter { get; }

	/// <summary>
	/// Gets the mechanism by which message boundaries may be discerned. Some expected values are found in <see cref="MessageDelimiters"/>.
	/// </summary>
	public MessageDelimiters MessageDelimiter { get; }

	/// <summary>
	/// Gets the way exceptions are communicated from the service to the client.
	/// This is set on the <see cref="JsonRpc.ExceptionStrategy"/> property when the <see cref="JsonRpc"/> instance is created.
	/// </summary>
	/// <value>The default value is <see cref="ExceptionProcessing.CommonErrorData"/>.</value>
	public ExceptionProcessing ExceptionStrategy { get; private set; } = ExceptionProcessing.CommonErrorData;

	/// <summary>
	/// Gets the options to use when creating a new <see cref="Nerdbank.Streams.MultiplexingStream"/> as a prerequisite to establishing an RPC connection.
	/// </summary>
	/// <remarks>
	/// Any non-null value from this property is always <see cref="MultiplexingStream.Options.IsFrozen">frozen</see>.
	/// </remarks>
	public MultiplexingStream.Options? MultiplexingStreamOptions { get; private set; }

	/// <summary>
	/// Gets a string for the debugger to display for this struct.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private protected string DebuggerDisplay => $"{this.Moniker.Name} via {this.Protocol}/{this.MessageDelimiter}/{this.Formatter}";

	/// <summary>
	/// Wraps some target object with a proxy that gives the caller the similar semantics to calling
	/// an actual RPC object using <see cref="JsonRpc"/>.
	/// </summary>
	/// <typeparam name="T">The interface that the returned proxy must implement.</typeparam>
	/// <param name="target">The object to which all calls to the proxy should be forwarded.</param>
	/// <returns>The proxy, or null if <paramref name="target"/> is null.</returns>
	/// <remarks>
	/// The proxy will forward all calls made to the <typeparamref name="T"/> interface to the <paramref name="target"/> object.
	/// Exceptions thrown from the target will be caught by the proxy and a <see cref="RemoteInvocationException"/> will be thrown instead
	/// with some of the original exception details preserved (but not as an <see cref="Exception.InnerException"/>) in order to
	/// emulate what an RPC connection would be like.
	/// This proxy implements <typeparamref name="T"/>.
	/// The proxy also implements <see cref="IDisposable"/> and will forward a call to <see cref="IDisposable.Dispose"/>
	/// to the <paramref name="target"/> object if the target object implements <see cref="IDisposable"/>.
	/// </remarks>
	[return: NotNullIfNotNull("target")]
	public override T? ConstructLocalProxy<T>(T? target)
		where T : class
	{
		return target != null ? LocalProxyGeneration.CreateProxy<T>(target, this.ExceptionStrategy) : null;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj) => this.Equals(obj as ServiceJsonRpcDescriptor);

	/// <inheritdoc/>
#pragma warning disable CS0672 // Base Member overrides obsolete member, To be handled at ServiceJsonRpcDescriptor only later for backward compatibility.
	public override ServiceRpcDescriptor WithMultiplexingStream(MultiplexingStream? multiplexingStream)
#pragma warning restore CS0672 // Base Member overrides obsolete member
	{
#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
		ServiceRpcDescriptor result = base.WithMultiplexingStream(multiplexingStream);
#pragma warning restore CS0618 // Type or member is obsolete

		if (result is ServiceJsonRpcDescriptor serviceJsonRpcDescriptor)
		{
			if (serviceJsonRpcDescriptor.MultiplexingStreamOptions is null)
			{
				return result;
			}

			result = serviceJsonRpcDescriptor.Clone();
			((ServiceJsonRpcDescriptor)result).MultiplexingStreamOptions = null;
		}

		return result;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcDescriptor"/> that resembles this one,
	/// but with the <see cref="MultiplexingStreamOptions" /> property set to a frozen copy of the specified value.
	/// If a <see cref="MultiplexingStream"/> has been set, it is cleared.
	/// </summary>
	/// <param name="multiplexingStreamOptions">Options to use when setting up a new <see cref="Nerdbank.Streams.MultiplexingStream"/> that should be set up on a pipe before initializing RPC; <see langword="null"/> to not set up or use any.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceRpcDescriptor WithMultiplexingStream(MultiplexingStream.Options? multiplexingStreamOptions)
	{
		if (this.MultiplexingStreamOptions == multiplexingStreamOptions && this.MultiplexingStream is null)
		{
			return this;
		}

#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
		var result = (ServiceJsonRpcDescriptor)base.WithMultiplexingStream(null);
#pragma warning restore CS0618 // Type or member is obsolete
		if (this == result)
		{
			// We got this far without cloning. But we must clone because we're about to set a property on the result.
			result = (ServiceJsonRpcDescriptor)result.Clone();
		}

		result.MultiplexingStreamOptions = multiplexingStreamOptions?.GetFrozenCopy();
		return result;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcDescriptor"/> that resembles this one,
	/// but with the <see cref="ExceptionStrategy" /> property set to a new value.
	/// </summary>
	/// <param name="exceptionStrategy">The new value for the <see cref="ExceptionStrategy"/> property.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceJsonRpcDescriptor WithExceptionStrategy(ExceptionProcessing exceptionStrategy)
	{
		if (this.ExceptionStrategy == exceptionStrategy)
		{
			return this;
		}

		var result = (ServiceJsonRpcDescriptor)this.Clone();
		result.ExceptionStrategy = exceptionStrategy;
		return result;
	}

	/// <inheritdoc />
	public bool Equals(ServiceJsonRpcDescriptor? other)
	{
		return other != null
			&& this.Moniker.Equals(other.Moniker)
			&& this.Formatter == other.Formatter
			&& this.MessageDelimiter == other.MessageDelimiter;
	}

	/// <inheritdoc />
	public override int GetHashCode() => this.Moniker.GetHashCode();

	/// <inheritdoc/>
	public override RpcConnection ConstructRpcConnection(IDuplexPipe pipe)
	{
		Requires.NotNull(pipe, nameof(pipe));

		if (this.MultiplexingStream is null && this.MultiplexingStreamOptions is object)
		{
			this.MultiplexingStreamOptions = this.CreateSeedChannels();
			var mxstream = MultiplexingStream.Create(pipe.AsStream(), this.MultiplexingStreamOptions);

			MultiplexingStream.Channel rpcChannel = mxstream.AcceptChannel(0); // accepting the seeded channelId = 0 only.
			rpcChannel.Completion.ContinueWith(_ => mxstream.DisposeAsync().Forget(), TaskScheduler.Default).Forget();

#pragma warning disable CS0618 // Type or member is obsolete
			return this.WithMultiplexingStream(mxstream).ConstructRpcConnection(rpcChannel);
#pragma warning restore CS0618 // Type or member is obsolete
		}

		IJsonRpcMessageHandler handler = this.CreateHandler(pipe, this.CreateFormatter());
		JsonRpc jsonRpc = this.CreateJsonRpc(handler);
		jsonRpc.ExceptionStrategy = this.ExceptionStrategy;
		jsonRpc.ActivityTracingStrategy = new CorrelationManagerTracingStrategy { TraceSource = this.TraceSource };
		jsonRpc.SynchronizationContext = new NonConcurrentSynchronizationContext(sticky: false);

		// Only set the TraceSource if we've been given one, so we don't set it to null (which would throw).
		if (this.TraceSource != null)
		{
			jsonRpc.TraceSource = this.TraceSource;
		}

		return this.CreateConnection(jsonRpc);
	}

	/// <inheritdoc />
	protected override ServiceRpcDescriptor Clone() => new ServiceJsonRpcDescriptor(this);

	/// <summary>
	/// Initializes a new instance of a <see cref="JsonRpcConnection"/> or derived type.
	/// </summary>
	/// <param name="jsonRpc">The <see cref="JsonRpc"/> object that will have to be passed to <see cref="JsonRpcConnection(JsonRpc)"/>.</param>
	/// <returns>The new instance of <see cref="JsonRpcConnection"/>.</returns>
	protected virtual JsonRpcConnection CreateConnection(JsonRpc jsonRpc) => new JsonRpcConnection(jsonRpc);

	/// <summary>
	/// Initializes a new instance of <see cref="IJsonRpcMessageHandler"/> for use in a new server or client.
	/// </summary>
	/// <param name="pipe">The pipe the handler should use to send and receive messages.</param>
	/// <param name="formatter">The <see cref="IJsonRpcMessageFormatter"/> the handler should use to encode messages.</param>
	/// <returns>The new message handler.</returns>
	protected virtual IJsonRpcMessageHandler CreateHandler(IDuplexPipe pipe, IJsonRpcMessageFormatter formatter)
	{
		Requires.NotNull(pipe, nameof(pipe));

		IJsonRpcMessageHandler handler;
		switch (this.MessageDelimiter)
		{
			case MessageDelimiters.BigEndianInt32LengthHeader:
				handler = new LengthHeaderMessageHandler(pipe, formatter);
				break;
			case MessageDelimiters.HttpLikeHeaders:
				handler = new HeaderDelimitedMessageHandler(pipe, formatter);
				break;
			default:
				throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.MessageDelimiterNotSupported, this.MessageDelimiter, this.Protocol));
		}

		return handler;
	}

	/// <summary>
	/// Initializes a new instance of <see cref="JsonRpc"/> for use in a new server or client.
	/// </summary>
	/// <param name="handler">The message handler that the <see cref="JsonRpc"/> instance should use.</param>
	/// <returns>The new <see cref="JsonRpc"/>.</returns>
	protected virtual JsonRpc CreateJsonRpc(IJsonRpcMessageHandler handler)
	{
		Requires.NotNull(handler, nameof(handler));

		return new JsonRpc(handler);
	}

	/// <summary>
	/// Initializes a new instance of <see cref="IJsonRpcMessageFormatter"/> for use in a new server or client.
	/// </summary>
	/// <returns>The new message formatter.</returns>
	protected virtual IJsonRpcMessageFormatter CreateFormatter()
	{
		switch (this.Formatter)
		{
			case Formatters.UTF8:
				return this.CreateJsonFormatter();
			case Formatters.MessagePack:
				return this.CreateMessagePackFormatter();
			default:
				throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.FormatterNotSupported, this.Formatter, this.Protocol));
		}
	}

	private IJsonRpcMessageFormatter CreateMessagePackFormatter()
	{
		return new MessagePackFormatter
		{
			MultiplexingStream = this.MultiplexingStream,
		};
	}

	/// <summary>
	/// Create first seed channel if not being assigned by the owner of the service descriptor.
	/// Sets the protocol version to be used. 1 is the original. 2 is a
	/// protocol breaking change backpressure support, 3 is a protocol breaking change and default version that
	/// removes the initial handshake so no round-trip to establish the connection is necessary.
	/// </summary>
	/// <returns>The options that a Nerdbank.Streams.MultiplexingStream may be created.</returns>
	private MultiplexingStream.Options? CreateSeedChannels()
	{
		Requires.Argument(this.MultiplexingStreamOptions?.ProtocolMajorVersion > 2, nameof(MultiplexingStream.Options.ProtocolMajorVersion), $"Should be greater than 2.");

		if (this.MultiplexingStreamOptions.SeededChannels.Count == 0)
		{
			return new MultiplexingStream.Options(this.MultiplexingStreamOptions)
			{
				SeededChannels =
					{
						new MultiplexingStream.ChannelOptions { }, // Channel 0
					},
				ProtocolMajorVersion = 3, // Removes initial handshake if a protocol version of 3 or later is specified.
			};
		}
		else
		{
			return this.MultiplexingStreamOptions;
		}
	}

	private IJsonRpcMessageFormatter CreateJsonFormatter()
	{
		return new JsonMessageFormatter
		{
			MultiplexingStream = this.MultiplexingStream,
			JsonSerializer =
					{
						ContractResolver = new CamelCasePropertyNamesContractResolver
						{
							NamingStrategy = new CamelCaseNamingStrategy(processDictionaryKeys: false, overrideSpecifiedNames: true),
						},
					},
		};
	}

	/// <summary>
	/// A <see cref="ServiceRpcDescriptor.RpcConnection"/>-derived type specifically for <see cref="JsonRpc"/>.
	/// </summary>
	public class JsonRpcConnection : RpcConnection
	{
		/// <summary>
		/// Backing field for the <see cref="LocalRpcTargetOptions"/> property.
		/// </summary>
		/// <devremarks>
		/// Create a new instance of <see cref="JsonRpcTargetOptions"/> every time because it's mutable.
		/// </devremarks>
		private JsonRpcTargetOptions localRpcTargetOptions = new JsonRpcTargetOptions { DisposeOnDisconnect = true };

		/// <summary>
		/// Backing field for the <see cref="LocalRpcProxyOptions"/> property.
		/// </summary>
		/// <devremarks>
		/// Create a new instance of <see cref="JsonRpcProxyOptions"/> every time because it's mutable.
		/// </devremarks>
		private JsonRpcProxyOptions localRpcProxyOptions = new JsonRpcProxyOptions { };

		/// <summary>
		/// Initializes a new instance of the <see cref="JsonRpcConnection"/> class.
		/// </summary>
		/// <param name="jsonRpc">The JSON-RPC object.</param>
		public JsonRpcConnection(JsonRpc jsonRpc)
		{
			this.JsonRpc = jsonRpc ?? throw new ArgumentNullException(nameof(jsonRpc));
		}

		/// <inheritdoc/>
		public override TraceSource TraceSource
		{
			get => this.JsonRpc.TraceSource;
			set => this.JsonRpc.TraceSource = value;
		}

		/// <inheritdoc/>
		public override bool IsDisposed => this.JsonRpc.IsDisposed;

		/// <summary>
		/// Gets or sets the options to pass to <see cref="JsonRpc.AddLocalRpcTarget(object, JsonRpcTargetOptions?)"/> in the default implementation of <see cref="AddLocalRpcTarget(object)"/>.
		/// </summary>
		public JsonRpcTargetOptions LocalRpcTargetOptions
		{
			get => this.localRpcTargetOptions;
			set
			{
				Requires.NotNull(value, nameof(value));
				this.localRpcTargetOptions = value;
			}
		}

		/// <summary>
		/// Gets or sets the options to pass to <see cref="JsonRpc.Attach{T}(JsonRpcProxyOptions?)"/> in the default implementation of <see cref="ConstructRpcClient{T}()"/>.
		/// </summary>
		public JsonRpcProxyOptions LocalRpcProxyOptions
		{
			get => this.localRpcProxyOptions;
			set
			{
				Requires.NotNull(value, nameof(value));
				this.localRpcProxyOptions = value;
			}
		}

		/// <summary>
		/// Gets the underlying <see cref="JsonRpc"/> object.
		/// </summary>
		public JsonRpc JsonRpc { get; }

		/// <inheritdoc/>
		public override Task Completion => this.JsonRpc.Completion;

		/// <inheritdoc/>
		public override void AddLocalRpcTarget(object rpcTarget)
		{
			Requires.NotNull(rpcTarget, nameof(rpcTarget));

			this.JsonRpc.AddLocalRpcTarget(rpcTarget, this.LocalRpcTargetOptions);
		}

		/// <inheritdoc/>
		public override T ConstructRpcClient<T>() => this.JsonRpc.Attach<T>(this.LocalRpcProxyOptions);

		/// <inheritdoc/>
		public override void Dispose() => this.JsonRpc.Dispose();

		/// <inheritdoc/>
		public override void StartListening() => this.JsonRpc.StartListening();
	}
}
