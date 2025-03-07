// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETFRAMEWORK

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.ServiceHub.Framework;
using Nerdbank.Streams;
using Newtonsoft.Json;

public class ServiceJsonRpcDescriptorAssemblyLoadTests : TestBase
{
	public ServiceJsonRpcDescriptorAssemblyLoadTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	/// <summary>
	/// The interface intentionally uses attributes from both MessagePack.Annotations and Newtonsoft.Json assemblies
	/// so that when SkipClrVisibilityChecks looks for attributes on this interface, it may load the assemblies that define other attributes.
	/// </summary>
	/// <remarks>
	/// This interface is internal because when it's public, we can't avoid the assembly load.
	/// </remarks>
	[JsonObject, Union(0, typeof(object))]
	public interface IPublicServiceData
	{
	}

	/// <summary>
	/// This service interface intentionally uses a custom interface as a generic type argument in a method
	/// to exercise our SkipClrVisibilityChecks code that loads attributes.
	/// </summary>
	/// <remarks>
	/// This service interface is attributed with a hack to skip the embedded types check to avoid loading assemblies
	/// that define non-embeddable attributes on interface types.
	/// </remarks>
	[SkipEmbeddableTypesCheck]
	public interface IPublicService
	{
		Task Test(IReadOnlyList<IPublicServiceData> list);
	}

	/// <summary>
	/// The interface intentionally uses attributes from both MessagePack.Annotations and Newtonsoft.Json assemblies
	/// so that when SkipClrVisibilityChecks looks for attributes on this interface, it may load the assemblies that define other attributes.
	/// </summary>
	/// <remarks>
	/// This interface is internal because when it's public, we can't avoid the assembly load.
	/// </remarks>
	[JsonObject, Union(0, typeof(object))]
	internal interface IInternalServiceData
	{
	}

	/// <summary>
	/// This service interface intentionally uses a custom interface as a generic type argument in a method
	/// to exercise our SkipClrVisibilityChecks code that loads attributes.
	/// </summary>
	internal interface IInternalService
	{
		Task Test(IReadOnlyList<IInternalServiceData> list);
	}

	[Fact]
	public void LocalProxyDoesNotLoadSerializationAssemblies()
	{
		AppDomain testDomain = CreateTestAppDomain();
		try
		{
			var driver = (AppDomainTestDriver)testDomain.CreateInstanceAndUnwrap(typeof(AppDomainTestDriver).Assembly.FullName, typeof(AppDomainTestDriver).FullName);

			this.PrintLoadedAssemblies(driver);

			driver.AccessNewtonsoftJsonDescriptor(localProxy: true);
			driver.AccessMessagePackDescriptor(localProxy: true);

			this.PrintLoadedAssemblies(driver);

			driver.ThrowIfAssembliesLoaded("MessagePack");
			driver.ThrowIfAssembliesLoaded("MessagePack.Annotations");
			driver.ThrowIfAssembliesLoaded("Newtonsoft.Json");
		}
		finally
		{
			AppDomain.Unload(testDomain);
		}
	}

	[Fact]
	public void MessagePackDoesNotLoadUnnecessarily()
	{
		AppDomain testDomain = CreateTestAppDomain();
		try
		{
			var driver = (AppDomainTestDriver)testDomain.CreateInstanceAndUnwrap(typeof(AppDomainTestDriver).Assembly.FullName, typeof(AppDomainTestDriver).FullName);

			this.PrintLoadedAssemblies(driver);

			driver.AccessNewtonsoftJsonDescriptor(localProxy: false);

			this.PrintLoadedAssemblies(driver);
			driver.ThrowIfAssembliesLoaded("MessagePack");
			driver.ThrowIfAssembliesLoaded("MessagePack.Annotations");
		}
		finally
		{
			AppDomain.Unload(testDomain);
		}
	}

	[Fact]
	public void NewtonsoftJsonDoesNotLoadUnnecessarily()
	{
		AppDomain testDomain = CreateTestAppDomain();
		try
		{
			var driver = (AppDomainTestDriver)testDomain.CreateInstanceAndUnwrap(typeof(AppDomainTestDriver).Assembly.FullName, typeof(AppDomainTestDriver).FullName);

			this.PrintLoadedAssemblies(driver);

			driver.AccessMessagePackDescriptor(localProxy: false);

			this.PrintLoadedAssemblies(driver);
			driver.ThrowIfAssembliesLoaded("Newtonsoft.Json");
		}
		finally
		{
			AppDomain.Unload(testDomain);
		}
	}

	private static AppDomain CreateTestAppDomain([CallerMemberName] string testMethodName = "") => AppDomain.CreateDomain($"Test: {testMethodName}", null, AppDomain.CurrentDomain.SetupInformation);

	private IEnumerable<string> PrintLoadedAssemblies(AppDomainTestDriver driver)
	{
		string[] assembliesLoaded = driver.GetLoadedAssemblyList();
		this.Logger.WriteLine($"Loaded assemblies: {Environment.NewLine}{string.Join(Environment.NewLine, assembliesLoaded.OrderBy(s => s).Select(s => "   " + s))}");
		return assembliesLoaded;
	}

#pragma warning disable CA1812 // Avoid uninstantiated internal classes
	private class AppDomainTestDriver : MarshalByRefObject
#pragma warning restore CA1812 // Avoid uninstantiated internal classes
	{
#pragma warning disable CA1822 // Mark members as static -- all members must be instance for marshalability

		private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("SomeMoniker");

		private readonly Dictionary<string, StackTrace> loadingStacks = new Dictionary<string, StackTrace>(StringComparer.OrdinalIgnoreCase);

		public AppDomainTestDriver()
		{
			AppDomain.CurrentDomain.AssemblyLoad += (s, e) =>
			{
				string simpleName = e.LoadedAssembly.GetName().Name;
				if (!this.loadingStacks.ContainsKey(simpleName))
				{
					this.loadingStacks.Add(simpleName, new StackTrace(skipFrames: 2, fNeedFileInfo: true));
				}
			};
		}

		internal string[] GetLoadedAssemblyList() => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToArray();

		internal void ThrowIfAssembliesLoaded(params string[] assemblyNames)
		{
			foreach (string assemblyName in assemblyNames)
			{
				if (this.loadingStacks.TryGetValue(assemblyName, out StackTrace? loadingStack))
				{
					throw new Exception($"Assembly {assemblyName} was loaded unexpectedly by the test with this stack trace: {Environment.NewLine}{loadingStack}");
				}
			}
		}

		internal void AccessNewtonsoftJsonDescriptor(bool localProxy)
		{
			ExerciseDescriptor(localProxy, new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null));
		}

		internal void AccessMessagePackDescriptor(bool localProxy)
		{
			ExerciseDescriptor(localProxy, new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null));
		}

		private static void ExerciseDescriptor(bool localProxy, ServiceRpcDescriptor descriptor)
		{
			if (localProxy)
			{
				descriptor.ConstructLocalProxy<IPublicService>(new Service());
				descriptor.ConstructLocalProxy<IInternalService>(new Service());
			}
			else
			{
				var duplexPipe = new DuplexPipe(new Pipe().Reader);
				descriptor.ConstructRpc(new object(), duplexPipe);
			}
		}

		internal class Service : IInternalService, IPublicService
		{
			public Task Test(IReadOnlyList<IInternalServiceData> list)
			{
				throw new NotImplementedException();
			}

			public Task Test(IReadOnlyList<IPublicServiceData> list)
			{
				throw new NotImplementedException();
			}
		}

		internal class ServiceData
		{
		}

#pragma warning restore CA1822 // Mark members as static
	}

	[AttributeUsage(AttributeTargets.Interface)]
	private class SkipEmbeddableTypesCheckAttribute : Attribute
	{
	}
}

#endif
