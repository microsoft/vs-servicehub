# Microsoft.ServiceHub.Framework

This package contains the APIs necessary to proffer and consume [brokered services](https://learn.microsoft.com/visualstudio/extensibility/internals/brokered-service-essentials?view=vs-2022).

It includes infrastructure for in-proc brokered service sharing as well as sharing across processes on the same machine and user account over named pipes.
Other transports are also supported, provided they provide a duplex pipe.
