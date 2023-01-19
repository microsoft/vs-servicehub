import { MessageConnection } from 'vscode-jsonrpc'

/** An additional interface implemented by ServiceJsonRpcDescriptor-generated proxies. */
export interface IJsonRpcClientProxy {
	/** Gets the underlying vscode-jsonrpc MessageConnection object. */
	_jsonRpc: MessageConnection
}
