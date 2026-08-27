# Microsoft.ServiceHub.Framework

[![NuGet package](https://img.shields.io/nuget/v/Microsoft.ServiceHub.Framework.svg)](https://www.nuget.org/packages/Microsoft.ServiceHub.Framework)

This package contains the APIs necessary to proffer and consume [brokered services](https://learn.microsoft.com/visualstudio/extensibility/internals/brokered-service-essentials?view=vs-2022).

It includes infrastructure for in-proc brokered service sharing as well as sharing across processes on the same machine and user account over named pipes.
Other transports are also supported, provided they provide a duplex pipe.

## Resilient service proxies

Use `ServiceBrokerAggregator.Resilient` when a consumer needs a stable service proxy that reconnects after the broker invalidates its current local or RPC proxy:

```csharp
IServiceBroker resilientBroker = ServiceBrokerAggregator.Resilient(serviceBroker);
IFooService? service = await resilientBroker.GetProxyAsync<IFooService>(
    FooServiceDescriptor,
    activationOptions,
    cancellationToken);
```

The service contract and all containing types must be `partial` so the Microsoft.ServiceHub source generator can register a resilient proxy. Initial unavailability still returns `null`. After a proxy has been returned, calls wait for an in-progress replacement acquisition, but throw `ServiceUnavailableException` when the broker confirms that the service is unavailable.

The resilient proxy keeps the same identity while replacing its inner proxy. Contract event handlers follow each replacement automatically. Methods that accept an `IObserver<T>` and return `Task<IDisposable>` or `ValueTask<IDisposable>` also return a stable subscription: the observer is subscribed to each replacement until that returned subscription is disposed. Subscribe to `IResilientServiceProxy.Invalidated` to rebuild other mutable state held by the service instance. Check `IResilientServiceProxy.IsAvailable` or subscribe to `AvailabilityChanged` to observe when the proxy loses or regains a backing service. A failed call is never replayed, because the service may have received it before the connection failed. Calls waiting for an in-progress replacement resume on that replacement. `void` notifications that cannot report a dispatch failure are queued in order until a replacement becomes available or the resilient proxy is disposed.

Dispose every resilient proxy through `IDisposable` or `IAsyncDisposable`. Disposal tears down only the stable proxy and its acquired inner proxies; the resilient broker wrapper does not own the broker passed to `ServiceBrokerAggregator.Resilient`.
