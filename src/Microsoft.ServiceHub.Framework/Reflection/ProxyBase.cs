// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Microsoft.ServiceHub.Framework.Reflection;

/// <summary>
/// An abstract base class for proxies around locally provisioned brokered services.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ProxyBase : IDisposableObservable, INotifyDisposable, IClientProxy, IJsonRpcLocalProxy
{
	// Sentinel value stored in <see cref="disposedHandlers"/> after disposal so that
	// add/remove of <see cref="INotifyDisposable.Disposed"/> can detect the disposed state
	// and invoke the handler immediately rather than rooting it in a backing field.
	private static readonly object DisposedSentinel = new();
	private static readonly HashSet<Type> BuiltInProxyInterfaces = [.. typeof(ProxyBase).GetInterfaces()];

	private readonly ProxyInputs proxyInputs;
	private object? target;
	private object? disposedHandlers;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProxyBase"/> class.
	/// </summary>
	/// <param name="target">The actual brokered service.</param>
	/// <param name="proxyInputs">Settings for the proxy.</param>
	protected ProxyBase(object target, in ProxyInputs proxyInputs)
	{
		Requires.NotNull(target);

		this.target = target;
		this.proxyInputs = proxyInputs;
	}

	/// <inheritdoc/>
	public event EventHandler? Disposed
	{
		add
		{
			if (!TryUpdateHandlers(ref this.disposedHandlers, value, combine: true))
			{
				value?.Invoke(this, EventArgs.Empty);
			}
		}

		remove => TryUpdateHandlers(ref this.disposedHandlers, value, combine: false);
	}

	/// <inheritdoc/>
	public bool IsDisposed => this.target is null;

	/// <summary>
	/// Gets the actual service object.
	/// </summary>
	/// <exception cref="ObjectDisposedException">Thrown if the proxy has already been disposed.</exception>
	protected object Target => this.target ?? throw new ObjectDisposedException(this.GetType().FullName);

	/// <summary>
	/// Gets the actual service object, or <see langword="null" /> if this proxy has been disposed.
	/// </summary>
	protected object? TargetOrNull => this.target;

	/// <summary>
	/// Creates a source generated proxy for the specified target object and <see cref="Reflection.ProxyInputs"/>.
	/// </summary>
	/// <param name="target">The target object that the proxy will dispatch all calls to.</param>
	/// <param name="proxyInputs">The inputs describing the contract interface, additional interfaces, and options for proxy generation.</param>
	/// <param name="startOrFail">
	/// If <see langword="true"/>, the <paramref name="target"/> will be disposed if no compatible proxy can be found.
	/// </param>
	/// <returns>The created <see cref="IJsonRpcClientProxy"/> instance.</returns>
	/// <remarks>
	/// If a compatible proxy is found, it is returned; otherwise, the <see cref="JsonRpc"/> instance is disposed (if <paramref name="startOrFail"/> is <see langword="true"/>)
	/// and a <see cref="NotImplementedException"/> is thrown.
	/// </remarks>
	/// <exception cref="ArgumentException">Thrown when <paramref name="target"/> does not implement all the interfaces as required by <paramref name="proxyInputs"/>.</exception>
	/// <exception cref="NotImplementedException">
	/// Thrown if no compatible source generated proxy can be found for the specified requirements in <paramref name="proxyInputs"/>.
	/// </exception>
	public static IClientProxy CreateProxy(object target, in ProxyInputs proxyInputs, bool startOrFail)
	{
		Requires.NotNull(target);

		if (TryCreateProxy(target, proxyInputs, out IClientProxy? proxy))
		{
			return proxy;
		}
		else
		{
			if (startOrFail)
			{
				(target as IDisposable)?.Dispose();
			}

			throw new NotImplementedException($"Unable to find a source generated proxy for *local* services filling the specified requirements: {proxyInputs.Requirements}. Research the NativeAOT topic in the documentation at https://microsoft.github.io/vs-streamjsonrpc");
		}
	}

	/// <summary>
	/// Attempts to create a source generated proxy that implements the specified contract and additional interfaces.
	/// </summary>
	/// <param name="target">The target object that the proxy will forward calls to.</param>
	/// <param name="proxyInputs">The inputs describing the contract interface, additional interfaces, and options for proxy generation.</param>
	/// <param name="proxy">
	/// When this method returns, contains the created <see cref="IClientProxy"/> if a compatible proxy was found; otherwise <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if a compatible proxy was found and created; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// This method searches for a source generated proxy type that implements a superset of the contract and additional interfaces specified in <paramref name="proxyInputs"/>.
	/// If a matching proxy type is found, it is instantiated and returned via <paramref name="proxy"/>.
	/// If no compatible proxy is found, <paramref name="proxy"/> is set to <see langword="null"/> and the method returns <see langword="false"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">Thrown when <paramref name="target"/> does not implement all the interfaces as required by <paramref name="proxyInputs"/>.</exception>
	public static bool TryCreateProxy(object target, in ProxyInputs proxyInputs, [NotNullWhen(true)] out IClientProxy? proxy)
	{
		Requires.NotNull(target);
		ThrowIfTargetIsMissingInterfaces(target, proxyInputs);

		foreach (LocalProxyMappingAttribute attribute in proxyInputs.ContractInterface.GetCustomAttributes(typeof(LocalProxyMappingAttribute), false))
		{
			// Of the various proxies that implement the interfaces the user requires,
			// look for a match.
			if (ProxyImplementsCompatibleSetOfInterfaces(
				attribute.ProxyClass,
				proxyInputs.ContractInterface,
				proxyInputs.AdditionalContractInterfaces.Span,
				proxyInputs.AcceptProxyWithExtraInterfaces))
			{
				proxy = (IClientProxy?)Activator.CreateInstance(attribute.ProxyClass, target, proxyInputs);
				return proxy is not null;
			}
		}

		proxy = null;
		return false;
	}

	/// <inheritdoc/>
	public T? ConstructLocalProxy<T>()
		where T : class
	{
		if (typeof(T).IsAssignableFrom(this.GetType()))
		{
			return (T)(object)this;
		}

		// If the wrapped target itself doesn't implement T, no proxy can satisfy the request.
		// Match the IL-emit proxy semantics by returning null rather than throwing.
		if (this.target is not T innerTarget)
		{
			return null;
		}

		return (T)CreateProxy(innerTarget, this.proxyInputs with { AdditionalContractInterfaces = default, ContractInterface = typeof(T) }, startOrFail: false);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		object? inner = Interlocked.Exchange(ref this.target, null);
		if (inner is null)
		{
			// Already disposed.
			return;
		}

		(inner as IDisposable)?.Dispose();
		inner = null;

		object? handlers = Interlocked.Exchange(ref this.disposedHandlers, DisposedSentinel);
		((EventHandler?)handlers)?.Invoke(this, EventArgs.Empty);
	}

	/// <inheritdoc/>
	public bool Is(Type type)
	{
		Requires.NotNull(type);

		bool assignable = type.IsAssignableFrom(this.GetType());

		// If the type check fails, then the contract is definitely not implemented.
		if (!assignable)
		{
			return false;
		}

		// If the type is one of the built-in interfaces that every proxy always implements, always return true.
		if (type.IsAssignableFrom(typeof(ProxyBase)))
		{
			return true;
		}

		if (type.IsAssignableFrom(this.proxyInputs.ContractInterface))
		{
			return true;
		}

		foreach (Type iface in this.proxyInputs.AdditionalContractInterfaces.Span)
		{
			if (type.IsAssignableFrom(iface))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Called from the generated proxy to help prepare the exception to throw.
	/// </summary>
	/// <param name="ex">The exception thrown from the target object.</param>
	/// <param name="exceptionStrategy">The value of <see cref="JsonRpc.ExceptionStrategy"/> to emulate.</param>
	/// <returns>The exception the generated code should throw.</returns>
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Exception ExceptionHelper(Exception ex, ExceptionProcessing exceptionStrategy)
	{
		var localRpcEx = ex as LocalRpcException;
		object errorData = localRpcEx?.ErrorData ?? new CommonErrorData(ex);
		if (localRpcEx is object)
		{
			return new RemoteInvocationException(
				ex.Message,
				localRpcEx?.ErrorCode ?? (int)JsonRpcErrorCode.InvocationError,
				errorData,
				errorData);
		}

		return exceptionStrategy switch
		{
			ExceptionProcessing.CommonErrorData => new RemoteInvocationException(
				ex.Message,
				localRpcEx?.ErrorCode ?? (int)JsonRpcErrorCode.InvocationError,
				errorData,
				errorData),
			ExceptionProcessing.ISerializable => new RemoteInvocationException(ex.Message, (int)JsonRpcErrorCode.InvocationErrorWithException, ex),
			_ => new NotSupportedException("Unsupported exception strategy: " + exceptionStrategy),
		};
	}

	/// <inheritdoc cref="ExceptionHelper(Exception, ExceptionProcessing)"/>
	protected Exception ExceptionHelper(Exception ex) => ExceptionHelper(ex, this.proxyInputs.ExceptionStrategy);

	/// <summary>
	/// Determines whether a proxy class implements a compatible set of interfaces.
	/// </summary>
	/// <param name="proxyClass">The type of the proxy class to be evaluated. This type must implement the specified interfaces.</param>
	/// <param name="contractInterface">The primary contract interface that the proxy class must implement.</param>
	/// <param name="additionalContractInterfaces">A span of additional contract interfaces that the proxy class must also implement.</param>
	/// <param name="acceptProxyWithExtraInterfaces">A value indicating whether extra interfaces are acceptable on a source-generated proxy.</param>
	/// <returns><see langword="true"/> if the proxy class implements the specified contract interface and additional interfaces,
	/// (and potentially extra interfaces); otherwise, <see langword="false"/>.</returns>
	private static bool ProxyImplementsCompatibleSetOfInterfaces(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type proxyClass,
		Type contractInterface,
		ReadOnlySpan<Type> additionalContractInterfaces,
		bool acceptProxyWithExtraInterfaces)
	{
		HashSet<Type> proxyInterfaces = [.. proxyClass.GetInterfaces()];
		if (!proxyInterfaces.Contains(contractInterface))
		{
			return false;
		}

		foreach (Type addl in additionalContractInterfaces)
		{
			if (!proxyInterfaces.Contains(addl))
			{
				return false;
			}
		}

		if (!acceptProxyWithExtraInterfaces)
		{
			foreach (Type proxyInterface in proxyInterfaces)
			{
				if (BuiltInProxyInterfaces.Contains(proxyInterface) || proxyInterface.IsAssignableFrom(contractInterface))
				{
					continue;
				}

				bool impliedByAdditionalInterface = false;
				foreach (Type addl in additionalContractInterfaces)
				{
					if (proxyInterface.IsAssignableFrom(addl))
					{
						impliedByAdditionalInterface = true;
						break;
					}
				}

				if (!impliedByAdditionalInterface)
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	/// Verifies that the specified <paramref name="target"/> implements all the interfaces as required by <paramref name="proxyInputs"/>.
	/// </summary>
	/// <exception cref="InvalidCastException">Thrown when <paramref name="target"/> does not implement all the interfaces as required by <paramref name="proxyInputs"/>.</exception>
	private static void ThrowIfTargetIsMissingInterfaces(object target, in ProxyInputs proxyInputs)
	{
		// Verify that the target implements the required contract interface and additional interfaces.
		Type targetType = target.GetType();
		if (!proxyInputs.ContractInterface.IsAssignableFrom(targetType))
		{
			throw new InvalidCastException();
		}

		foreach (Type addlInterface in proxyInputs.AdditionalContractInterfaces.Span)
		{
			if (!addlInterface.IsAssignableFrom(targetType))
			{
				throw new InvalidCastException();
			}
		}
	}

	/// <summary>
	/// Atomically updates a delegate handler field, returning <see langword="false"/> if the field
	/// already holds <see cref="DisposedSentinel"/>.
	/// </summary>
	private static bool TryUpdateHandlers(ref object? handlers, EventHandler? value, bool combine)
	{
		object? oldValue = handlers;
		while (oldValue != DisposedSentinel)
		{
			object? newValue = combine
				? (object?)Delegate.Combine((EventHandler?)oldValue, value)
				: (object?)Delegate.Remove((EventHandler?)oldValue, value);
			object? prevValue = Interlocked.CompareExchange(ref handlers, newValue, oldValue);
			if (prevValue == oldValue)
			{
				return true;
			}

			oldValue = prevValue;
		}

		return false;
	}
}
