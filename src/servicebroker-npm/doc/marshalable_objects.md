# Marshalable objects

This package implements [the marshalable object protocol](https://microsoft.github.io/vs-streamjsonrpc/exotic_types/general_marshaled_objects.html) first implemented by StreamJsonRpc.
The overall behavior and wire protocol are defined there.

The TypeScript/javascript-specific behaviors are outlined here.

## Sending a marshaled object

By default, all values passed as arguments or return values are serialized by value.
To marshal an object for remote function invocation instead of serializing by value, the object should implement the `RpcMarshalable` interface.

For example:

```ts
interface ICalculator {
    add(a: number, b: number): Promise<number> | number
}

class Calculator implements ICalculator, RpcMarshalable {
    readonly _jsonRpcMarshalableLifetime: MarshaledObjectLifetime = 'explicit'

    add(a: number, b: number) {
        return a + b
    }
}
```

Only top-level arguments and return values are tested for marshalability.
This means that the Calculator object must appear as its own argument or return value in order to be marshaled.
If it appears deep in the object graph of an argument or return value, it will simply be serialized.

If the receiver disposes the proxy, and the real object defines a `dispose` method, the `dispose` method will be invoked on the real object.
This means that when you pass an object across RPC, you are effectively transferring lifetime ownership to the remote party.

## Receiving a marshaled object

When it arrives on the remote side, it will no longer be an instance of the `Calculator` class, but instead will be a proxy.
This proxy will relay all function calls to the original object.

The proxy must be disposed of when done or it will occupy resources for as long as the JSON-RPC connection lasts on both ends of the connection.

You can leverage Typescript for type safety for this. For example, the calculator might be accepted as an argument like this:

```ts
interface IServer {
    doSomeMath(calc: ICalculator): Promise<number>
}
```

The server may be implemented like this:

```ts
class Server {
    doSomeMath(calc: ICalculator & IDisposable): Promise<number> {
        const sum = await calc.add(2, 3)
        calc.dispose()
        return sum // 5
    }
}
```
