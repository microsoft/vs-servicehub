// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;
using StreamJsonRpc;

(System.IO.Pipelines.IDuplexPipe clientPipe, System.IO.Pipelines.IDuplexPipe serverPipe) = FullDuplexStream.CreatePipePair();
using ServiceRpcDescriptor.RpcConnection connection = FrameworkServices.RemoteServiceBroker.ConstructRpcConnection(clientPipe);
connection.ConstructRpcClient<IRemoteServiceBroker>();
serverPipe.Input.Complete();
serverPipe.Output.Complete();
