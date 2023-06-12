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

Important points to remember:

1. Always be defensive by checking for an `null` result from the call for a service.
1. Always dispose the proxy when you're done with it to avoid leaking resources. These proxies do *not* get garbage collected automatically on account of the I/O resource they require.

Let's do something real. Visual Studio 16.6 includes a `VersionInfoService` that exposes the VS and Live Share versions on the host.
You can call that service from VS Code like this:

```ts
import * as isb from '@microsoft/servicehub-framework';
import * as vsls from 'vsls';

const VersionInfoService = new isb.ServiceJsonRpcDescriptor(
    isb.ServiceMoniker.create('Microsoft.VisualStudio.Shell.VersionInfoService', '1.0'),
    isb.Formatters.Utf8,
    isb.MessageDelimiters.HttpLikeHeaders);

interface IVersionInfoService {
    GetVersionInformationAsync(cancellationToken?: vscode.CancellationToken): Promise<VersionInformation>;
}

interface VersionInformation {
    visualStudioVersion: string;
    liveShareVersion: string;
}

const ls = await vsls.getApi();
const serviceBroker = await ls.services.getRemoteServiceBroker();
const proxy = await serviceBroker?.getProxy<IVersionInfoService>(VersionInfoService);
try {
    if (proxy) {
        const versionInfo = await proxy.GetVersionInformationAsync();
        console.log(`VS version: ${versionInfo.visualStudioVersion}`);
    }
} finally {
    proxy?.dispose();
}
```

### Brokered Service Container

A process that wishes to offer its own brokered services need a container.
If your javascript process does not already have a container, the following is for you.

Here is the simplest possible, self-contained executable sample (in TypeScript).
The sample demonstrates definition and implementation of a service, registers it and proffers it with a `ServiceRpcDescriptor` and service factory into the container, and finally consumes it via an `IServiceBroker`.

```ts
import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import {
    Formatters,
    MessageDelimiters,
    ServiceJsonRpcDescriptor,
    ServiceMoniker,
    ServiceRpcDescriptor,
    GlobalBrokeredServiceContainer,
    ServiceAudience,
    ServiceRegistration,
} from '@microsoft/servicehub-framework'

interface IService {
    readonly moniker: ServiceMoniker
    readonly descriptor: ServiceRpcDescriptor
    readonly registration: ServiceRegistration
}

class Services {
    static calculator: Readonly<IService> = Services.defineLocal('calc')

    private static defineLocal(
        name: string,
        version?: string
    ): Readonly<IService> {
        const moniker = { name, version }
        const descriptor = new ServiceJsonRpcDescriptor(
            moniker,
            Formatters.MessagePack,
            MessageDelimiters.BigEndianInt32LengthHeader
        )
        const registration = new ServiceRegistration(
            ServiceAudience.local,
            false
        )
        return Object.freeze({ moniker, descriptor, registration })
    }
}

interface ICalculator {
    add(
        a: number,
        b: number,
        cancellationToken?: CancellationToken
    ): Promise<number>
}

class Calculator implements ICalculator {
    public add(
        a: number,
        b: number,
        cancellationToken?: CancellationToken
    ): Promise<number> {
        return Promise.resolve(a + b)
    }
}

let container: GlobalBrokeredServiceContainer
beforeAll(function () {
    container = new GlobalBrokeredServiceContainer()
    container.register([Services.calculator])
    container.profferServiceFactory(
        Services.calculator.descriptor,
        (mk, options, sb, ct) => Promise.resolve(new Calculator())
    )
})

it('self-contained sample', async function () {
    const sb = container.getFullAccessServiceBroker()
    const calc = await sb.getProxy<ICalculator>(Services.calculator.descriptor)
    assert(calc)
    const sum = await calc.add(3, 5)
    assert(sum === 8)
})
```

Note that in a real world application, the preceding code would typically be divided into many files, and may even span packages and processes.

## Contributing

Check out our [CONTRIBUTING.md](CONTRIBUTING.md) file.
