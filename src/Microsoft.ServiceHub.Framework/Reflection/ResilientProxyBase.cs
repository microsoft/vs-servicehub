// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.ServiceHub.Framework.Reflection;

/// <summary>
/// Base class for source-generated resilient service proxies.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ResilientProxyBase : IResilientServiceProxy
{
	private static readonly object DisposedSentinel = new();
	private static readonly HashSet<Type> BuiltInProxyInterfaces =
	[
		typeof(IResilientServiceProxy),
		typeof(IDisposableObservable),
		typeof(INotifyDisposable),
		typeof(IDisposable),
		typeof(IAsyncDisposable),
	];

	private EventHandler? availabilityChangedHandlers;
	private object? disposedHandlers;
	private EventHandler<ResilientServiceProxyInvalidatedEventArgs>? invalidatedHandlers;

	/// <inheritdoc />
	public event EventHandler? AvailabilityChanged
	{
		add => UpdateEventHandlers(ref this.availabilityChangedHandlers, value, add: true);
		remove => UpdateEventHandlers(ref this.availabilityChangedHandlers, value, add: false);
	}

	/// <inheritdoc />
	public event EventHandler? Disposed
	{
		add
		{
			if (!TryUpdateDisposedHandlers(ref this.disposedHandlers, value, combine: true))
			{
				value?.Invoke(this, EventArgs.Empty);
			}
		}

		remove => TryUpdateDisposedHandlers(ref this.disposedHandlers, value, combine: false);
	}

	/// <inheritdoc />
	public event EventHandler<ResilientServiceProxyInvalidatedEventArgs>? Invalidated
	{
		add => UpdateEventHandlers(ref this.invalidatedHandlers, value, add: true);
		remove => UpdateEventHandlers(ref this.invalidatedHandlers, value, add: false);
	}

	/// <summary>
	/// Gets a value indicating whether this proxy currently has a backing service.
	/// </summary>
	public abstract bool IsAvailable { get; }

	/// <summary>
	/// Gets a value indicating whether this proxy has been disposed.
	/// </summary>
	public abstract bool IsDisposed { get; }

	/// <summary>
	/// Releases resources owned by this proxy.
	/// </summary>
	public abstract void Dispose();

	/// <summary>
	/// Asynchronously releases resources owned by this proxy.
	/// </summary>
	/// <returns>A task representing disposal.</returns>
	public abstract ValueTask DisposeAsync();

	/// <summary>
	/// Creates and initializes a source-generated resilient proxy.
	/// </summary>
	/// <typeparam name="T">The requested service interface.</typeparam>
	/// <param name="serviceBroker">The broker used to acquire inner proxies.</param>
	/// <param name="serviceDescriptor">The service descriptor.</param>
	/// <param name="options">Service activation options.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The resilient proxy, or <see langword="null"/> when the service is initially unavailable.</returns>
	internal static async ValueTask<T?> CreateAsync<T>(
		IServiceBroker serviceBroker,
		ServiceRpcDescriptor serviceDescriptor,
		ServiceActivationOptions options,
		CancellationToken cancellationToken)
		where T : class
	{
		Type? proxyClass = FindProxyClass<T>(serviceDescriptor);
		if (proxyClass is null)
		{
			throw new NotSupportedException($"No source-generated resilient proxy is available for {typeof(T).FullName}. Make the RPC contract and all containing types partial so Microsoft.ServiceHub.Analyzers can register one.");
		}

		ResilientProxyBase proxy;
		try
		{
			proxy = (ResilientProxyBase?)Activator.CreateInstance(proxyClass, serviceBroker, serviceDescriptor, options)
				?? throw new ServiceCompositionException("Unable to activate the source-generated resilient proxy.");
		}
		catch (System.Reflection.TargetInvocationException ex)
		{
			throw new ServiceCompositionException("Unable to activate the source-generated resilient proxy.", ex.InnerException);
		}

		try
		{
			if (!await proxy.InitializeAsync(cancellationToken).ConfigureAwait(false))
			{
				await proxy.DisposeAsync().ConfigureAwait(false);
				return null;
			}
		}
		catch
		{
			try
			{
				await proxy.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				try
				{
					proxy.TraceEventHandlerFailure(ex);
				}
				catch
				{
					// Preserve the activation failure even when cleanup diagnostics fail.
				}
			}

			throw;
		}

		return (T)(object)proxy;
	}

	/// <summary>
	/// Initializes the first inner proxy.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns><see langword="true"/> when the service was available.</returns>
	protected abstract ValueTask<bool> InitializeAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Raises <see cref="AvailabilityChanged"/>.
	/// </summary>
	protected void OnAvailabilityChanged()
	{
		EventHandler? handlers = this.availabilityChangedHandlers;
		if (handlers is null)
		{
			return;
		}

		foreach (EventHandler handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				this.TraceEventHandlerFailure(ex);
			}
		}
	}

	/// <summary>
	/// Raises <see cref="Invalidated"/>.
	/// </summary>
	/// <param name="args">The event arguments.</param>
	protected void OnInvalidated(ResilientServiceProxyInvalidatedEventArgs args)
	{
		EventHandler<ResilientServiceProxyInvalidatedEventArgs>? handlers = this.invalidatedHandlers;
		if (handlers is null)
		{
			return;
		}

		foreach (EventHandler<ResilientServiceProxyInvalidatedEventArgs> handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception ex)
			{
				this.TraceEventHandlerFailure(ex);
			}
		}
	}

	/// <summary>
	/// Raises <see cref="Disposed"/>.
	/// </summary>
	protected void OnDisposed()
	{
		object? handlers = Interlocked.Exchange(ref this.disposedHandlers, DisposedSentinel);
		if (handlers is EventHandler disposed)
		{
			disposed(this, EventArgs.Empty);
		}
	}

	/// <summary>
	/// Records an exception thrown by a lifecycle event handler.
	/// </summary>
	/// <param name="exception">The exception.</param>
	protected abstract void TraceEventHandlerFailure(Exception exception);

	[return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
	private static Type? FindProxyClass<T>(ServiceRpcDescriptor serviceDescriptor)
		where T : class
	{
		ReadOnlySpan<Type> additionalInterfaces = serviceDescriptor switch
		{
			ServiceJsonRpcDescriptor { AdditionalServiceInterfaces: { } additional } => additional.AsSpan(),
			ServiceJsonRpcPolyTypeDescriptor { AdditionalServiceInterfaces: { } additional } => additional.AsSpan(),
			_ => [],
		};
		bool acceptProxyWithExtraInterfaces = serviceDescriptor switch
		{
			ServiceJsonRpcDescriptor descriptor => descriptor.AcceptProxyWithExtraInterfaces,
			ServiceJsonRpcPolyTypeDescriptor => true,
			_ => false,
		};

		Type? bestMatch = null;
		int bestInterfaceCount = int.MaxValue;
		foreach (ResilientProxyMappingAttribute attribute in typeof(T).GetCustomAttributes(typeof(ResilientProxyMappingAttribute), inherit: false))
		{
			Type[] implementedInterfaces = attribute.ProxyClass.GetInterfaces();
			if (!typeof(T).IsAssignableFrom(attribute.ProxyClass))
			{
				continue;
			}

			bool includesAllAdditionalInterfaces = true;
			foreach (Type additionalInterface in additionalInterfaces)
			{
				if (!additionalInterface.IsAssignableFrom(attribute.ProxyClass))
				{
					includesAllAdditionalInterfaces = false;
					break;
				}
			}

			if (!includesAllAdditionalInterfaces)
			{
				continue;
			}

			if (!acceptProxyWithExtraInterfaces && HasUnexpectedInterfaces<T>(implementedInterfaces, additionalInterfaces))
			{
				continue;
			}

			if (implementedInterfaces.Length < bestInterfaceCount)
			{
				bestMatch = attribute.ProxyClass;
				bestInterfaceCount = implementedInterfaces.Length;
			}
		}

		return bestMatch;
	}

	private static bool HasUnexpectedInterfaces<T>(Type[] implementedInterfaces, ReadOnlySpan<Type> additionalInterfaces)
		where T : class
	{
		foreach (Type implementedInterface in implementedInterfaces)
		{
			if (BuiltInProxyInterfaces.Contains(implementedInterface) || implementedInterface.IsAssignableFrom(typeof(T)))
			{
				continue;
			}

			bool impliedByAdditionalInterface = false;
			foreach (Type additionalInterface in additionalInterfaces)
			{
				if (implementedInterface.IsAssignableFrom(additionalInterface))
				{
					impliedByAdditionalInterface = true;
					break;
				}
			}

			if (!impliedByAdditionalInterface)
			{
				return true;
			}
		}

		return false;
	}

	private static bool TryUpdateDisposedHandlers(ref object? handlers, EventHandler? value, bool combine)
	{
		object? oldValue = handlers;
		while (oldValue != DisposedSentinel)
		{
			object? newValue = combine
				? (object?)Delegate.Combine((EventHandler?)oldValue, value)
				: (object?)Delegate.Remove((EventHandler?)oldValue, value);
			object? previousValue = Interlocked.CompareExchange(ref handlers, newValue, oldValue);
			if (previousValue == oldValue)
			{
				return true;
			}

			oldValue = previousValue;
		}

		return false;
	}

	private static void UpdateEventHandlers<THandler>(ref THandler? handlers, THandler? value, bool add)
		where THandler : Delegate
	{
		THandler? oldValue;
		THandler? newValue;
		do
		{
			oldValue = handlers;
			newValue = (THandler?)(add ? Delegate.Combine(oldValue, value) : Delegate.Remove(oldValue, value));
		}
		while (Interlocked.CompareExchange(ref handlers, newValue, oldValue) != oldValue);
	}
}
