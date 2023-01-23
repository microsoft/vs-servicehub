// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Reflection;
using Nerdbank.Streams;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A description of a service to help automate connecting to it.
/// </summary>
public abstract partial class ServiceRpcDescriptor
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceRpcDescriptor"/> class.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="clientInterface">The interface type that the client's "callback" RPC target is expected to implement. May be null if the service does not invoke methods on the client.</param>
	public ServiceRpcDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface)
	{
		Requires.NotNull(serviceMoniker, nameof(serviceMoniker));

		this.Moniker = serviceMoniker;
		this.ClientInterface = clientInterface;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceRpcDescriptor"/> class
	/// and initializes all fields based on a template instance.
	/// </summary>
	/// <param name="copyFrom">The instance to copy all fields from.</param>
	protected ServiceRpcDescriptor(ServiceRpcDescriptor copyFrom)
	{
		Requires.NotNull(copyFrom, nameof(copyFrom));

		this.Moniker = copyFrom.Moniker;
		this.ClientInterface = copyFrom.ClientInterface;
		this.TraceSource = copyFrom.TraceSource;
		this.MultiplexingStream = copyFrom.MultiplexingStream;
	}

	/// <summary>
	/// Gets the moniker for the service.
	/// </summary>
	public ServiceMoniker Moniker { get; private set; }

	/// <summary>
	/// Gets a non-localized name of the protocol supported by this instance.
	/// </summary>
	public abstract string Protocol { get; }

	/// <summary>
	/// Gets the <see cref="TraceSource"/> to be used on constructed clients or servers.
	/// </summary>
	/// <value><see langword="null"/> by default.</value>
	public TraceSource? TraceSource { get; private set; }

	/// <summary>
	/// Gets the <see cref="Nerdbank.Streams.MultiplexingStream"/> that may be used by constructed clients or servers.
	/// </summary>
	/// <value><see langword="null"/> by default.</value>
	public MultiplexingStream? MultiplexingStream { get; private set; }

	/// <summary>
	/// Gets the interface type that the client's "callback" RPC target is expected to implement.
	/// </summary>
	/// <value>May be null if the service does not invoke methods on the client.</value>
	public Type? ClientInterface { get; }

	/// <summary>
	/// Creates an RPC client proxy over a given <see cref="IDuplexPipe"/>
	/// and provides a local RPC target for the remote party to invoke methods locally.
	/// </summary>
	/// <typeparam name="T">The type of the RPC proxy to generate for invoking methods on the remote party or receiving events from it.</typeparam>
	/// <param name="rpcTarget">
	/// A local RPC target on which the remote party can invoke methods.
	/// This is usually optional for requestors of a service but is typically expected for the proffering services to provide.
	/// If this object implements <see cref="IDisposable"/>, <see cref="IDisposable.Dispose"/> will be invoked
	/// when the client closes their connection.
	/// </param>
	/// <param name="pipe">The pipe used to communicate with the remote party.</param>
	/// <returns>
	/// The generated proxy.
	/// This value should be disposed of when no longer needed, if it implements <see cref="IDisposable"/> at runtime.
	/// A convenient disposal syntax is:
	/// <code><![CDATA[(proxy as IDisposable)?.Dispose();]]></code>
	/// </returns>
	public T ConstructRpc<T>(object? rpcTarget, IDuplexPipe pipe)
		where T : class
	{
		Requires.NotNull(pipe, nameof(pipe));

		RpcConnection connection = this.ConstructRpcConnection(pipe);
		if (rpcTarget != null)
		{
			connection.AddLocalRpcTarget(rpcTarget);
		}

		T client = connection.ConstructRpcClient<T>();
		connection.StartListening();
		return client;
	}

	/// <summary>
	/// Creates an RPC client proxy over a given <see cref="IDuplexPipe"/>
	/// without providing a local RPC target for the remote party to invoke methods locally.
	/// </summary>
	/// <typeparam name="T">The type of the RPC proxy to generate for invoking methods on the remote party or receiving events from it.</typeparam>
	/// <param name="pipe">The pipe used to communicate with the remote party.</param>
	/// <returns>
	/// The generated proxy.
	/// This value should be disposed of when no longer needed, if it implements <see cref="IDisposable"/> at runtime.
	/// A convenient disposal syntax is:
	/// <code><![CDATA[(proxy as IDisposable)?.Dispose();]]></code>
	/// </returns>
	public T ConstructRpc<T>(IDuplexPipe pipe)
		where T : class
	{
		return this.ConstructRpc<T>(rpcTarget: null, pipe);
	}

	/// <summary>
	/// Establishes an RPC connection to a given object over an <see cref="IDuplexPipe"/>,
	/// allowing the remote party to invoke methods locally on the given object.
	/// </summary>
	/// <param name="rpcTarget">
	/// The target of any RPC calls received over the supplied <paramref name="pipe"/>.
	/// Raising events defined on this object may result in notifications being forwarded to the remote party.
	/// If this object implements <see cref="IDisposable"/>, <see cref="IDisposable.Dispose"/> will be invoked
	/// when the client closes their connection.
	/// </param>
	/// <param name="pipe">The pipe the <paramref name="rpcTarget"/> should use to communicate.</param>
	public void ConstructRpc(object rpcTarget, IDuplexPipe pipe)
	{
		Requires.NotNull(rpcTarget, nameof(rpcTarget));
		Requires.NotNull(pipe, nameof(pipe));

		RpcConnection connection = this.ConstructRpcConnection(pipe);
		if (rpcTarget != null)
		{
			connection.AddLocalRpcTarget(rpcTarget);
		}

		connection.StartListening();
	}

	/// <summary>
	/// Gives the <see cref="ServiceRpcDescriptor"/> a chance to wrap a local target object
	/// so that interacting with it behaves similarly to if it were a remote target that was using RPC.
	/// </summary>
	/// <typeparam name="T">The interface that defines the RPC contract for communicating with the <paramref name="target"/>.</typeparam>
	/// <param name="target">The local target object. May be null, which will result in null being returned.</param>
	/// <returns>The proxy wrapper (or null if <paramref name="target"/> is null); or possibly the original <paramref name="target"/> object if this method is not overriden by a derived-type.</returns>
	[return: NotNullIfNotNull("target")]
	public virtual T? ConstructLocalProxy<T>(T? target)
		where T : class => target;

	/// <summary>
	/// Returns an instance of <see cref="ServiceRpcDescriptor"/> that resembles this one,
	/// but with the <see cref="TraceSource" /> property set to the specified value.
	/// </summary>
	/// <param name="traceSource">The receiver of trace messages.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceRpcDescriptor WithTraceSource(TraceSource? traceSource)
	{
		if (this.TraceSource == traceSource)
		{
			return this;
		}

		ServiceRpcDescriptor result = this.Clone();
		result.TraceSource = traceSource;
		return result;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceRpcDescriptor"/> that resembles this one,
	/// but with the <see cref="ServiceMoniker" /> property set to the specified value.
	/// </summary>
	/// <param name="moniker">The moniker to be used in place of the original.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	public ServiceRpcDescriptor WithServiceMoniker(ServiceMoniker moniker)
	{
		Requires.NotNull(moniker, nameof(moniker));

		if (this.Moniker == moniker)
		{
			return this;
		}

		ServiceRpcDescriptor result = this.Clone();
		result.Moniker = moniker;
		return result;
	}

	/// <summary>
	/// Returns an instance of <see cref="ServiceRpcDescriptor"/> that resembles this one,
	/// but with the <see cref="MultiplexingStream" /> property set to the specified value and <see cref="MultiplexingStream.Options"/> set to <see langword="null"/>.
	/// </summary>
	/// <param name="multiplexingStream">The <see cref="Nerdbank.Streams.MultiplexingStream"/> that may be used by constructed clients or servers.</param>
	/// <returns>A clone of this instance, with the property changed. Or this same instance if the property already matches.</returns>
	[Obsolete("Use the WithMultiplexingStream(MultiplexingStream.Options) overload as may be defined in a derived type instead.")]
	public virtual ServiceRpcDescriptor WithMultiplexingStream(MultiplexingStream? multiplexingStream)
	{
		if (this.MultiplexingStream == multiplexingStream)
		{
			return this;
		}

		ServiceRpcDescriptor result = this.Clone();
		result.MultiplexingStream = multiplexingStream;
		return result;
	}

	/// <summary>
	/// Establishes an RPC connection over an <see cref="IDuplexPipe"/>.
	/// </summary>
	/// <param name="pipe">The pipe used to send and receive RPC messages.</param>
	/// <returns>An object representing the lifetime of the connection.</returns>
	/// <remarks>
	/// Callers are expected to call <see cref="RpcConnection.ConstructRpcClient{T}"/> and/or <see cref="RpcConnection.AddLocalRpcTarget(object)"/> on the result value
	/// before invoking <see cref="RpcConnection.StartListening"/> to begin the RPC session.
	/// </remarks>
	public abstract RpcConnection ConstructRpcConnection(IDuplexPipe pipe);

	/// <summary>
	/// Creates a copy of this instance with all the same properties.
	/// </summary>
	/// <returns>The copy.</returns>
	/// <remarks>
	/// Derived types should override this method to create a new instance of their own type,
	/// using the <see cref="ServiceRpcDescriptor(ServiceRpcDescriptor)"/> copy constructor,
	/// then copy all their unique properties from this instance to the new one before returning the new one.
	/// </remarks>
	protected abstract ServiceRpcDescriptor Clone();

	/// <summary>
	/// Represents an RPC connection.
	/// </summary>
	/// <remarks>
	/// This object should self-dispose when the underlying <see cref="IDuplexPipe"/> completes.
	/// </remarks>
	public abstract class RpcConnection : IDisposableObservable
	{
		/// <summary>
		/// Gets or sets the <see cref="TraceSource"/> that receives log messages regarding the RPC connection.
		/// </summary>
		public abstract TraceSource TraceSource { get; set; }

		/// <inheritdoc/>
		public abstract bool IsDisposed { get; }

		/// <summary>
		/// Gets a <see cref="Task"/> that completes when the underlying RPC connection has shutdown
		/// and any local RPC target objects have been disposed of, if applicable.
		/// </summary>
		public abstract Task Completion { get; }

		/// <summary>
		/// Adds a target object to receive RPC calls.
		/// </summary>
		/// <param name="rpcTarget">
		/// A target for any RPC calls received over the connection.
		/// If this object implements <see cref="IDisposable"/>, <see cref="IDisposable.Dispose"/> will be invoked
		/// when the client closes their connection.
		/// </param>
		/// <exception cref="InvalidOperationException">May be thrown when <see cref="StartListening"/> has already been called.</exception>
		public abstract void AddLocalRpcTarget(object rpcTarget);

		/// <summary>
		/// Produces a proxy that provides a strongly-typed API for invoking methods offered by the remote party.
		/// </summary>
		/// <typeparam name="T">The interface that the returned proxy should implement.</typeparam>
		/// <returns>The generated proxy.</returns>
		/// <remarks>
		/// This method may be called any number of times, but restrictions may apply after <see cref="StartListening"/> is called,
		/// particularly when <typeparamref name="T"/> includes events.
		/// </remarks>
		/// <exception cref="InvalidOperationException">May be thrown when <see cref="StartListening"/> has already been called.</exception>
		public abstract T ConstructRpcClient<T>()
			where T : class;

		/// <summary>
		/// Produces a proxy that provides a strongly-typed API for invoking methods offered by the remote party.
		/// </summary>
		/// <param name="interfaceType">The interface that the returned proxy should implement.</param>
		/// <returns>The generated proxy.</returns>
		/// <remarks>
		/// This method may be called any number of times, but restrictions may apply after <see cref="StartListening"/> is called,
		/// particularly when <paramref name="interfaceType"/> includes events.
		/// </remarks>
		/// <exception cref="InvalidOperationException">May be thrown when <see cref="StartListening"/> has already been called.</exception>
		public virtual object ConstructRpcClient(Type interfaceType)
		{
			Requires.NotNull(interfaceType, nameof(interfaceType));

			MethodInfo? genericOverload = this.GetType().GetTypeInfo().GetRuntimeMethod(nameof(this.ConstructRpcClient), Type.EmptyTypes);
			Assumes.NotNull(genericOverload);
			MethodInfo closedGenericOverload = genericOverload.MakeGenericMethod(interfaceType);
			return closedGenericOverload.Invoke(this, Array.Empty<object>())!;
		}

		/// <summary>
		/// Begins listening for incoming messages.
		/// </summary>
		/// <remarks>
		/// This isn't automatic since sometimes event listeners must be wired up before messages come in that would raise those events.
		/// </remarks>
		public abstract void StartListening();

		/// <summary>
		/// Disconnects from the RPC pipe, and disposes of managed and native resources held by this instance.
		/// </summary>
		public abstract void Dispose();
	}
}
