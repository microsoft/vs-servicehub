// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/*
 * This class was copied from https://github.com/AArnott/vs-mef/blob/master/src/Microsoft.VisualStudio.Composition/Reflection/SkipClrVisibilityChecks.cs
 * and should ideally be kept in sync.
 * Last updated from vs-mef commit: 8543110b32841ffca3cff659cbe2f2adabd93063
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Gives a dynamic assembly the ability to skip CLR visibility checks,
/// allowing the assembly to access private members of another assembly.
/// </summary>
[RequiresDynamicCode(Reasons.DynamicProxy)]
[RequiresUnreferencedCode(Reasons.DynamicProxy)]
internal class SkipClrVisibilityChecks
{
	/// <summary>
	/// The <see cref="Attribute.Attribute()"/> constructor.
	/// </summary>
	private static readonly ConstructorInfo AttributeBaseClassCtor = typeof(Attribute).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single(ctor => ctor.GetParameters().Length == 0);

	/// <summary>
	/// The <see cref="AttributeUsageAttribute(AttributeTargets)"/> constructor.
	/// </summary>
	private static readonly ConstructorInfo AttributeUsageCtor = typeof(AttributeUsageAttribute).GetConstructor(new Type[] { typeof(AttributeTargets) })!;

	/// <summary>
	/// The <see cref="AttributeUsageAttribute.AllowMultiple"/> property.
	/// </summary>
	private static readonly PropertyInfo AttributeUsageAllowMultipleProperty = typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.AllowMultiple))!;

	/// <summary>
	/// The assembly builder that is constructing the dynamic assembly.
	/// </summary>
	private readonly AssemblyBuilder assemblyBuilder;

	/// <summary>
	/// The module builder for the default module of the <see cref="assemblyBuilder"/>.
	/// This is where the special attribute will be defined.
	/// </summary>
	private readonly ModuleBuilder moduleBuilder;

	/// <summary>
	/// The set of assemblies that already have visibility checks skipped for.
	/// </summary>
	private readonly HashSet<string> attributedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The constructor on the special attribute to reference for each skipped assembly.
	/// </summary>
	private ConstructorInfo? magicAttributeCtor;

	/// <summary>
	/// Initializes a new instance of the <see cref="SkipClrVisibilityChecks"/> class.
	/// </summary>
	/// <param name="assemblyBuilder">The builder for the dynamic assembly.</param>
	/// <param name="moduleBuilder">The builder for the default module defined by <see cref="assemblyBuilder"/>.</param>
	internal SkipClrVisibilityChecks(AssemblyBuilder assemblyBuilder, ModuleBuilder moduleBuilder)
	{
		Requires.NotNull(assemblyBuilder, nameof(assemblyBuilder));
		Requires.NotNull(moduleBuilder, nameof(moduleBuilder));
		this.assemblyBuilder = assemblyBuilder;
		this.moduleBuilder = moduleBuilder;
	}

	/// <summary>
	/// Gets the set of assemblies that a generated assembly must be granted the ability to skip visiblity checks for
	/// in order to access the specified type.
	/// </summary>
	/// <param name="typeInfo">The type which may be internal.</param>
	/// <returns>The set of names of assemblies to skip visibility checks for.</returns>
	internal static ImmutableHashSet<AssemblyName> GetSkipVisibilityChecksRequirements(TypeInfo typeInfo)
	{
		Requires.NotNull(typeInfo, nameof(typeInfo));

		// Allow for a service interface to have an attribute with a specially recognized name (that may be defined internally to themselves, since we don't define it)
		// that will cause us to skip attribute checks on public interfaces used as generic type arguments.
		// This can avoid assembly loads for other attributes that may be defined on those interfaces.
		// But this attribute should only be used on service interfaces that are sure to not reference embeddable types or else runtime failures may result.
		// NOTE: if we ever document this or declare a public attribute for this, we should add an analyzer that reports an error when the assertion it's making isn't true.
		bool skipEmbeddableTypesCheck = typeInfo.GetCustomAttributesData().Any(ad => ad.AttributeType.Name == "SkipEmbeddableTypesCheckAttribute");

		var visitedTypes = new HashSet<TypeInfo>();
		ImmutableHashSet<AssemblyName>.Builder assembliesDeclaringInternalTypes = ImmutableHashSet.CreateBuilder<AssemblyName>(AssemblyNameEqualityComparer.Instance);
		CheckForNonPublicTypes(typeInfo, assembliesDeclaringInternalTypes, visitedTypes, skipEmbeddableTypesCheck);

		// Enumerate members on the interface that we're going to need to implement.
		foreach (MethodInfo methodInfo in typeInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
		{
			CheckForNonPublicTypes(methodInfo.ReturnType.GetTypeInfo(), assembliesDeclaringInternalTypes, visitedTypes, skipEmbeddableTypesCheck);
			foreach (ParameterInfo parameter in methodInfo.GetParameters())
			{
				CheckForNonPublicTypes(parameter.ParameterType.GetTypeInfo(), assembliesDeclaringInternalTypes, visitedTypes, skipEmbeddableTypesCheck);
			}
		}

		return assembliesDeclaringInternalTypes.ToImmutable();
	}

	/// <summary>
	/// Add attributes to a dynamic assembly so that the CLR will skip visibility checks
	/// for the assemblies with the specified names.
	/// </summary>
	/// <param name="assemblyNames">The names of the assemblies to skip visibility checks for.</param>
	internal void SkipVisibilityChecksFor(IEnumerable<AssemblyName> assemblyNames)
	{
		Requires.NotNull(assemblyNames, nameof(assemblyNames));

		foreach (AssemblyName assemblyName in assemblyNames)
		{
			this.SkipVisibilityChecksFor(assemblyName);
		}
	}

	/// <summary>
	/// Add an attribute to a dynamic assembly so that the CLR will skip visibility checks
	/// for the assembly with the specified name.
	/// </summary>
	/// <param name="assemblyName">The name of the assembly to skip visibility checks for.</param>
	internal void SkipVisibilityChecksFor(AssemblyName assemblyName)
	{
		Requires.NotNull(assemblyName, nameof(assemblyName));

		string? assemblyNameArg = assemblyName.Name;
		Requires.Argument(assemblyNameArg is object, nameof(assemblyName), "Name is null");
		if (this.attributedAssemblyNames.Add(assemblyNameArg))
		{
			var cab = new CustomAttributeBuilder(this.GetMagicAttributeCtor(), new object[] { assemblyNameArg });
			this.assemblyBuilder.SetCustomAttribute(cab);
		}
	}

	private static void CheckForNonPublicTypes(TypeInfo typeInfo, ImmutableHashSet<AssemblyName>.Builder assembliesDeclaringInternalTypes, HashSet<TypeInfo> visitedTypes, bool skipEmbeddableTypesCheck)
	{
		Requires.NotNull(typeInfo, nameof(typeInfo));
		Requires.NotNull(assembliesDeclaringInternalTypes, nameof(assembliesDeclaringInternalTypes));
		Requires.NotNull(visitedTypes, nameof(visitedTypes));

		if (!visitedTypes.Add(typeInfo))
		{
			// This type has already been visited.
			// Break out early to avoid a stack overflow in the case of recursive generic types.
			return;
		}

		if (typeInfo.IsArray)
		{
			CheckForNonPublicTypes(typeInfo.GetElementType()!.GetTypeInfo(), assembliesDeclaringInternalTypes, visitedTypes, skipEmbeddableTypesCheck);
		}
		else
		{
			if (typeInfo.IsNotPublic || !(typeInfo.IsPublic || typeInfo.IsNestedPublic))
			{
				assembliesDeclaringInternalTypes.Add(typeInfo.Assembly.GetName());
			}
			else if (typeInfo.DeclaringType is not null)
			{
				// A "public" interface may be nested inside an internal one, making *this* interface effectively internal too.
				CheckForNonPublicTypes(typeInfo.DeclaringType.GetTypeInfo(), assembliesDeclaringInternalTypes, visitedTypes, skipEmbeddableTypesCheck);
			}

			if (typeInfo.IsGenericType && !typeInfo.IsGenericTypeDefinition)
			{
				// We have to treat embedded types that appear as generic type arguments as non-public,
				// because the CLR cannot assign Outer<TEmbedded> to Outer<TEmbedded> across assembly boundaries.
				foreach (Type typeArg in typeInfo.GenericTypeArguments)
				{
					if (!skipEmbeddableTypesCheck && IsEmbeddedType(typeArg))
					{
						assembliesDeclaringInternalTypes.Add(typeInfo.Assembly.GetName());
					}

					CheckForNonPublicTypes(typeArg.GetTypeInfo(), assembliesDeclaringInternalTypes, visitedTypes, skipEmbeddableTypesCheck);
				}
			}
		}
	}

	private static bool IsEmbeddedType(Type type)
	{
		Requires.NotNull(type, nameof(type));
		TypeInfo typeInfo = type.GetTypeInfo();

		// Embedded types are always public.
		// Test everything we can *before* looking for attributes to avoid loading assemblies that define irrelevant attributes.
		if ((typeInfo.IsNestedPublic || typeInfo.IsPublic) && typeInfo.IsInterface)
		{
			// TypeIdentifierAttribute signifies an embeddED type.
			// ComImportAttribute suggests an embeddABLE type.
			if (typeInfo.IsDefined(typeof(TypeIdentifierAttribute)) && typeInfo.IsDefined(typeof(GuidAttribute)))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Gets the constructor to the IgnoresAccessChecksToAttribute, generating the attribute if necessary.
	/// </summary>
	/// <returns>The constructor to the IgnoresAccessChecksToAttribute.</returns>
	private ConstructorInfo GetMagicAttributeCtor()
	{
		if (this.magicAttributeCtor == null)
		{
			TypeInfo magicAttribute = this.EmitMagicAttribute();
			this.magicAttributeCtor = magicAttribute.GetConstructor(new Type[] { typeof(string) });
			Assumes.NotNull(this.magicAttributeCtor);
		}

		return this.magicAttributeCtor;
	}

	/// <summary>
	/// Defines the special IgnoresAccessChecksToAttribute type in the <see cref="moduleBuilder"/>.
	/// </summary>
	/// <returns>The generated attribute type.</returns>
	private TypeInfo EmitMagicAttribute()
	{
		TypeBuilder tb = this.moduleBuilder.DefineType(
			"System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute",
			TypeAttributes.NotPublic,
			typeof(Attribute));

		var attributeUsage = new CustomAttributeBuilder(
			AttributeUsageCtor,
			new object[] { AttributeTargets.Assembly },
			new PropertyInfo[] { AttributeUsageAllowMultipleProperty },
			new object[] { false });
		tb.SetCustomAttribute(attributeUsage);

		ConstructorBuilder cb = tb.DefineConstructor(
			MethodAttributes.Public |
			MethodAttributes.HideBySig |
			MethodAttributes.SpecialName |
			MethodAttributes.RTSpecialName,
			CallingConventions.Standard,
			new Type[] { typeof(string) });
		cb.DefineParameter(1, ParameterAttributes.None, "assemblyName");

		ILGenerator il = cb.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Call, AttributeBaseClassCtor);
		il.Emit(OpCodes.Ret);

		return tb.CreateTypeInfo()!;
	}

	private class AssemblyNameEqualityComparer : IEqualityComparer<AssemblyName>
	{
		internal static readonly IEqualityComparer<AssemblyName> Instance = new AssemblyNameEqualityComparer();

		private AssemblyNameEqualityComparer()
		{
		}

		public bool Equals(AssemblyName? x, AssemblyName? y)
		{
			if (x == null && y == null)
			{
				return true;
			}

			if (x == null || y == null)
			{
				return false;
			}

			return string.Equals(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase);
		}

		public int GetHashCode(AssemblyName obj)
		{
			Requires.NotNull(obj, nameof(obj));

			return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FullName);
		}
	}
}
