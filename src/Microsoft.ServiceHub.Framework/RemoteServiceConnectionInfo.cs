// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes how to connect to a provisioned remote service.
/// </summary>
/// <remarks>
/// An initialized instance of this struct is expected to represent exactly one connection mechanism.
/// </remarks>
/// <devremarks>
/// When adding connection styles to this struct, be sure to add a value to <see cref="RemoteServiceConnections"/> to match.
/// </devremarks>
public struct RemoteServiceConnectionInfo
{
	/// <summary>
	/// Gets or sets the ID assigned to the service request that this instance is in response to.
	/// </summary>
	/// <remarks>
	/// This value is useful when canceling this service request without connecting to it.
	/// If null, no resources are allocated for this service prior to the client connecting to it,
	/// and thus no resources need to be released if the client decides not to connect.
	/// </remarks>
	public Guid? RequestId { get; set; }

	/// <summary>
	/// Gets or sets the ID of the channel created over the remote service broker's multiplexing stream where the activated service is listening.
	/// </summary>
	public ulong? MultiplexingChannelId { get; set; }

	/// <summary>
	/// Gets or sets the name of an IPC pipe to connect to where the activated service is listening.
	/// On Windows this is a named pipe, whereas on OSX/Linux this is the path to a unix domain socket.
	/// </summary>
	public string? PipeName { get; set; }

	/// <summary>
	/// Gets or sets instructions to activate the service within the client process.
	/// </summary>
	public LocalCLRServiceActivation ClrActivation { get; set; }

	/// <summary>
	/// Gets a value indicating whether this instance represents no connection information.
	/// </summary>
	public bool IsEmpty => !this.RequestId.HasValue && !this.MultiplexingChannelId.HasValue && string.IsNullOrWhiteSpace(this.PipeName) && this.ClrActivation == null;

	/// <summary>
	/// Checks whether this instance contains instructions for any of a set of connection types.
	/// </summary>
	/// <param name="connections">The set of connection types to test for.</param>
	/// <returns>
	/// <see langword="true"/> if any of the <paramref name="connections"/> specified coincide with instructions available in this value; <see langword="false"/> otherwise.
	/// <see langword="false"/> is returned if <paramref name="connections"/> is set to <see cref="RemoteServiceConnections.None"/>.
	/// </returns>
	public bool IsOneOf(RemoteServiceConnections connections)
	{
		if (connections.HasFlag(RemoteServiceConnections.ClrActivation) && this.ClrActivation is object)
		{
			return true;
		}

		if (connections.HasFlag(RemoteServiceConnections.IpcPipe) && !string.IsNullOrWhiteSpace(this.PipeName))
		{
			return true;
		}

		if (connections.HasFlag(RemoteServiceConnections.Multiplexing) && this.MultiplexingChannelId.HasValue)
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Throws an exception if the connection info is non-empty yet contains only activation details that
	/// are not supported.
	/// </summary>
	/// <param name="allowedConnections">The set of supported activation details.</param>
	internal void ThrowIfOutsideAllowedConnections(RemoteServiceConnections allowedConnections)
	{
		if (!this.IsEmpty && !this.IsOneOf(allowedConnections))
		{
			throw new Exception("Remote service broker responded with an unsupported connection type.");
		}
	}

	/// <summary>
	/// Describes activation instructions for a CLR-based service.
	/// </summary>
	public class LocalCLRServiceActivation
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="LocalCLRServiceActivation"/> class.
		/// </summary>
		/// <param name="assemblyPath">the local path to the assembly to be loaded.</param>
		/// <param name="fullTypeName">the full name (not including assembly name qualifier) of the type that serves as the entrypoint and (if applicable) the RPC target for the service.</param>
		public LocalCLRServiceActivation(string assemblyPath, string fullTypeName)
		{
			this.AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
			this.FullTypeName = fullTypeName ?? throw new ArgumentNullException(nameof(fullTypeName));
		}

		/// <summary>
		/// Gets the local path to the assembly to be loaded.
		/// </summary>
		public string AssemblyPath { get; }

		/// <summary>
		/// Gets the full name (not including assembly name qualifier) of the type
		/// that serves as the entrypoint and (if applicable) the RPC target for the service.
		/// </summary>
		public string FullTypeName { get; }
	}
}
