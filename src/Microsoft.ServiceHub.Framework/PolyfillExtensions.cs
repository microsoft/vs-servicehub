﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1403 // File may only contain a single namespace

using System.IO.Pipes;

namespace Microsoft.ServiceHub.Framework
{
	/// <summary>
	/// Extension methods to make multi-targeting require fewer <c>#if</c> regions.
	/// </summary>
	internal static class PolyfillExtensions
	{
#if !NET5_0_OR_GREATER
		/// <summary>
		/// When used to create a <see cref="NamedPipeServerStream"/> instance, indicates
		/// that the pipe can only be connected to a client created by the same user. When
		/// used to create a <see cref="NamedPipeClientStream"/> instance, indicates that
		/// the pipe can only connect to a server created by the same user. On Windows, it
		/// verifies both the user account and elevation level.
		/// </summary>
		/// <remarks>
		/// .NET implements this, but on .NET Framework we have to implement it ourselves.
		/// .NET's implementations are available as a template for us to follow:
		/// <see href="https://github.com/dotnet/runtime/blob/220437ef6591bee5907ed097b5e193a1d1235dca/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L102-L113">server</see> and
		/// <see href="https://github.com/dotnet/runtime/blob/220437ef6591bee5907ed097b5e193a1d1235dca/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeClientStream.Windows.cs#L141-L151">client</see>.
		/// </remarks>
		internal const PipeOptions PipeOptionsCurrentUserOnly = (PipeOptions)0x2000_0000;

		/// <summary>
		/// Disposes the stream.
		/// </summary>
		/// <param name="stream">The stream to be disposed.</param>
		/// <returns>A task.</returns>
		internal static ValueTask DisposeAsync(this Stream stream)
		{
			stream.Dispose();
			return default;
		}
#endif
	}
}

#if !NET5_0_OR_GREATER

namespace System.Runtime.Versioning
{
	/// <summary>
	/// Annotates a custom guard field, property or method with a supported platform name and optional version.
	/// Multiple attributes can be applied to indicate guard for multiple supported platforms.
	/// </summary>
	/// <remarks>
	/// Callers can apply a <see cref="System.Runtime.Versioning.SupportedOSPlatformGuardAttribute " /> to a field, property or method
	/// and use that field, property or method in a conditional or assert statements in order to safely call platform specific APIs.
	///
	/// The type of the field or property should be boolean, the method return type should be boolean in order to be used as platform guard.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
	internal sealed class SupportedOSPlatformGuardAttribute : OSPlatformAttribute
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="SupportedOSPlatformGuardAttribute"/> class.
		/// </summary>
		/// <param name="platformName">The platform.</param>
		internal SupportedOSPlatformGuardAttribute(string platformName)
			: base(platformName)
		{
		}
	}

	/// <summary>
	/// Records the operating system (and minimum version) that supports an API. Multiple attributes can be
	/// applied to indicate support on multiple operating systems.
	/// </summary>
	/// <remarks>
	/// Callers can apply a <see cref="System.Runtime.Versioning.SupportedOSPlatformAttribute " />
	/// or use guards to prevent calls to APIs on unsupported operating systems.
	///
	/// A given platform should only be specified once.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Enum | AttributeTargets.Event | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Module | AttributeTargets.Property | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
	internal sealed class SupportedOSPlatformAttribute : OSPlatformAttribute
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="SupportedOSPlatformAttribute"/> class.
		/// </summary>
		/// <param name="platformName">The platform name.</param>
		internal SupportedOSPlatformAttribute(string platformName)
			: base(platformName)
		{
		}
	}

	/// <summary>
	/// Marks APIs that were removed or are unsupported in a given operating system version.
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
	internal sealed class UnsupportedOSPlatformAttribute : OSPlatformAttribute
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="UnsupportedOSPlatformAttribute" /> class
		/// for the specified unsupported OS platform.
		/// </summary>
		/// <param name="platformName">The unsupported OS platform name, optionally including a version.</param>
		internal UnsupportedOSPlatformAttribute(string platformName)
			: base(platformName)
		{
		}
	}

	/// <summary>
	/// Base type for all platform-specific API attributes.
	/// </summary>
	internal abstract class OSPlatformAttribute : Attribute
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="OSPlatformAttribute"/> class.
		/// </summary>
		/// <param name="platformName">The platform name.</param>
		private protected OSPlatformAttribute(string platformName)
		{
		}
	}
}

#endif
