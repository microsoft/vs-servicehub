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
	private readonly ProxyInputs proxyInputs;
	private object? target;

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
	public event EventHandler? Disposed;

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

			throw new NotImplementedException($"Unable to find a source generated proxy filling the specified requirements: {proxyInputs.Requirements}. Research the NativeAOT topic in the documentation at https://microsoft.github.io/vs-streamjsonrpc");
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
				proxyInputs.AdditionalContractInterfaces.Span))
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

		return (T)CreateProxy(this.Target, this.proxyInputs with { AdditionalContractInterfaces = default, ContractInterface = typeof(T) }, startOrFail: false);
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
		this.Disposed?.Invoke(this, EventArgs.Empty);
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
	/// <returns><see langword="true"/> if the proxy class implements the specified contract interface and additional interfaces,
	/// (and potentially extra interfaces); otherwise, <see langword="false"/>.</returns>
	private static bool ProxyImplementsCompatibleSetOfInterfaces(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type proxyClass,
		Type contractInterface,
		ReadOnlySpan<Type> additionalContractInterfaces)
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
}
