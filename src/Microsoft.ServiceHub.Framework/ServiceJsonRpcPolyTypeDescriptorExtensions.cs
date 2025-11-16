// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using Nerdbank.Streams;
using PolyType;
using PolyType.Abstractions;
using static Microsoft.ServiceHub.Framework.ServiceJsonRpcPolyTypeDescriptor;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Extension members for the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class.
/// </summary>
public static class ServiceJsonRpcPolyTypeDescriptorExtensions
{
	extension(ServiceJsonRpcPolyTypeDescriptor)
	{
		/// <inheritdoc cref="Create{TService, TProvider}(ServiceMoniker, Formatters, MessageDelimiters, MultiplexingStream.Options?)"/>
		/// <typeparam name="TService">The service interface. This must have <see cref="GenerateShapeAttribute"/> applied with <see cref="GenerateShapeAttribute.IncludeMethods"/> set to at least <see cref="MethodShapeFlags.PublicInstance"/>.</typeparam>
#if NET8_0
		[RequiresDynamicCode(ResolveDynamicMessage)]
#endif
#if NET
		[EditorBrowsable(EditorBrowsableState.Never)]
		[Obsolete("Use the ServiceJsonRpcPolyTypeDescriptor.Create<T>() method instead. If using the extension method syntax, check that your type argument actually has a [GenerateShape] attribute or otherwise implements IShapeable<T> to avoid a runtime failure.", error: true)]
#endif
		public static ServiceJsonRpcPolyTypeDescriptor Create<TService>(ServiceMoniker serviceMoniker, Formatters formatter = Formatters.NerdbankMessagePack, MessageDelimiters messageDelimiter = MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStream.Options? multiplexingStreamOptions = null)
			=> new(serviceMoniker, [TypeShapeResolver.ResolveDynamicOrThrow<TService>()], [], formatter, messageDelimiter, multiplexingStreamOptions);

		/// <summary>
		/// Initializes a new instance of the <see cref="ServiceJsonRpcPolyTypeDescriptor"/> class.
		/// </summary>
		/// <typeparam name="TService">The service interface.</typeparam>
		/// <typeparam name="TProvider">The type that provides the shape for <typeparamref name="TService"/>. This must have <see cref="GenerateShapeForAttribute"/> applied with <see cref="GenerateShapeForAttribute.IncludeMethods"/> set to at least <see cref="MethodShapeFlags.PublicInstance"/>.</typeparam>
		/// <param name="serviceMoniker"><inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, System.Collections.Immutable.ImmutableArray{ITypeShape}, System.Collections.Immutable.ImmutableArray{ITypeShape}, Formatters, MessageDelimiters, MultiplexingStream.Options?)" path="/param[@name='serviceMoniker']"/></param>
		/// <param name="formatter"><inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, System.Collections.Immutable.ImmutableArray{ITypeShape}, System.Collections.Immutable.ImmutableArray{ITypeShape}, Formatters, MessageDelimiters, MultiplexingStream.Options?)" path="/param[@name='formatter']"/></param>
		/// <param name="messageDelimiter"><inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, System.Collections.Immutable.ImmutableArray{ITypeShape}, System.Collections.Immutable.ImmutableArray{ITypeShape}, Formatters, MessageDelimiters, MultiplexingStream.Options?)" path="/param[@name='messageDelimiter']"/></param>
		/// <param name="multiplexingStreamOptions"><inheritdoc cref="ServiceJsonRpcPolyTypeDescriptor(ServiceMoniker, System.Collections.Immutable.ImmutableArray{ITypeShape}, System.Collections.Immutable.ImmutableArray{ITypeShape}, Formatters, MessageDelimiters, MultiplexingStream.Options?)" path="/param[@name='multiplexingStreamOptions']"/></param>
		/// <returns>The newly initialized instance.</returns>
#if NET8_0
		[RequiresDynamicCode(ResolveDynamicMessage)]
#endif
#if NET
		[EditorBrowsable(EditorBrowsableState.Never)]
		[Obsolete("Use the ServiceJsonRpcPolyTypeDescriptor.Create<T>() method instead. If using the extension method syntax, check that your type argument actually has a [GenerateShape] attribute or otherwise implements IShapeable<T> to avoid a runtime failure.", error: true)]
#endif
		public static ServiceJsonRpcPolyTypeDescriptor Create<TService, TProvider>(ServiceMoniker serviceMoniker, Formatters formatter = Formatters.NerdbankMessagePack, MessageDelimiters messageDelimiter = MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStream.Options? multiplexingStreamOptions = null)
			=> new(serviceMoniker, [TypeShapeResolver.ResolveDynamicOrThrow<TService, TProvider>()], [], formatter, messageDelimiter, multiplexingStreamOptions);
	}

#if NET8_0
	/// <summary>
	/// A message to use as the argument to <see cref="RequiresDynamicCodeAttribute"/>
	/// for methods that call into <see cref="TypeShapeResolver.ResolveDynamicOrThrow{T}"/>.
	/// </summary>
	/// <seealso href="https://github.com/dotnet/runtime/issues/119440#issuecomment-3269894751"/>
	private const string ResolveDynamicMessage =
		"Dynamic resolution of IShapeable<T> interface may require dynamic code generation in .NET 8 Native AOT. " +
		"It is recommended to switch to statically resolved IShapeable<T> APIs or upgrade your app to .NET 9 or later.";
#endif
}
