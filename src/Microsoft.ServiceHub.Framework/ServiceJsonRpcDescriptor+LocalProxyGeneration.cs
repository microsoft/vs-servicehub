// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Uncomment the SaveAssembly symbol and run one test to save the generated DLL for inspection in ILSpy as part of debugging.
#if NETFRAMEWORK
////#define SaveAssembly
#endif

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Microsoft.ServiceHub.Framework;

/// <content>
/// The <see cref="LocalProxyGeneration"/> nested class.
/// </content>
public partial class ServiceJsonRpcDescriptor
{
	/// <summary>
	/// Creates and caches proxies generated to wrap local target objects for the <see cref="ConstructLocalProxy{T}(T)"/> method.
	/// </summary>
	private static class LocalProxyGeneration
	{
		private static readonly List<(ImmutableHashSet<AssemblyName> SkipVisibilitySet, ModuleBuilder Builder)> TransparentProxyModuleBuilderByVisibilityCheck = new List<(ImmutableHashSet<AssemblyName>, ModuleBuilder)>();
		private static readonly object BuilderLock = new object();

		private static readonly Dictionary<ReadOnlyMemory<Type>, TypeInfo> GeneratedProxiesByInterface = new(TypeArrayUnorderedEqualityComparer.Instance);

		private static readonly ConstructorInfo ObjectCtor = typeof(object).GetTypeInfo().DeclaredConstructors.Single();
		private static readonly MethodInfo IDisposableDisposeMethod = typeof(IDisposable).GetTypeInfo().GetRuntimeMethod(nameof(IDisposable.Dispose), Type.EmptyTypes)!;
		private static readonly MethodInfo IDisposableObservableIsDisposedGetterMethod = typeof(IDisposableObservable).GetTypeInfo().GetRuntimeProperty(nameof(IDisposableObservable.IsDisposed))!.GetMethod!;
		private static readonly MethodInfo INotifyDisposableAddHandler = typeof(INotifyDisposable).GetTypeInfo().GetRuntimeEvent(nameof(INotifyDisposable.Disposed))!.AddMethod!;
		private static readonly MethodInfo INotifyDisposableRemoveHandler = typeof(INotifyDisposable).GetTypeInfo().GetRuntimeEvent(nameof(INotifyDisposable.Disposed))!.RemoveMethod!;
		private static readonly ConstructorInfo ObjectDisposedExceptionCtor = typeof(ObjectDisposedException).GetTypeInfo().GetConstructor(new Type[] { typeof(string) })!;
		private static readonly MethodInfo ExceptionHelperMethod = typeof(LocalProxyGeneration).GetTypeInfo().GetMethod(nameof(ExceptionHelper), BindingFlags.Static | BindingFlags.NonPublic)!;
		private static readonly MethodInfo ReturnedTaskHelperMethod = typeof(LocalProxyGeneration).GetTypeInfo().GetMethod(nameof(ReturnedTaskHelperAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
		private static readonly MethodInfo ReturnedTaskOfTHelperMethod = typeof(LocalProxyGeneration).GetTypeInfo().GetMethod(nameof(ReturnedTaskOfTHelperAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
		private static readonly MethodInfo ReturnedValueTaskHelperMethod = typeof(LocalProxyGeneration).GetTypeInfo().GetMethod(nameof(ReturnedValueTaskHelperAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
		private static readonly MethodInfo ReturnedValueTaskOfTHelperMethod = typeof(LocalProxyGeneration).GetTypeInfo().GetMethod(nameof(ReturnedValueTaskOfTHelperAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
		private static readonly MethodInfo ThrowIfCancellationRequestedMethod = typeof(CancellationToken).GetTypeInfo().GetMethod(nameof(CancellationToken.ThrowIfCancellationRequested), BindingFlags.Instance | BindingFlags.Public)!;
		private static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!;
		private static readonly MethodInfo InterlockedExchangeMethod = typeof(Interlocked).GetMethod(nameof(Interlocked.Exchange), BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(object).MakeByRefType(), typeof(object) }, null)!;
		private static readonly MethodInfo InterlockedCompareExchangeMethod = typeof(Interlocked).GetMethod(nameof(Interlocked.CompareExchange), BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(object).MakeByRefType(), typeof(object), typeof(object) }, null)!;
		private static readonly FieldInfo EventArgsEmptyField = typeof(EventArgs).GetField(nameof(EventArgs.Empty))!;
		private static readonly MethodInfo EventHandlerInvokeMethod = typeof(EventHandler).GetMethod(nameof(EventHandler.Invoke))!;
		private static readonly MethodInfo DelegateCombineMethod = typeof(Delegate).GetMethod(nameof(Delegate.Combine), new Type[] { typeof(Delegate), typeof(Delegate) })!;
		private static readonly MethodInfo DelegateRemoveMethod = typeof(Delegate).GetMethod(nameof(Delegate.Remove))!;
		private static readonly MethodInfo CreateProxyMethod = typeof(LocalProxyGeneration).GetTypeInfo().GetMethod(nameof(CreateProxyHelper), BindingFlags.Static | BindingFlags.NonPublic)!;
		private static readonly MethodInfo ConstructLocalProxyMethod = typeof(IJsonRpcLocalProxy).GetMethod(nameof(IJsonRpcLocalProxy.ConstructLocalProxy))!;
		private static readonly Type[] EventHandlerTypeInArray = new Type[] { typeof(EventHandler) };

		internal static T CreateProxy<T>(T target, ReadOnlySpan<Type> additionalInterfaces, ExceptionProcessing exceptionStrategy)
			where T : class
		{
			Requires.NotNull(target, nameof(target));

			TypeInfo proxyType = Get(typeof(T), additionalInterfaces);
			try
			{
				T? result = (T?)Activator.CreateInstance(proxyType, target, exceptionStrategy);
				if (result is null)
				{
					throw new ServiceCompositionException("Unable to activate proxy type.");
				}

				return result;
			}
			catch (TargetInvocationException ex)
			{
				throw new ServiceCompositionException("Unable to activate proxy type.", ex.InnerException);
			}
		}

		/// <summary>
		/// Gets the <see cref="ModuleBuilder"/> to use for generating a proxy for the given type.
		/// </summary>
		/// <param name="interfaceTypes">The types of the interfaces the proxy will implement.</param>
		/// <returns>The <see cref="ModuleBuilder"/> to use.</returns>
		private static ModuleBuilder GetProxyModuleBuilder(Type[] interfaceTypes)
		{
			Assumes.True(Monitor.IsEntered(BuilderLock));

			// Dynamic assemblies are relatively expensive. We want to create as few as possible.
			// For each set of skip visibility check assemblies, we need a dynamic assembly that skips at *least* that set.
			// The CLR will not honor any additions to that set once the first generated type is closed.
			// We maintain a dictionary to point at dynamic modules based on the set of skip visibility check assemblies they were generated with.
			ImmutableHashSet<AssemblyName> skipVisibilityCheckAssemblies = ImmutableHashSet.CreateRange(interfaceTypes.SelectMany(t => SkipClrVisibilityChecks.GetSkipVisibilityChecksRequirements(t.GetTypeInfo())))
				.Add(ExceptionHelperMethod.DeclaringType!.Assembly.GetName());
			foreach ((ImmutableHashSet<AssemblyName> SkipVisibilitySet, ModuleBuilder Builder) existingSet in TransparentProxyModuleBuilderByVisibilityCheck)
			{
				if (existingSet.SkipVisibilitySet.IsSupersetOf(skipVisibilityCheckAssemblies))
				{
					return existingSet.Builder;
				}
			}

			// As long as we're going to start a new module, let's maximize the chance that this is the last one
			// by skipping visibility checks on ALL assemblies loaded so far.
			// I have disabled this optimization though till we need it since it would sometimes cover up any bugs in the above visibility checking code.
			////skipVisibilityCheckAssemblies = skipVisibilityCheckAssemblies.Union(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName()));

			AssemblyBuilder assemblyBuilder = CreateProxyAssemblyBuilder(skipVisibilityCheckAssemblies);
			ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("rpcProxies");
			var skipClrVisibilityChecks = new SkipClrVisibilityChecks(assemblyBuilder, moduleBuilder);
			skipClrVisibilityChecks.SkipVisibilityChecksFor(skipVisibilityCheckAssemblies);
			TransparentProxyModuleBuilderByVisibilityCheck.Add((skipVisibilityCheckAssemblies, moduleBuilder));

			return moduleBuilder;
		}

		private static AssemblyBuilder CreateProxyAssemblyBuilder(ImmutableHashSet<AssemblyName> assemblies)
		{
			var proxyAssemblyName = new AssemblyName(string.Format(CultureInfo.InvariantCulture, "localRpcProxies_{0}", GenerateGuidFromAssemblies(assemblies)));
#if SaveAssembly
			return AssemblyBuilder.DefineDynamicAssembly(proxyAssemblyName, AssemblyBuilderAccess.RunAndSave);
#else
			return AssemblyBuilder.DefineDynamicAssembly(proxyAssemblyName, AssemblyBuilderAccess.RunAndCollect);
#endif
		}

		private static Guid GenerateGuidFromAssemblies(ImmutableHashSet<AssemblyName> assemblies)
		{
			// To make dll load and JIT comparisons between builds easier, we generate a
			// consistent name for the proxy assembly based on the input assemblies involved.
			using var algorithm = SHA256.Create();

			foreach (AssemblyName name in assemblies.OrderBy(a => a.FullName, StringComparer.Ordinal))
			{
				byte[] bytes = Encoding.Unicode.GetBytes(name.FullName!);
				algorithm.TransformBlock(bytes, 0, bytes.Length, null, 0);
			}

			algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

			// SHA256 produces 32-bytes hash but we need only 16-bytes of them to produce a GUID,
			// this is not used for cryptographic purposes so we just grab the first half.
			byte[] guidBytes = new byte[16];
			Array.Copy(algorithm.Hash!, guidBytes, guidBytes.Length);

			return new Guid(guidBytes);
		}

		/// <summary>
		/// Gets the generated type for a proxy for a given interface.
		/// </summary>
		/// <param name="serviceInterface">The interface the proxy must implement.</param>
		/// <param name="additionalInterfaces">Additional interfaces that the proxy should implement.</param>
		/// <returns>The generated type.</returns>
		private static TypeInfo Get(Type serviceInterface, ReadOnlySpan<Type> additionalInterfaces)
		{
			Requires.NotNull(serviceInterface, nameof(serviceInterface));
			if (!serviceInterface.IsInterface)
			{
				throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ClientProxyTypeArgumentMustBeAnInterface, serviceInterface));
			}

			foreach (Type additionalInterface in additionalInterfaces)
			{
				if (!additionalInterface.IsInterface)
				{
					throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, Strings.ClientProxyTypeArgumentMustBeAnInterface, additionalInterface));
				}
			}

			TypeInfo? generatedType;

			lock (BuilderLock)
			{
				Type[] interfaces = [
					serviceInterface,
					.. additionalInterfaces,
					typeof(IDisposableObservable),
					typeof(INotifyDisposable),
					typeof(IJsonRpcLocalProxy),
				];

				if (GeneratedProxiesByInterface.TryGetValue(interfaces, out generatedType))
				{
					return generatedType;
				}

				ModuleBuilder proxyModuleBuilder = GetProxyModuleBuilder(interfaces);

				TypeBuilder proxyTypeBuilder = proxyModuleBuilder.DefineType(
					string.Format(CultureInfo.InvariantCulture, "_localproxy_{0}_{1}", serviceInterface.FullName, Guid.NewGuid()),
					TypeAttributes.Public,
					typeof(object),
					interfaces);
				Type proxyType = proxyTypeBuilder;
				const FieldAttributes fieldAttributes = FieldAttributes.Private;
				FieldBuilder disposedSentinelStaticField = proxyTypeBuilder.DefineField("DisposedSentinel", typeof(object), fieldAttributes | FieldAttributes.Static | FieldAttributes.InitOnly);
				FieldBuilder disposedField = proxyTypeBuilder.DefineField("disposed", typeof(object), fieldAttributes);
				FieldBuilder targetField = proxyTypeBuilder.DefineField("target", serviceInterface, fieldAttributes);
				FieldBuilder exceptionStrategyField = proxyTypeBuilder.DefineField("exceptionStrategy", typeof(ExceptionProcessing), fieldAttributes);

				EmitClassConstructor(proxyTypeBuilder, disposedSentinelStaticField);
				EmitConstructor(serviceInterface.GetTypeInfo(), additionalInterfaces, proxyTypeBuilder, targetField, exceptionStrategyField);
				EmitDisposeMethod(proxyTypeBuilder, targetField, disposedField, disposedSentinelStaticField);
				EmitIsDisposedProperty(proxyTypeBuilder, disposedField, disposedSentinelStaticField);
				EmitDisposedEvent(proxyTypeBuilder, disposedField, disposedSentinelStaticField);
				EmitJsonRpcLocalProxyMethods(proxyTypeBuilder, targetField, exceptionStrategyField);

				foreach (Type iface in (Type[])[serviceInterface, .. additionalInterfaces])
				{
					foreach (MethodInfo? method in FindAllOnThisAndOtherInterfaces(iface.GetTypeInfo(), i => i.DeclaredMethods).Where(m => !m.IsSpecialName))
					{
						// Check for specially supported methods from derived interfaces.
						if (Equals(DisposeMethod, method))
						{
							// This is unconditionally implemented earlier.
							continue;
						}

						EmitMethodThunk(iface.GetTypeInfo(), proxyTypeBuilder, targetField, exceptionStrategyField, method);
					}

					foreach (EventInfo? evt in FindAllOnThisAndOtherInterfaces(iface.GetTypeInfo(), i => i.DeclaredEvents))
					{
						if (evt.AddMethod is object && evt.RemoveMethod is object)
						{
							EmitEventThunk(proxyTypeBuilder, targetField, evt);
						}
					}
				}

				generatedType = proxyTypeBuilder.CreateTypeInfo()!;
				GeneratedProxiesByInterface.Add(interfaces, generatedType);

#if SaveAssembly
				((AssemblyBuilder)proxyModuleBuilder.Assembly).Save(proxyModuleBuilder.ScopeName);
				System.IO.File.Delete(proxyModuleBuilder.ScopeName + ".dll");
				System.IO.File.Move(proxyModuleBuilder.ScopeName, proxyModuleBuilder.ScopeName + ".dll");
#endif
			}

			return generatedType;
		}

		private static void EmitClassConstructor(TypeBuilder proxyTypeBuilder, FieldBuilder disposedSentinelValue)
		{
			ConstructorBuilder cctor = proxyTypeBuilder.DefineTypeInitializer();
			ILGenerator il = cctor.GetILGenerator();

			// DisposedSentinel = new object();
			il.Emit(OpCodes.Newobj, ObjectCtor);
			il.Emit(OpCodes.Stsfld, disposedSentinelValue);
			il.Emit(OpCodes.Ret);
		}

		private static void EmitConstructor(TypeInfo serviceInterface, ReadOnlySpan<Type> additionalInterfaces, TypeBuilder proxyTypeBuilder, FieldBuilder targetField, FieldBuilder exceptionStrategyField)
		{
			ConstructorBuilder ctor = proxyTypeBuilder.DefineConstructor(
				MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
				CallingConventions.Standard,
				new Type[] { serviceInterface, typeof(ExceptionProcessing) });
			ILGenerator il = ctor.GetILGenerator();

			// : base()
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Call, ObjectCtor);

			// Verify that the target implements all the additional interfaces required.
			Label insufficientInterfaces = il.DefineLabel();
			foreach (Type addlIface in additionalInterfaces)
			{
				// if (target is not addlIface) goto throwException;
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Isinst, addlIface);
				il.Emit(OpCodes.Brfalse_S, insufficientInterfaces);
			}

			// this.target = target;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Stfld, targetField);

			// this.exceptionStrategy = exceptionStrategy;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_2);
			il.Emit(OpCodes.Stfld, exceptionStrategyField);

			il.Emit(OpCodes.Ret);

			if (additionalInterfaces.Length > 0)
			{
				// throwException: throw new InvalidCastException();
				il.MarkLabel(insufficientInterfaces);
				il.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes)!);
				il.Emit(OpCodes.Throw);
				il.Emit(OpCodes.Ret);
			}
		}

		private static void EmitJsonRpcLocalProxyMethods(TypeBuilder typeBuilder, FieldBuilder targetField, FieldBuilder exceptionStrategyField)
		{
			MethodBuilder method = typeBuilder.DefineMethod(
				nameof(IJsonRpcLocalProxy.ConstructLocalProxy),
				MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final);

			// Define the generic type parameter T
			GenericTypeParameterBuilder typeParameter = method.DefineGenericParameters("T").First();
			typeParameter.SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint);
			method.SetReturnType(typeParameter);

			ILGenerator il = method.GetILGenerator();

			// Order of locals matters since we index into them.
			il.DeclareLocal(typeParameter);
			il.DeclareLocal(typeParameter);

			Label loadNullAndReturnLabel = il.DefineLabel();

			// T targetObject = this.target as T;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, targetField);
			il.Emit(OpCodes.Isinst, typeParameter);
			il.Emit(OpCodes.Unbox_Any, typeParameter);
			il.Emit(OpCodes.Stloc_0);

			// if (targetObject == null) return null;
			il.Emit(OpCodes.Ldloc_0);
			il.Emit(OpCodes.Box, typeParameter);
			il.Emit(OpCodes.Brfalse_S, loadNullAndReturnLabel);

			// CreateProxy(targetObject, exceptionStrategy);
			il.Emit(OpCodes.Ldloc_0);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, exceptionStrategyField);
			il.Emit(OpCodes.Call, CreateProxyMethod);
			il.Emit(OpCodes.Ret);

			il.MarkLabel(loadNullAndReturnLabel);
			il.Emit(OpCodes.Ldloca_S, 1);
			il.Emit(OpCodes.Initobj, typeParameter);
			il.Emit(OpCodes.Ldloc_1);
			il.Emit(OpCodes.Ret);

			typeBuilder.DefineMethodOverride(method, ConstructLocalProxyMethod);
		}

		private static void EmitDisposeMethod(TypeBuilder proxyTypeBuilder, FieldBuilder targetField, FieldBuilder disposedField, FieldBuilder disposedSentinelStaticField)
		{
			MethodBuilder disposeMethod = proxyTypeBuilder.DefineMethod(nameof(IDisposable.Dispose), MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual);
			ILGenerator il = disposeMethod.GetILGenerator();

			Label retLabel = il.DefineLabel();

			// object disposed = Interlocked.Exchange(ref this.disposed, DisposedSentinel);
			LocalBuilder disposedLocal = il.DeclareLocal(typeof(object));
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldflda, disposedField);
			il.Emit(OpCodes.Ldsfld, disposedSentinelStaticField);
			il.Emit(OpCodes.Call, InterlockedExchangeMethod);
			il.Emit(OpCodes.Stloc, disposedLocal);

			// if (disposed == DisposedSentinel) return;
			il.Emit(OpCodes.Ldloc, disposedLocal);
			il.Emit(OpCodes.Ldsfld, disposedSentinelStaticField);
			il.Emit(OpCodes.Beq_S, retLabel);

			// (this.target as IDisposable)?.Dispose();
			Label skipDisposeLabel = il.DefineLabel();
			Label disposedLabel = il.DefineLabel();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, targetField);
			il.Emit(OpCodes.Isinst, typeof(IDisposable));
			il.Emit(OpCodes.Dup);
			il.Emit(OpCodes.Brfalse_S, skipDisposeLabel);
			il.EmitCall(OpCodes.Callvirt, IDisposableDisposeMethod, Type.EmptyTypes);
			il.Emit(OpCodes.Br_S, disposedLabel);

			il.MarkLabel(skipDisposeLabel);
			il.Emit(OpCodes.Pop); // Pop the duplicated field value off the stack since we didn't use it.

			// this.target = null;
			il.MarkLabel(disposedLabel);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldnull);
			il.Emit(OpCodes.Stfld, targetField);

			// if (disposed is null) return;
			il.Emit(OpCodes.Ldloc, disposedLocal);
			il.Emit(OpCodes.Brfalse_S, retLabel);

			// ((EventHandler)disposed)(this, EventArgs.Empty);
			il.Emit(OpCodes.Ldloc, disposedLocal);
			il.Emit(OpCodes.Castclass, typeof(EventHandler));
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldsfld, EventArgsEmptyField);
			il.Emit(OpCodes.Callvirt, EventHandlerInvokeMethod);

			il.MarkLabel(retLabel);
			il.Emit(OpCodes.Ret);

			proxyTypeBuilder.DefineMethodOverride(disposeMethod, DisposeMethod);
		}

		private static void EmitIsDisposedProperty(TypeBuilder proxyTypeBuilder, FieldBuilder disposedField, FieldBuilder disposedSentinel)
		{
			PropertyBuilder isDisposedProperty = proxyTypeBuilder.DefineProperty(
				nameof(IDisposableObservable) + "." + nameof(IDisposableObservable.IsDisposed),
				PropertyAttributes.None,
				typeof(bool),
				Type.EmptyTypes);

			// get_IsDisposed method
			MethodBuilder isDisposedPropertyGetter = proxyTypeBuilder.DefineMethod(
				"get_" + nameof(IDisposableObservable.IsDisposed),
				MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual,
				typeof(bool),
				Type.EmptyTypes);
			ILGenerator il = isDisposedPropertyGetter.GetILGenerator();

			// return this.disposed == DisposedSentinel;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, disposedField);
			il.Emit(OpCodes.Ldsfld, disposedSentinel);
			il.Emit(OpCodes.Ceq);
			il.Emit(OpCodes.Ret);

			proxyTypeBuilder.DefineMethodOverride(isDisposedPropertyGetter, IDisposableObservableIsDisposedGetterMethod);
			isDisposedProperty.SetGetMethod(isDisposedPropertyGetter);
		}

		private static void EmitDisposedEvent(TypeBuilder proxyTypeBuilder, FieldBuilder disposedField, FieldBuilder disposedSentinelStaticField)
		{
			EventBuilder eventBuilder = proxyTypeBuilder.DefineEvent(
				nameof(INotifyDisposable) + "." + nameof(INotifyDisposable.Disposed),
				EventAttributes.None,
				typeof(EventHandler));

			// add_Disposed method
			MethodBuilder addMethod = proxyTypeBuilder.DefineMethod(
				"add_" + nameof(INotifyDisposable.Disposed),
				MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual,
				returnType: null,
				parameterTypes: EventHandlerTypeInArray);
			ILGenerator il = addMethod.GetILGenerator();
			BuildBody(addBody: true);

			// remove_Disposed method
			MethodBuilder removeMethod = proxyTypeBuilder.DefineMethod(
				"remove_" + nameof(INotifyDisposable.Disposed),
				MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual,
				returnType: null,
				parameterTypes: EventHandlerTypeInArray);
			il = removeMethod.GetILGenerator();
			BuildBody(addBody: false);

			proxyTypeBuilder.DefineMethodOverride(addMethod, INotifyDisposableAddHandler);
			proxyTypeBuilder.DefineMethodOverride(removeMethod, INotifyDisposableRemoveHandler);
			eventBuilder.SetAddOnMethod(addMethod);
			eventBuilder.SetRemoveOnMethod(removeMethod);

			void BuildBody(bool addBody)
			{
				// C# events in their simplest syntax get compiled into a thread-safe accessor that uses Interlocked.
				// We have to reproduce this handlers ourselves.
				// But to add to the thread-safety requirements, we must address the race with concurrent disposal.

				// object oldDisposed, newDisposed, currentDisposed;
				LocalBuilder oldDisposedLocal = il.DeclareLocal(typeof(object));
				LocalBuilder newDisposedLocal = il.DeclareLocal(typeof(object));
				LocalBuilder currentDisposedLocal = il.DeclareLocal(typeof(object));

				Label returnLabel = il.DefineLabel();

				// oldDisposed = this.disposed;
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, disposedField);
				il.Emit(OpCodes.Stloc, oldDisposedLocal);

				// while (true)
				Label whileLoop = il.DefineLabel();
				il.MarkLabel(whileLoop);
				{
					Label notDisposedYetLabel = il.DefineLabel();

					// if (oldDisposedHandlers == DisposedSentinel) {
					il.Emit(OpCodes.Ldloc, oldDisposedLocal);
					il.Emit(OpCodes.Ldsfld, disposedSentinelStaticField);
					il.Emit(OpCodes.Bne_Un_S, notDisposedYetLabel);
					{
						if (addBody)
						{
							// // The proxy has already been disposed of. Invoke the handler directly instead of storing it for later.
							// value(this, EventArgs.Empty);
							il.Emit(OpCodes.Ldarg_1);
							il.Emit(OpCodes.Ldarg_0);
							il.Emit(OpCodes.Ldsfld, EventArgsEmptyField);
							il.Emit(OpCodes.Callvirt, EventHandlerInvokeMethod);
						}

						// return;
						il.Emit(OpCodes.Ret);
					}

					il.MarkLabel(notDisposedYetLabel);

					// newDisposed = Delegate.Combine((EventHandler)oldDisposed, value);
					// - or -
					// newDisposed = Delegate.Remove((EventHandler)oldDisposed, value);
					il.Emit(OpCodes.Ldloc, oldDisposedLocal);
					il.Emit(OpCodes.Castclass, typeof(EventHandler));
					il.Emit(OpCodes.Ldarg_1);
					il.Emit(OpCodes.Call, addBody ? DelegateCombineMethod : DelegateRemoveMethod);
					il.Emit(OpCodes.Stloc, newDisposedLocal);

					// object currentDisposed = Interlocked.CompareExchange(ref this.disposed, newDisposed, oldDisposed);
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldflda, disposedField);
					il.Emit(OpCodes.Ldloc, newDisposedLocal);
					il.Emit(OpCodes.Ldloc, oldDisposedLocal);
					il.Emit(OpCodes.Call, InterlockedCompareExchangeMethod);
					il.Emit(OpCodes.Stloc, currentDisposedLocal);

					// if (currentDisposed == oldDisposed) return;
					il.Emit(OpCodes.Ldloc, currentDisposedLocal);
					il.Emit(OpCodes.Ldloc, oldDisposedLocal);
					il.Emit(OpCodes.Beq_S, returnLabel);

					// oldDisposed = currentDisposed;
					il.Emit(OpCodes.Ldloc, currentDisposedLocal);
					il.Emit(OpCodes.Stloc, oldDisposedLocal);

					il.Emit(OpCodes.Br_S, whileLoop);
				}

				il.MarkLabel(returnLabel);
				il.Emit(OpCodes.Ret);
			}
		}

		private static void EmitMethodThunk(TypeInfo serviceInterface, TypeBuilder proxyTypeBuilder, FieldBuilder targetField, FieldBuilder exceptionStrategyField, MethodInfo method)
		{
			Requires.Argument(serviceInterface.FullName is object, nameof(serviceInterface), "FullName is null.");

			ParameterInfo[] methodParameters = method.GetParameters();
			MethodBuilder methodBuilder = proxyTypeBuilder.DefineMethod(
				method.Name,
				MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
				method.ReturnType,
				methodParameters.Select(p => p.ParameterType).ToArray());
			ILGenerator il = methodBuilder.GetILGenerator();

			// All locals at top are in a specific order so we can use the smallest IL instructions.
			LocalBuilder targetLocal = il.DeclareLocal(serviceInterface);
			LocalBuilder resultLocal;
			if (!method.ReturnType.Equals(typeof(void)))
			{
				resultLocal = il.DeclareLocal(method.ReturnType);
			}

			// If a CancellationToken appears as the last parameter, consider it immediately and throw instead of anything else.
			// This simulates what would happen if a token were precanceled going into StreamJsonRpc.
			int cancellationTokenParameterIndex = methodParameters.Length > 0 && methodParameters[methodParameters.Length - 1].ParameterType == typeof(CancellationToken) ? methodParameters.Length : -1;
			if (cancellationTokenParameterIndex >= 0)
			{
				// cancellationToken.ThrowIfCancellationRequested();
				il.Emit(cancellationTokenParameterIndex <= 255 ? OpCodes.Ldarga_S : OpCodes.Ldarga, cancellationTokenParameterIndex);
				il.Emit(OpCodes.Call, ThrowIfCancellationRequestedMethod);
			}

			// var target = (interface)this.target
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, targetField);
			if (serviceInterface != targetField.FieldType)
			{
				il.Emit(OpCodes.Castclass, serviceInterface);
			}

			il.Emit(OpCodes.Stloc_0);

			// if (target == null) throw new ObjectDisposedException();
			Label notDisposedLabel = il.DefineLabel();
			il.Emit(OpCodes.Ldloc_0);
			il.Emit(OpCodes.Brtrue, notDisposedLabel);
			il.Emit(OpCodes.Ldstr, serviceInterface.FullName);
			il.Emit(OpCodes.Newobj, ObjectDisposedExceptionCtor);
			il.Emit(OpCodes.Throw);
			il.MarkLabel(notDisposedLabel);

			Label retLabel = il.DefineLabel();

			// try {
			il.BeginExceptionBlock();

			// result = target.SomeMethod(args);
			il.Emit(OpCodes.Ldloc_0);
			LoadAllArguments(il, methodParameters);
			il.Emit(OpCodes.Callvirt, method);
			if (!method.ReturnType.Equals(typeof(void)))
			{
				il.Emit(OpCodes.Stloc_1);
			}

			il.Emit(OpCodes.Leave_S, retLabel);

			// } catch (Exception ex) {
			il.BeginCatchBlock(typeof(Exception));

			// throw ExceptionHelper(ex, this.exceptionStrategy);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, exceptionStrategyField);
			il.Emit(OpCodes.Call, ExceptionHelperMethod);
			il.Emit(OpCodes.Throw);

			// }
			il.EndExceptionBlock();

			il.MarkLabel(retLabel);

			// return result; or some variant
			if (!method.ReturnType.Equals(typeof(void)))
			{
				il.Emit(OpCodes.Ldloc_1);

				if (method.ReturnType.Equals(typeof(Task)))
				{
					// return ReturnedTaskHelper(result, this.exceptionStrategy);
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldfld, exceptionStrategyField);
					il.Emit(OpCodes.Call, ReturnedTaskHelperMethod);
				}
				else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition().Equals(typeof(Task<>)))
				{
					// return ReturnedTaskOfTHelper(result, this.exceptionStrategy);
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldfld, exceptionStrategyField);
					il.Emit(OpCodes.Call, ReturnedTaskOfTHelperMethod.MakeGenericMethod(method.ReturnType.GetGenericArguments()));
				}
				else if (method.ReturnType.Equals(typeof(ValueTask)))
				{
					// return ReturnedValueTaskHelper(result, this.exceptionStrategy);
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldfld, exceptionStrategyField);
					il.Emit(OpCodes.Call, ReturnedValueTaskHelperMethod);
				}
				else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition().Equals(typeof(ValueTask<>)))
				{
					// return ReturnedValueTaskOfTHelper(result, this.exceptionStrategy);
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldfld, exceptionStrategyField);
					il.Emit(OpCodes.Call, ReturnedValueTaskOfTHelperMethod.MakeGenericMethod(method.ReturnType.GetGenericArguments()));
				}
			}

			il.Emit(OpCodes.Ret);

			proxyTypeBuilder.DefineMethodOverride(methodBuilder, method);
		}

		private static void EmitEventThunk(TypeBuilder proxyTypeBuilder, FieldBuilder targetField, EventInfo evt)
		{
			Requires.Argument(evt.EventHandlerType is object, nameof(evt), "EventHandlerType is null");
			Requires.Argument(evt.AddMethod is object, nameof(evt), "AddMethod is null");
			Requires.Argument(evt.RemoveMethod is object, nameof(evt), "RemoveMethod is null");

			// public event EventHandler EventName;
			EventBuilder evtBuilder = proxyTypeBuilder.DefineEvent(evt.Name, evt.Attributes, evt.EventHandlerType);

			// add_EventName => this.target?.EventName = value;
			var addRemoveHandlerParams = new Type[] { evt.EventHandlerType };
			const MethodAttributes methodAttributes = MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual;
			MethodBuilder addMethod = proxyTypeBuilder.DefineMethod($"add_{evt.Name}", methodAttributes, null, addRemoveHandlerParams);
			{
				ILGenerator il = addMethod.GetILGenerator();
				Label skipLabel = il.DefineLabel();
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, targetField);
				if (evt.DeclaringType != targetField.FieldType)
				{
					il.Emit(OpCodes.Castclass, evt.DeclaringType!);
				}

				il.Emit(OpCodes.Dup);
				il.Emit(OpCodes.Brfalse, skipLabel);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Callvirt, evt.AddMethod);
				il.Emit(OpCodes.Ret);
				il.MarkLabel(skipLabel);
				il.Emit(OpCodes.Pop);
				il.Emit(OpCodes.Ret);
				evtBuilder.SetAddOnMethod(addMethod);
			}

			// remove_EventName
			MethodBuilder removeMethod = proxyTypeBuilder.DefineMethod($"remove_{evt.Name}", methodAttributes, null, addRemoveHandlerParams);
			{
				ILGenerator il = removeMethod.GetILGenerator();
				Label skipLabel = il.DefineLabel();
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, targetField);
				if (evt.DeclaringType != targetField.FieldType)
				{
					il.Emit(OpCodes.Castclass, evt.DeclaringType!);
				}

				il.Emit(OpCodes.Dup);
				il.Emit(OpCodes.Brfalse, skipLabel);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Callvirt, evt.RemoveMethod);
				il.Emit(OpCodes.Ret);
				il.MarkLabel(skipLabel);
				il.Emit(OpCodes.Pop);
				il.Emit(OpCodes.Ret);
				evtBuilder.SetRemoveOnMethod(removeMethod);
			}
		}

		/// <summary>
		/// Called from the generated proxy to help prepare the exception to throw.
		/// </summary>
		/// <param name="ex">The exception thrown from the target object.</param>
		/// <param name="exceptionStrategy">The value of <see cref="JsonRpc.ExceptionStrategy"/> to emulate.</param>
		/// <returns>The exception the generated code should throw.</returns>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Exception ExceptionHelper(Exception ex, ExceptionProcessing exceptionStrategy)
		{
			var localRpcEx = ex as LocalRpcException;
			object errorData = localRpcEx?.ErrorData ?? new CommonErrorData(ex);
			if (localRpcEx is object)
			{
				throw new RemoteInvocationException(
					ex.Message,
					localRpcEx?.ErrorCode ?? (int)JsonRpcErrorCode.InvocationError,
					errorData,
					errorData);
			}

			return exceptionStrategy switch
			{
				ExceptionProcessing.CommonErrorData => new RemoteInvocationException(
					ex.Message,
					localRpcEx?.ErrorCode ?? (int)JsonRpcErrorCode.InvocationError,
					errorData,
					errorData),
				ExceptionProcessing.ISerializable => new RemoteInvocationException(ex.Message, (int)JsonRpcErrorCode.InvocationErrorWithException, ex),
				_ => throw new NotSupportedException("Unsupported exception strategy: " + exceptionStrategy),
			};
		}

		private static Task? ReturnedTaskHelperAsync(Task task, ExceptionProcessing exceptionStrategy)
		{
#pragma warning disable VSTHRD110 // Observe result of async calls -- https://github.com/microsoft/vs-threading/issues/899
			return task?.ContinueWith(
#pragma warning restore VSTHRD110 // Observe result of async calls
				_ =>
				{
					if (_.IsFaulted)
					{
						throw ExceptionHelper(_.Exception!.InnerException ?? _.Exception, exceptionStrategy);
					}

					if (_.IsCanceled)
					{
						// Rethrow the same cancellation exception so the CancellationToken is set.
						_.GetAwaiter().GetResult();
					}
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		private static Task<T>? ReturnedTaskOfTHelperAsync<T>(Task<T> task, ExceptionProcessing exceptionStrategy)
		{
#pragma warning disable VSTHRD110 // Observe result of async calls -- https://github.com/microsoft/vs-threading/issues/899
			return task?.ContinueWith(
#pragma warning restore VSTHRD110 // Observe result of async calls -- https://github.com/microsoft/vs-threading/issues/899
				_ =>
				{
					if (_.IsFaulted)
					{
						throw ExceptionHelper(_.Exception!.InnerException ?? _.Exception, exceptionStrategy);
					}

					if (_.IsCanceled)
					{
						// Rethrow the same cancellation exception so the CancellationToken is set.
						_.GetAwaiter().GetResult();
					}

					return _.Result;
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		private static async ValueTask ReturnedValueTaskHelperAsync(ValueTask task, ExceptionProcessing exceptionStrategy)
		{
			try
			{
				await task.ConfigureAwait(false);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				throw ExceptionHelper(ex, exceptionStrategy);
			}
		}

		private static async ValueTask<T> ReturnedValueTaskOfTHelperAsync<T>(ValueTask<T> task, ExceptionProcessing exceptionStrategy)
		{
			try
			{
				return await task.ConfigureAwait(false);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				throw ExceptionHelper(ex, exceptionStrategy);
			}
		}

		private static void LoadAllArguments(ILGenerator il, ParameterInfo[] methodParameters)
		{
			for (int argIndex = 1; argIndex <= methodParameters.Length; argIndex++)
			{
				switch (argIndex)
				{
					case 0:
						il.Emit(OpCodes.Ldarg_0);
						break;
					case 1:
						il.Emit(OpCodes.Ldarg_1);
						break;
					case 2:
						il.Emit(OpCodes.Ldarg_2);
						break;
					case 3:
						il.Emit(OpCodes.Ldarg_3);
						break;
					case int idx when idx <= 255:
						il.Emit(OpCodes.Ldarg_S, idx);
						break;
					default:
						il.Emit(OpCodes.Ldarg, argIndex);
						break;
				}
			}
		}

		private static IEnumerable<T> FindAllOnThisAndOtherInterfaces<T>(TypeInfo interfaceType, Func<TypeInfo, IEnumerable<T>> oneInterfaceQuery)
		{
			Requires.NotNull(interfaceType, nameof(interfaceType));
			Requires.NotNull(oneInterfaceQuery, nameof(oneInterfaceQuery));

			IEnumerable<T> result = oneInterfaceQuery(interfaceType);
			return result.Concat(interfaceType.ImplementedInterfaces.SelectMany(i => oneInterfaceQuery(i.GetTypeInfo())));
		}

		private static T CreateProxyHelper<T>(T target, ExceptionProcessing exceptionStrategy)
			where T : class
		{
			return CreateProxy<T>(target, default, exceptionStrategy);
		}
	}

	private class TypeArrayUnorderedEqualityComparer : IEqualityComparer<ReadOnlyMemory<Type>>
	{
		internal static readonly TypeArrayUnorderedEqualityComparer Instance = new();

		private TypeArrayUnorderedEqualityComparer()
		{
		}

		public bool Equals(ReadOnlyMemory<Type> x, ReadOnlyMemory<Type> y)
		{
			if (x.Length != y.Length)
			{
				return false;
			}

			bool mismatchFound = false;
			for (int i = 0; i < x.Span.Length; i++)
			{
				if (!x.Span[i].IsEquivalentTo(y.Span[i]))
				{
					mismatchFound = true;
					break;
				}
			}

			if (!mismatchFound)
			{
				return true;
			}

			// Try falling back to a disordered comparison.
			// We use an n^2 search instead of allocating because the length of these arrays tends to be *very* small.
			for (int i = 0; i < x.Span.Length; i++)
			{
				bool matchFound = false;
				for (int j = 0; j < y.Span.Length; j++)
				{
					if (x.Span[i].IsEquivalentTo(y.Span[j]))
					{
						matchFound = true;
						break;
					}
				}

				if (!matchFound)
				{
					return false;
				}
			}

			return true;
		}

		public int GetHashCode([DisallowNull] ReadOnlyMemory<Type> obj)
		{
			int hashCode = obj.Length;
			for (int i = 0; i < obj.Span.Length; i++)
			{
				hashCode = (hashCode * 31) + obj.Span[i].GetHashCode();
			}

			return hashCode;
		}
	}

	private class ByContentEqualityComparer : IEqualityComparer<ImmutableHashSet<AssemblyName>>
	{
		public bool Equals(ImmutableHashSet<AssemblyName>? x, ImmutableHashSet<AssemblyName>? y)
		{
			if (x is null && y is null)
			{
				return true;
			}

			if (x is null || y is null)
			{
				return false;
			}

			if (x.Count != y.Count)
			{
				return false;
			}

			return !x.Except(y).Any();
		}

		public int GetHashCode(ImmutableHashSet<AssemblyName> obj)
		{
			int hashCode = 0;
			foreach (AssemblyName item in obj)
			{
				hashCode += item.GetHashCode();
			}

			return hashCode;
		}
	}
}
