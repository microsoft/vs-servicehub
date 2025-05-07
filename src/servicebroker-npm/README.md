# NodeJS ServiceHub Framework

This NPM package provides a way to proffer and consume brokered services.
It is used in Visual Studio-related products to exchange services within and across processes and even across machines.

Learn about the [brokered services essentials](https://learn.microsoft.com/visualstudio/extensibility/internals/brokered-service-essentials), [how to provide a brokered service](https://learn.microsoft.com/visualstudio/extensibility/how-to-provide-brokered-service), and [how to consume a brokered service](https://learn.microsoft.com/visualstudio/extensibility/how-to-consume-brokered-service).

## Usage

Given an instance of `IServiceBroker`, you can request a service (such as a simple calculator service) like this:

```ts
const proxy = await serviceBroker.getProxy<ICalculatorService>(CalculatorDescriptor);
try {
    if (proxy) {
        const sum = await proxy.add(3, 5);
        assert(sum == 8);
    }
} finally {
    proxy?.dispose();
}
```

Learn more on [our doc site](https://microsoft.github.io/vs-servicehub/docs/npm.html).

## Contributing

Check out our [CONTRIBUTING.md](https://github.com/microsoft/vs-servicehub/blob/main/CONTRIBUTING.md) file.
