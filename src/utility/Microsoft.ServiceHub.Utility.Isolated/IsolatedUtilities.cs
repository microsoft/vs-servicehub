// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// Contains various utility methods without any non-framework dependencies.
/// </summary>
internal static class IsolatedUtilities
{
	private static readonly string HomeEnvVar = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
	private static readonly string LogNameEnvVar = Environment.GetEnvironmentVariable("LOGNAME") ?? string.Empty;
	private static readonly string UserEnvVar = Environment.GetEnvironmentVariable("USER") ?? string.Empty;
	private static readonly string LNameEnvVar = Environment.GetEnvironmentVariable("LNAME") ?? string.Empty;
	private static readonly string UsernameEnvVar = Environment.GetEnvironmentVariable("USERNAME") ?? string.Empty;

	/// <summary>
	/// Throws an exception if the specified parameter's value is null.
	/// </summary>
	/// <param name="obj">The value of the argument.</param>
	/// <param name="name">The name of the parameter to include in any thrown exception.</param>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is <see langword="null"/>.</exception>
	internal static void RequiresNotNull(object? obj, string name)
	{
		if (obj == null)
		{
			throw new ArgumentNullException(name);
		}
	}

	/// <summary>
	/// Throws an exception if the specified parameter's value is null,
	/// has no elements or has an element with a null value.
	/// </summary>
	/// <param name="obj">The value of the argument.</param>
	/// <param name="name">The name of the parameter to include in any thrown exception.</param>
	/// <exception cref="ArgumentException">Thrown if the tested condition is false.</exception>
	internal static void RequiresNotNullOrEmpty(string? obj, string name)
	{
		if (obj == null)
		{
			throw new ArgumentNullException(name);
		}

		if (obj.Length == 0 || obj[0] == '\0')
		{
			throw new ArgumentException(string.Format(UtilityResources.Argument_EmptyString, name), name);
		}
	}

	/// <summary>
	/// Throws an exception if the specified parameter's value is null, empty, or whitespace.
	/// </summary>
	/// <param name="obj">The value of the argument.</param>
	/// <param name="name">The name of the parameter to include in any thrown exception.</param>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">Thrown if <paramref name="obj"/> is empty.</exception>
	internal static void RequiresNotNullOrWhiteSpace(string? obj, string name)
	{
		if (obj == null)
		{
			throw new ArgumentNullException(name);
		}

		if (obj.Length == 0 || obj[0] == '\0')
		{
			throw new ArgumentException(string.Format(UtilityResources.Argument_EmptyString, name), name);
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(string.Format(UtilityResources.Argument_Whitespace, name), name);
		}
	}

	/// <summary>
	/// Throws an <see cref="ArgumentOutOfRangeException"/> if a condition does not evaluate to true.
	/// </summary>
	/// <param name="range">A conditional statement indicating whether a range is valid.</param>
	/// <param name="name">The name of the parameter to include in any thrown exception.</param>
	/// <param name="errorMessage">The error message to us if the condition is false.</param>
	internal static void RequiresRange(bool range, string name, string errorMessage)
	{
		if (!range)
		{
			throw new ArgumentOutOfRangeException(name, errorMessage);
		}
	}

	/// <summary>
	/// Combines the provided baseDirectory path with the relativePath, or return null if the relativePath is null.
	/// </summary>
	/// <param name="baseDirectory">The directory to be used as the root.</param>
	/// <param name="relativePath">A relative path, or null.</param>
	/// <returns>A combination of the baseDirectory and relativePath, or null if the relativePath is null.</returns>
	internal static string? CombineRelativePath(string baseDirectory, string? relativePath)
	{
		IsolatedUtilities.RequiresNotNullOrEmpty(baseDirectory, nameof(baseDirectory));

		if (relativePath is null)
		{
			return relativePath;
		}

		return Path.Combine(baseDirectory, relativePath);
	}

	/// <summary>
	/// Given an input calculates the SHA256 hash of it.
	/// </summary>
	/// <param name="input">The string to hash.</param>
	/// <returns>A hash of the input string.</returns>
	internal static string GetSHA256Hash(string input)
	{
		using (SHA256 sha256Hash = SHA256.Create())
		{
			byte[] hash = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

			StringBuilder sb = new StringBuilder(hash.Length * 2);
			for (int i = 0; i < hash.Length; i++)
			{
				sb.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
			}

			return sb.ToString();
		}
	}

	/// <summary>
	/// Gets whether or not the current platform is Windows.
	/// </summary>
	/// <returns>True if the current platform is Windows, false otherwise.</returns>
#if NET5_0_OR_GREATER
	[SupportedOSPlatformGuard("windows6.0.6000")]
#endif
	internal static bool IsWindowsPlatform()
	{
#if NET5_0_OR_GREATER
		return OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000);
#else
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.OSVersion.Version > new Version(6, 0, 6000);
#endif
	}

	/// <summary>
	/// Gets whether or not the current platform is OSX.
	/// </summary>
	/// <returns>True if the current platform is OSX, false otherwise.</returns>
#if NET5_0_OR_GREATER
	[SupportedOSPlatformGuard("macos")]
#endif
	internal static bool IsMacPlatform()
	{
		return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
	}

	/// <summary>
	/// Gets whether or not the current platform is Linux.
	/// </summary>
	/// <returns>True if the current platform is Linux, false otherwise.</returns>
#if NET5_0_OR_GREATER
	[SupportedOSPlatformGuard("linux")]
#endif
	internal static bool IsLinuxPlatform()
	{
		return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
	}

	/// <summary>
	/// Gets whether or not the current platform is Windows 7 or 2008 R2.
	/// </summary>
	/// <returns>True if the current platform is Windows 7 or 2008 R2, false otherwise.</returns>
	internal static bool IsWindows7Or2008R2()
	{
		if (IsWindowsPlatform())
		{
			// https://msdn.microsoft.com/en-us/library/system.environment.osversion%28v=vs.110%29.aspx
			// Starting with windows 8 the OSVersion property will return the same value for all windows versions
			// Under the covers it calls GetVersion or GetVersionEx which will always return 6.2 as the version number when running on windows 8 or higher when the application is not manifested with supported OS versions.
			// However in this case we only need to detect if we are on an OS greater than windows 7, per https://docs.microsoft.com/en-us/windows/desktop/api/winnt/ns-winnt-_osversioninfoa
			// Windows 7 / Server 2008R2 will return 6.1 while windows 8 and higher will return 6.2 (for un manifested applications) or greater (if the application has a manifest that indicates windows 8.1 or greater support)
			// Since we only need to know if we are less than windows 8 the OSVersion method can still be used.
			//
			// If differentiation between windows 8 or higher versions is required then you need to use https://docs.microsoft.com/en-us/windows/desktop/SysInfo/version-helper-apis but these are just macros defined in VersionHelpers.h
			// so are not accessible from managed code
			OperatingSystem os = Environment.OSVersion;
			if (os.Platform == PlatformID.Win32NT)
			{
				Version v = os.Version;
				return v.Major == 6 && v.Minor == 1;
			}
		}

		return false;
	}

	/// <summary>
	/// Gets the base directory to be used by ServiceHub on *nix platforms.
	/// </summary>
	/// <returns>"{userhomedir}/.ServiceHub" for *nix platforms.</returns>
	internal static string GetDevHubBaseDirForUnix()
	{
		return Path.Combine(GetUserHomeDirOnUnix(), ".ServiceHub");
	}

	/// <summary>
	/// Gets a Unix socket directory.
	/// </summary>
	/// <param name="locationServiceChannelName">The channel to be used for the socket.</param>
	/// <returns>The socket directory.</returns>
	internal static string GetUnixSocketDir(string locationServiceChannelName)
	{
		return Path.Combine(GetDevHubBaseDirForUnix(), locationServiceChannelName);
	}

	/// <summary>
	/// Gets a Unix socket directory.
	/// </summary>
	/// <param name="channelName">The multiplexing channel to be used for the socket.</param>
	/// <param name="locationServiceChannelName">The base channel to be used for the socket.</param>
	/// <returns>The socket directory.</returns>
	internal static string GetUnixSocketDir(string channelName, string locationServiceChannelName)
	{
		return Path.Combine(GetUnixSocketDir(locationServiceChannelName), channelName);
	}

	private static string GetUserHomeDirOnUnix()
	{
		if (IsWindowsPlatform())
		{
			throw new NotImplementedException();
		}

		if (!string.IsNullOrEmpty(HomeEnvVar))
		{
			return HomeEnvVar;
		}

		string username = string.Empty;
		if (!string.IsNullOrEmpty(LogNameEnvVar))
		{
			username = LogNameEnvVar;
		}
		else if (!string.IsNullOrEmpty(UserEnvVar))
		{
			username = UserEnvVar;
		}

		if (!string.IsNullOrEmpty(LNameEnvVar))
		{
			username = LNameEnvVar;
		}

		if (!string.IsNullOrEmpty(UsernameEnvVar))
		{
			username = UsernameEnvVar;
		}

		if (IsMacPlatform())
		{
			return !string.IsNullOrEmpty(username) ? Path.Combine("/Users", username) : string.Empty;
		}
		else if (IsLinuxPlatform())
		{
			if (Linux.NativeMethods.getuid() == Linux.NativeMethods.RootUserId)
			{
				return "/root";
			}
			else
			{
				return !string.IsNullOrEmpty(username) ? Path.Combine("/home", username) : string.Empty;
			}
		}
		else
		{
			throw new NotImplementedException();
		}
	}
}
