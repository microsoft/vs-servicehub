// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1310 // Avoid underscores in member names

namespace Microsoft.ServiceHub.Framework;

[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "Value of string constants are self explanatory")]
internal static class UtilityResources
{
	public const string Argument_EmptyString = "'{0}' cannot be an empty string (\"\") or start with the null character.";
	public const string Argument_Whitespace = "The parameter '{0}' cannot be entirely made up of empty spaces.";
	public const string ValidateError_InvalidOperation = "The operation cannot be executed at this time. The object {0} is not null.";
	public const string ValidateError_StringEmpty = "The string is empty.";
	public const string ValidateError_GuidEmpty = "The Guid is empty.";
	public const string ValidateError_StringWhiteSpace = "The string cannot be null or contain only whitespace characters.";
	public const string ValidateError_InvalidValue_Format = "The argument value {0} is not the expected value {1}.";
	public const string ValidateError_UnexpectedValue_Format = "The value {0} is unexpected for argument {1}.";
	public const string ValidateError_OutOfRange_Format = "The value {0} is outside the acceptable range of [{1},{2}].";
	public const string FormattedMessageCannotBeNullMessage = "The FormattedMessage cannot be null or empty";
	public const string UnixSocket_CreateFailed = "Unable to create socket.";
	public const string UnixSocket_ListenInvalidOperation = "Listen: Operation allowed only on server socket";
	public const string UnixSocket_ListenFailed = "Unable to listen to server socket.";
	public const string UnixSocket_ConnectFailed = "Can't connect to socket.";
	public const string UnixSocketChannel_WriteFailed = "Unable to write to socket.";
	public const string UnixSocketChannel_ReadFailed = "Unable to read data from socket.";
	public const string UnixSocketChannel_SocketAlreadyClosed = "Socket already closed.";
	public const string UnixDomainSocketsAreNotSupportedOnWindows = "Unix domain sockets are not supported on Windows.";
}
