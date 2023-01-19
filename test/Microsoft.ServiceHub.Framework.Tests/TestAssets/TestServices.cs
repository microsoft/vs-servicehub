// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

internal static class TestServices
{
	internal static readonly ServiceRpcDescriptor Echo = new ServiceJsonRpcDescriptor(new ServiceMoniker(nameof(Echo)), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
	internal static readonly ServiceRpcDescriptor Calculator = new ServiceJsonRpcDescriptor(new ServiceMoniker(nameof(Calculator)), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
	internal static readonly ServiceRpcDescriptor CallMeBack = new ServiceJsonRpcDescriptor(new ServiceMoniker(nameof(CallMeBackService)), typeof(ICallMeBackClient), ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
	internal static readonly ServiceRpcDescriptor DoesNotExist = new ServiceJsonRpcDescriptor(new ServiceMoniker(nameof(DoesNotExist)), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
	internal static readonly ServiceRpcDescriptor Throws = new ServiceJsonRpcDescriptor(new ServiceMoniker("Throws"), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
}
