// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Nerdbank.Streams;
using PolyType;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An RPC descriptor for services that support JSON-RPC using a PolyType-based formatter.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public class ServiceJsonRpcPolyTypeDescriptor : ServiceJsonRpcDescriptor
{
	/// <inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, Type?, Formatters, MessageDelimiters, ITypeShapeProvider)" />
	public ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter, ITypeShapeProvider typeShapeProvider)
		: this(serviceMoniker, clientInterface: null, formatter, messageDelimiter, typeShapeProvider)
	{
		Requires.NotNull(typeShapeProvider);
		this.TypeShapeProvider = typeShapeProvider;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class
	/// with no support for opening additional streams except by relying on the underlying service broker to provide one.
	/// </summary>
	/// <inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, Type?, Formatters, MessageDelimiters, MultiplexingStream.Options?, ITypeShapeProvider)" />
	public ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface, Formatters formatter, MessageDelimiters messageDelimiter, [ValidatedNotNull] ITypeShapeProvider typeShapeProvider)
		: base(serviceMoniker, clientInterface, formatter, messageDelimiter)
	{
		Requires.NotNull(typeShapeProvider);
		this.TypeShapeProvider = typeShapeProvider;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class
	/// with support for opening additional streams with <see cref="ServiceJsonRpcDescriptor.MultiplexingStreamOptions"/>.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="clientInterface">The interface type that the client's "callback" RPC target is expected to implement. May be null if the service does not invoke methods on the client.</param>
	/// <param name="formatter">The formatter to use for the JSON-RPC message.</param>
	/// <param name="messageDelimiter">The message delimiter scheme to use.</param>
	/// <param name="multiplexingStreamOptions">The options that a <see cref="MultiplexingStream" /> may be created with. A <see langword="null"/> value will prevent a <see cref="MultiplexingStream" /> from being created for the RPC connection.</param>
	/// <param name="typeShapeProvider">The source of type shapes for all parameter and return types used in the RPC contract.</param>
	public ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface, Formatters formatter, MessageDelimiters messageDelimiter, MultiplexingStream.Options? multiplexingStreamOptions, [ValidatedNotNull] ITypeShapeProvider typeShapeProvider)
		: base(serviceMoniker, clientInterface, formatter, messageDelimiter, multiplexingStreamOptions)
	{
		Requires.NotNull(typeShapeProvider);
		this.TypeShapeProvider = typeShapeProvider;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class and initializes all fields based on a template instance.
	/// </summary>
	/// <param name="copyFrom">The instance to copy all fields from.</param>
	protected ServiceJsonRpcPolyTypeDescriptor([ValidatedNotNull] ServiceJsonRpcPolyTypeDescriptor copyFrom)
		: base(copyFrom)
	{
		this.TypeShapeProvider = copyFrom.TypeShapeProvider;
	}

	/// <summary>
	/// Gets the <see cref="ITypeShapeProvider"/> to use for serializing and deserializing parameter and return types.
	/// </summary>
	public ITypeShapeProvider TypeShapeProvider { get; private set; }

	/// <summary>
	/// Returns an instance of <see cref="ServiceJsonRpcPolyTypeDescriptor"/> that resembles this one,
	/// but with the <see cref="TypeShapeProvider" /> property set to a new value.
	/// </summary>
	/// <param name="value">The new value for the <see cref="TypeShapeProvider"/> property.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceJsonRpcPolyTypeDescriptor WithTypeShapeProvider(ITypeShapeProvider value)
	{
		Requires.NotNull(value);

		if (this.TypeShapeProvider == value)
		{
			return this;
		}

		var copy = (ServiceJsonRpcPolyTypeDescriptor)this.Clone();
		copy.TypeShapeProvider = value;
		return copy;
	}

	/// <inheritdoc/>
	protected internal override IJsonRpcMessageFormatter CreateFormatter()
		=> this.Formatter switch
		{
			Formatters.NerdbankMessagePack => this.CreateNerdbankMessagePackFormatter(this.TypeShapeProvider),
			_ => throw new NotSupportedException(Strings.FormatFormatterNotSupported(this.Formatter, this.Protocol)),
		};

	/// <inheritdoc/>
	protected override ServiceRpcDescriptor Clone() => new ServiceJsonRpcPolyTypeDescriptor(this) { TypeShapeProvider = this.TypeShapeProvider };

	private IJsonRpcMessageFormatter CreateNerdbankMessagePackFormatter(ITypeShapeProvider provider) => new NerdbankMessagePackFormatter { TypeShapeProvider = provider };
}
