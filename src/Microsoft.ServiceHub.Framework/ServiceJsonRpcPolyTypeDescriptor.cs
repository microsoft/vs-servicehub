// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable PolyTypeJson

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using PolyType;
using StreamJsonRpc;
using StreamJsonRpc.Reflection;
using STJ = System.Text.Json;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An RPC descriptor for services that support JSON-RPC via StreamJsonRpc using a PolyType-based formatter.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public class ServiceJsonRpcPolyTypeDescriptor : ServiceRpcDescriptor, IEquatable<ServiceJsonRpcPolyTypeDescriptor>
{
	/// <inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, ImmutableArray{ITypeShape}, ImmutableArray{ITypeShape}, Formatters, MessageDelimiters, MultiplexingStream.Options?)" />
	public ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker serviceMoniker, ITypeShape serviceRpcContracts, Formatters formatter = Formatters.NerdbankMessagePack, MessageDelimiters messageDelimiter = MessageDelimiters.BigEndianInt32LengthHeader)
		: this(serviceMoniker, [serviceRpcContracts], [], formatter, messageDelimiter, multiplexingStreamOptions: null)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class
	/// with support for opening additional streams with <see cref="ServiceJsonRpcPolyTypeDescriptor.MultiplexingStreamOptions"/>.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="formatter">The formatter to use for the JSON-RPC message.</param>
	/// <param name="messageDelimiter">The message delimiter scheme to use.</param>
	/// <param name="multiplexingStreamOptions">The options that a <see cref="MultiplexingStream" /> may be created with. A <see langword="null"/> value will prevent a <see cref="MultiplexingStream" /> from being created for the RPC connection.</param>
	/// <param name="serviceRpcContracts">The RPC contracts the service implements.</param>
	/// <param name="clientRpcContracts">The RPC contracts that the client callback object implements.</param>
	public ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker serviceMoniker, ImmutableArray<ITypeShape> serviceRpcContracts, ImmutableArray<ITypeShape> clientRpcContracts, Formatters formatter = Formatters.NerdbankMessagePack, MessageDelimiters messageDelimiter = MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStream.Options? multiplexingStreamOptions = null)
		: base(serviceMoniker, clientRpcContracts.FirstOrDefault()?.Type)
	{
		if (FindErrorsInServiceContracts(ref serviceRpcContracts) is string serviceError)
		{
			throw new ArgumentException(serviceError, nameof(serviceRpcContracts));
		}

		if (FindErrorsInServiceContracts(ref clientRpcContracts) is string clientError)
		{
			throw new ArgumentException(clientError, nameof(clientRpcContracts));
		}

		Requires.Argument(!serviceRpcContracts.IsEmpty, nameof(serviceRpcContracts), Strings.AtLeastOneRequired);

		this.Formatter = formatter;
		this.MessageDelimiter = messageDelimiter;
		this.MultiplexingStreamOptions = multiplexingStreamOptions?.GetFrozenCopy();
		this.ServiceRpcContracts = serviceRpcContracts;
		this.ClientRpcContracts = clientRpcContracts;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class and initializes all fields based on a template instance.
	/// </summary>
	/// <param name="copyFrom">The instance to copy all fields from.</param>
	protected ServiceJsonRpcPolyTypeDescriptor([ValidatedNotNull] ServiceJsonRpcPolyTypeDescriptor copyFrom)
		: base(copyFrom)
	{
		this.Formatter = copyFrom.Formatter;
		this.MessageDelimiter = copyFrom.MessageDelimiter;
		this.MultiplexingStreamOptions = copyFrom.MultiplexingStreamOptions;
		this.ExceptionStrategy = copyFrom.ExceptionStrategy;
		this.ServiceRpcContracts = copyFrom.ServiceRpcContracts;
		this.ClientRpcContracts = copyFrom.ClientRpcContracts;
	}

	/// <summary>
	/// The formats that JSON-RPC can be serialized to.
	/// </summary>
	public enum Formatters
	{
		/// <summary>
		/// Format messages as MessagePack for a high throughput, compact binary representation
		/// using <see cref="NerdbankMessagePackFormatter"/> (the <see href="https://github.com/AArnott/Nerdbank.MessagePack">Nerdbank.MessagePack serializer</see>).
		/// </summary>
		NerdbankMessagePack,

		/// <summary>
		/// Format messages with UTF-8 text for a human readable JSON representation using the <see cref="System.Text.Json.JsonSerializer"/> serializer.
		/// </summary>
		[Experimental("PolyTypeJson")]
		UTF8,
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
	/// Gets a collection of <see cref="ITypeShape"/> where each describes an RPC contract for use when calling <see cref="JsonRpcConnection.AddLocalRpcTarget(object)"/>
	/// with a service object.
	/// </summary>
	/// <remarks>
	/// The RPC target object passed to <see cref="JsonRpcConnection.AddLocalRpcTarget(object)"/> is expected to be assignable to the type designated
	/// by the <see cref="RpcTargetMetadata.TargetType"/> property of all elements of this collection.
	/// </remarks>
	public ImmutableArray<ITypeShape> ServiceRpcContracts { get; private set; } = [];

	/// <summary>
	/// Gets a collection of <see cref="ITypeShape"/> where each describes an RPC contract that <em>may</em> be added by the user or <see cref="IBrokeredServiceContainer"/>
	/// to the <see cref="ServiceRpcContracts"/> collection before establishing the RPC connection,
	/// based on service registration data when calling <see cref="JsonRpcConnection.AddLocalRpcTarget(object)"/> with a service object.
	/// </summary>
	public ImmutableArray<ITypeShape> OptionalServiceRpcContracts { get; private set; } = [];

	/// <summary>
	/// Gets a collection of <see cref="ITypeShape"/> where each describes an RPC contract for use when calling <see cref="JsonRpcConnection.AddClientLocalRpcTarget(object)"/>
	/// with a client callback object.
	/// </summary>
	/// <remarks>
	/// The RPC target object passed to <see cref="JsonRpcConnection.AddLocalRpcTarget(object)"/> is expected to be assignable to the type designated
	/// by the <see cref="RpcTargetMetadata.TargetType"/> property of all elements of this collection.
	/// </remarks>
	public ImmutableArray<ITypeShape> ClientRpcContracts { get; private set; } = [];

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

	private ITypeShapeProvider TypeShapeProvider => this.ServiceRpcContracts.First().Provider;

#if NET
	/// <inheritdoc cref="ServiceJsonRpcPolyTypeDescriptorExtensions.Create{TService}(ServiceMoniker, Formatters, MessageDelimiters, MultiplexingStream.Options?)"/>
	public static ServiceJsonRpcPolyTypeDescriptor Create<TService>(ServiceMoniker serviceMoniker, Formatters formatter = Formatters.NerdbankMessagePack, MessageDelimiters messageDelimiter = MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStream.Options? multiplexingStreamOptions = null)
		where TService : IShapeable<TService> => new(serviceMoniker, [TService.GetTypeShape()], [], formatter, messageDelimiter, multiplexingStreamOptions);

	/// <inheritdoc cref="ServiceJsonRpcPolyTypeDescriptorExtensions.Create{TService, TProvider}(ServiceMoniker, Formatters, MessageDelimiters, MultiplexingStream.Options?)"/>
	public static ServiceJsonRpcPolyTypeDescriptor Create<TService, TProvider>(ServiceMoniker serviceMoniker, Formatters formatter = Formatters.NerdbankMessagePack, MessageDelimiters messageDelimiter = MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStream.Options? multiplexingStreamOptions = null)
		where TProvider : IShapeable<TService> => new(serviceMoniker, [TProvider.GetTypeShape()], [], formatter, messageDelimiter, multiplexingStreamOptions);
#endif

	/// <inheritdoc/>
#pragma warning disable CS0672 // Base Member overrides obsolete member, To be handled at ServiceJsonRpcPolyTypeDescriptor only later for backward compatibility.
	public override ServiceRpcDescriptor WithMultiplexingStream(MultiplexingStream? multiplexingStream)
#pragma warning restore CS0672 // Base Member overrides obsolete member
	{
#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
		ServiceRpcDescriptor result = base.WithMultiplexingStream(multiplexingStream);
#pragma warning restore CS0618 // Type or member is obsolete

		if (result is ServiceJsonRpcPolyTypeDescriptor serviceJsonRpcDescriptor)
		{
			if (serviceJsonRpcDescriptor.MultiplexingStreamOptions is null)
			{
				return result;
			}

			result = serviceJsonRpcDescriptor.Clone();
			((ServiceJsonRpcPolyTypeDescriptor)result).MultiplexingStreamOptions = null;
		}

		return result;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
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
		var result = (ServiceJsonRpcPolyTypeDescriptor)base.WithMultiplexingStream(null);
#pragma warning restore CS0618 // Type or member is obsolete
		if (this == result)
		{
			// We got this far without cloning. But we must clone because we're about to set a property on the result.
			result = (ServiceJsonRpcPolyTypeDescriptor)result.Clone();
		}

		result.MultiplexingStreamOptions = multiplexingStreamOptions?.GetFrozenCopy();
		return result;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
	/// but with the <see cref="ExceptionStrategy" /> property set to a new value.
	/// </summary>
	/// <param name="exceptionStrategy">The new value for the <see cref="ExceptionStrategy"/> property.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceJsonRpcPolyTypeDescriptor WithExceptionStrategy(ExceptionProcessing exceptionStrategy)
	{
		if (this.ExceptionStrategy == exceptionStrategy)
		{
			return this;
		}

		var result = (ServiceJsonRpcPolyTypeDescriptor)this.Clone();
		result.ExceptionStrategy = exceptionStrategy;
		return result;
	}

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
	/// This proxy implements <typeparamref name="T"/> and any interfaces specified in the <see cref="ServiceRpcContracts"/> property.
	/// The proxy also implements <see cref="IDisposable"/> and will forward a call to <see cref="IDisposable.Dispose"/>
	/// to the <paramref name="target"/> object if the target object implements <see cref="IDisposable"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">The <typeparamref name="T"/> type is not among the interfaces specified in the <see cref="ServiceRpcContracts"/> property.</exception>
	[return: NotNullIfNotNull("target")]
	public override T? ConstructLocalProxy<T>(T? target)
		where T : class
	{
		if (target is null)
		{
			return null;
		}

		if (!typeof(T).IsInterface)
		{
			throw new NotSupportedException(Strings.ClientProxyTypeArgumentMustBeAnInterface);
		}

		// Make sure that the T type appears as a service interface.
		Requires.Argument(this.ServiceRpcContracts.Any(shape => shape.Type == typeof(T)), nameof(T), Strings.FormatClientProxyTypeArgumentMustBeAmongServiceInterfaces(typeof(T).FullName));

		Reflection.ProxyInputs localProxyInputs = new()
		{
			ContractInterface = this.ServiceRpcContracts.First().Type,
			AdditionalContractInterfaces = this.ServiceRpcContracts[1..].Select(s => s.Type).ToArray(),
			ExceptionStrategy = this.ExceptionStrategy,
		};

		return (T)Reflection.ProxyBase.CreateProxy(target, localProxyInputs, true);
	}

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
		jsonRpc.JoinableTaskFactory = this.JoinableTaskFactory;

		// Only set the TraceSource if we've been given one, so we don't set it to null (which would throw).
		if (this.TraceSource != null)
		{
			jsonRpc.TraceSource = this.TraceSource;
		}

		return this.CreateConnection(jsonRpc);
	}

	/// <inheritdoc />
	public bool Equals(ServiceJsonRpcPolyTypeDescriptor? other)
	{
		return other != null
			&& this.Moniker.Equals(other.Moniker)
			&& this.Formatter == other.Formatter
			&& this.MessageDelimiter == other.MessageDelimiter
			&& this.ServiceRpcContracts.SequenceEqual(other.ServiceRpcContracts)
			&& this.ClientRpcContracts.SequenceEqual(other.ClientRpcContracts);
	}

	/// <inheritdoc />
	public override int GetHashCode() => this.Moniker.GetHashCode();

	/// <inheritdoc />
	public override bool Equals(object? obj) => this.Equals(obj as ServiceJsonRpcPolyTypeDescriptor);

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
	/// but with the <see cref="ServiceRpcContracts" /> property set to a new value.
	/// </summary>
	/// <param name="value">The new value for the <see cref="ServiceRpcContracts"/> property. Must have at least one element.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceJsonRpcPolyTypeDescriptor WithServiceRpcContracts(params ImmutableArray<ITypeShape> value)
	{
		if (this.ServiceRpcContracts == value)
		{
			return this;
		}

		if (FindErrorsInServiceContracts(ref value) is string serviceError)
		{
			throw new ArgumentException(serviceError, nameof(value));
		}

		Requires.Argument(!value.IsEmpty, nameof(value), Strings.AtLeastOneRequired);

		var copy = (ServiceJsonRpcPolyTypeDescriptor)this.Clone();
		copy.ServiceRpcContracts = value;
		return copy;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
	/// but with the <see cref="OptionalServiceRpcContracts" /> property set to a new value.
	/// </summary>
	/// <param name="value">The new value for the <see cref="OptionalServiceRpcContracts"/> property.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceJsonRpcPolyTypeDescriptor WithOptionalServiceRpcContracts(params ImmutableArray<ITypeShape> value)
	{
		if (this.OptionalServiceRpcContracts == value)
		{
			return this;
		}

		if (FindErrorsInServiceContracts(ref value) is string serviceError)
		{
			throw new ArgumentException(serviceError, nameof(value));
		}

		var copy = (ServiceJsonRpcPolyTypeDescriptor)this.Clone();
		copy.OptionalServiceRpcContracts = value;
		return copy;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
	/// but with the <see cref="ClientRpcContracts" /> property set to a new value.
	/// </summary>
	/// <param name="value">The new value for the <see cref="ClientRpcContracts"/> property.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceJsonRpcPolyTypeDescriptor WithClientRpcContracts(params ImmutableArray<ITypeShape> value)
	{
		Requires.Argument(!value.IsDefault, nameof(value), Strings.NotInitialized);

		if (this.ClientRpcContracts == value)
		{
			return this;
		}

		var copy = (ServiceJsonRpcPolyTypeDescriptor)this.Clone();
		copy.ClientRpcContracts = value;
		return copy;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
	/// but with <see cref="ExceptionStrategy"/>, <see cref="MultiplexingStreamOptions"/>, <see cref="JoinableTaskFactory"/> and <see cref="TraceSource"/>
	/// altered to match a given descriptor.
	/// </summary>
	/// <param name="copyFrom">The descriptor to copy settings from.</param>
	/// <returns>A clone of this instance, with the properties changed or this same instance if the properties already match.</returns>
	internal ServiceJsonRpcPolyTypeDescriptor WithSettingsFrom(ServiceJsonRpcPolyTypeDescriptor copyFrom)
	{
		if (this.ExceptionStrategy == copyFrom.ExceptionStrategy &&
			this.MultiplexingStreamOptions == copyFrom.MultiplexingStreamOptions &&
			this.MultiplexingStream == copyFrom.MultiplexingStream &&
			this.JoinableTaskFactory == copyFrom.JoinableTaskFactory &&
			this.TraceSource == copyFrom.TraceSource)
		{
			return this;
		}

		var result = (ServiceJsonRpcPolyTypeDescriptor)this.Clone();
		result.ExceptionStrategy = copyFrom.ExceptionStrategy;

		if (copyFrom.MultiplexingStreamOptions is not null)
		{
#pragma warning disable CS0618 // Type or member is obsolete
			result = (ServiceJsonRpcPolyTypeDescriptor)result.WithMultiplexingStream((MultiplexingStream?)null);
			result.MultiplexingStreamOptions = copyFrom.MultiplexingStreamOptions.GetFrozenCopy();
		}
		else
		{
			result = (ServiceJsonRpcPolyTypeDescriptor)result.WithMultiplexingStream(copyFrom.MultiplexingStream);
#pragma warning restore CS0618 // Type or member is obsolete
		}

		result = (ServiceJsonRpcPolyTypeDescriptor)result.WithJoinableTaskFactory(copyFrom.JoinableTaskFactory);
		result = (ServiceJsonRpcPolyTypeDescriptor)result.WithTraceSource(copyFrom.TraceSource);

		return result;
	}

	/// <summary>
	/// Initializes a new instance of a <see cref="JsonRpcConnection"/> or derived type.
	/// </summary>
	/// <param name="jsonRpc">The <see cref="JsonRpc"/> object that will have to be passed to <see cref="JsonRpcConnection(JsonRpc, ServiceJsonRpcPolyTypeDescriptor)"/>.</param>
	/// <returns>The new instance of <see cref="JsonRpcConnection"/>.</returns>
	protected internal virtual JsonRpcConnection CreateConnection(JsonRpc jsonRpc) => new JsonRpcConnection(jsonRpc, this);

	/// <summary>
	/// Initializes a new instance of <see cref="IJsonRpcMessageHandler"/> for use in a new server or client.
	/// </summary>
	/// <param name="pipe">The pipe the handler should use to send and receive messages.</param>
	/// <param name="formatter">The <see cref="IJsonRpcMessageFormatter"/> the handler should use to encode messages.</param>
	/// <returns>The new message handler.</returns>
	protected internal virtual IJsonRpcMessageHandler CreateHandler(IDuplexPipe pipe, IJsonRpcMessageFormatter formatter)
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
				throw new NotSupportedException(Strings.FormatMessageDelimiterNotSupported(this.MessageDelimiter, this.Protocol));
		}

		return handler;
	}

	/// <summary>
	/// Initializes a new instance of <see cref="IJsonRpcMessageFormatter"/> for use in a new server or client.
	/// </summary>
	/// <returns>The new message formatter.</returns>
	protected internal virtual IJsonRpcMessageFormatter CreateFormatter()
		=> this.Formatter switch
		{
			Formatters.NerdbankMessagePack => this.CreateNerdbankMessagePackFormatter(this.TypeShapeProvider),
			Formatters.UTF8 => this.CreatePolyTypeJsonFormatter(this.TypeShapeProvider),
			_ => throw new NotSupportedException(Strings.FormatFormatterNotSupported(this.Formatter, this.Protocol)),
		};

	/// <summary>
	/// Initializes a new instance of <see cref="JsonRpc"/> for use in a new server or client.
	/// </summary>
	/// <param name="handler">The message handler that the <see cref="JsonRpc"/> instance should use.</param>
	/// <returns>The new <see cref="JsonRpc"/>.</returns>
	protected internal virtual JsonRpc CreateJsonRpc(IJsonRpcMessageHandler handler)
	{
		Requires.NotNull(handler, nameof(handler));

		return new JsonRpc(handler);
	}

	/// <inheritdoc/>
	protected override ServiceRpcDescriptor Clone() => new ServiceJsonRpcPolyTypeDescriptor(this);

	private static string? FindErrorsInServiceContracts(ref ImmutableArray<ITypeShape> contracts)
	{
		if (contracts.IsDefault)
		{
			return Strings.NotInitialized;
		}

		HashSet<ITypeShape> set = [];
		bool duplicatesFound = false;
		foreach (ITypeShape typeShape in contracts)
		{
			if (!typeShape.Type.IsInterface)
			{
				return Strings.FormatClientProxyTypeArgumentMustBeAnInterface(typeShape.Type.FullName);
			}

			if (!set.Add(typeShape))
			{
				duplicatesFound = true;
			}
		}

		if (duplicatesFound)
		{
			contracts = [.. set];
		}

		return null;
	}

	private IJsonRpcMessageFormatter CreateNerdbankMessagePackFormatter(ITypeShapeProvider provider) => new NerdbankMessagePackFormatter { TypeShapeProvider = provider };

	private IJsonRpcMessageFormatter CreatePolyTypeJsonFormatter(ITypeShapeProvider provider)
	{
		return new PolyTypeJsonFormatter
		{
			TypeShapeProvider = provider,
			MultiplexingStream = this.MultiplexingStream,
			JsonSerializerOptions =
			{
				DictionaryKeyPolicy = null,
				PropertyNamingPolicy = STJ.JsonNamingPolicy.CamelCase,
				DefaultIgnoreCondition = STJ.Serialization.JsonIgnoreCondition.WhenWritingNull,
			},
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

	/// <summary>
	/// A <see cref="ServiceRpcDescriptor.RpcConnection"/>-derived type specifically for <see cref="JsonRpc"/>.
	/// </summary>
	public class JsonRpcConnection : RpcConnection
	{
		private readonly ServiceJsonRpcPolyTypeDescriptor owner;

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
		private JsonRpcProxyOptions localRpcProxyOptions = new JsonRpcProxyOptions { AcceptProxyWithExtraInterfaces = true };

		/// <summary>
		/// Initializes a new instance of the <see cref="JsonRpcConnection"/> class.
		/// </summary>
		/// <param name="jsonRpc">The JSON-RPC object.</param>
		/// <param name="owner">The descriptor that created this object.</param>
		public JsonRpcConnection(JsonRpc jsonRpc, ServiceJsonRpcPolyTypeDescriptor owner)
		{
			this.JsonRpc = jsonRpc ?? throw new ArgumentNullException(nameof(jsonRpc));
			this.owner = owner;
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
		/// <value>The default value has <see cref="JsonRpcProxyOptions.AcceptProxyWithExtraInterfaces"/> set to <see langword="true" />.</value>
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

			Type rpcTargetType = rpcTarget.GetType();

			foreach (ITypeShape typeShape in this.owner.ServiceRpcContracts)
			{
				Requires.Argument(typeShape.Type.IsAssignableFrom(rpcTargetType), nameof(rpcTarget), $"RPC target type must be assignable to all {nameof(ServiceJsonRpcPolyTypeDescriptor.ServiceRpcContracts)} elements.");
			}

			foreach (ITypeShape typeShape in this.owner.ServiceRpcContracts)
			{
				this.JsonRpc.AddLocalRpcTarget(RpcTargetMetadata.FromShape(typeShape), rpcTarget, this.LocalRpcTargetOptions);
			}
		}

		/// <inheritdoc/>
		public override void AddClientLocalRpcTarget(object rpcTarget)
		{
			Requires.NotNull(rpcTarget, nameof(rpcTarget));

			Type rpcTargetType = rpcTarget.GetType();

			foreach (ITypeShape typeShape in this.owner.ClientRpcContracts)
			{
				Requires.Argument(typeShape.Type.IsAssignableFrom(rpcTargetType), nameof(rpcTarget), $"RPC target type must be assignable to all {nameof(ServiceJsonRpcPolyTypeDescriptor.ClientRpcContracts)} elements.");
			}

			foreach (ITypeShape typeShape in this.owner.ClientRpcContracts)
			{
				this.JsonRpc.AddLocalRpcTarget(RpcTargetMetadata.FromShape(typeShape), rpcTarget, this.LocalRpcTargetOptions);
			}
		}

		/// <inheritdoc/>
		public override T ConstructRpcClient<T>() => (T)this.ConstructRpcClient(typeof(T));

		/// <inheritdoc/>
		public override object ConstructRpcClient(Type interfaceType)
		{
			Requires.NotNull(interfaceType, nameof(interfaceType));

			if (!interfaceType.IsInterface)
			{
				throw new NotSupportedException(Strings.ClientProxyTypeArgumentMustBeAnInterface);
			}

			Requires.Argument(this.owner.ServiceRpcContracts.Any(shape => shape.Type == interfaceType), nameof(interfaceType), Strings.FormatClientProxyTypeArgumentMustBeAmongServiceInterfaces(interfaceType.FullName));

			ProxyInputs proxyInputs = new()
			{
				ContractInterface = this.owner.ServiceRpcContracts[0].Type,
				AdditionalContractInterfaces = this.owner.ServiceRpcContracts[1..].Select(c => c.Type).ToArray(),
				Options = this.LocalRpcProxyOptions,
			};

			return ProxyBase.CreateProxy(this.JsonRpc, proxyInputs, startOrFail: false);
		}

		/// <inheritdoc/>
		public override void Dispose()
		{
			GC.SuppressFinalize(this);
			this.JsonRpc.Dispose();
		}

		/// <inheritdoc/>
		public override void StartListening() => this.JsonRpc.StartListening();
	}
}
