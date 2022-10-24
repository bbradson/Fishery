// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using MonoMod.Utils;
using Verse;
using static Mono.Cecil.Mixin;

namespace FisheryLib;
public static class Reflection
{
	public static CodeInstructions GetCodeInstructions(Delegate method)
		=> PatchProcessor.GetCurrentInstructions(method.Method);

	public static CodeInstructions GetCodeInstructions(Expression<Action> method)
		=> PatchProcessor.GetCurrentInstructions(SymbolExtensions.GetMethodInfo(method));

	public static CodeInstructions MakeReplacementCall(Delegate method)
		=> MakeReplacementCall(method.Method);

	public static CodeInstructions MakeReplacementCall(MethodInfo method)
	{
		if (!method.IsStatic)
			yield return FishTranspiler.This;

		foreach (var argument in method.GetParameters())
			yield return FishTranspiler.Argument(argument);

		yield return FishTranspiler.Call(method);
		yield return FishTranspiler.Return;
	}

	public static Type Type(string assembly, string type) => System.Type.GetType($"{type}, {assembly}");

	public static MethodInfo MethodInfo<T>(T method) where T : Delegate => method.Method;

	public static MethodInfo MethodInfo(string assembly, string type, string name, Type[]? parameters = null, Type[]? generics = null)
		=> AccessTools.Method(Type(assembly, type), name, parameters, generics);

	public static IEnumerable<MethodInfo> MethodsOfName(Type type, string name)
	{
		var methods = MethodsOfNameSilentFail(type, name).ToArray();
		if (!methods.Any())
			ThrowForMethodsOfName(type, name);
		return methods;
	}

	private static void ThrowForMethodsOfName(Type type, string name) => ThrowHelper.ThrowArgumentException($"No methods found for {type.FullDescription()}:{name}");

	public static IEnumerable<MethodInfo> MethodsOfNameSilentFail(Type type, string name)
		=> type?.GetMethods(AccessTools.allDeclared)
		.Where(m => m.Name == name)
		?? Enumerable.Empty<MethodInfo>();

	public static FieldInfo FieldInfo(string assembly, string type, string name) => AccessTools.Field(Type(assembly, type), name);

	public static bool IsAssignableTo(this Type type, [NotNullWhen(true)] Type? targetType, params Type[] generics)
	{
		Guard.IsNotNull(type);

		return targetType != null
			&& (generics is { Length: >= 1 }
				? targetType.MakeGenericType(generics)
				: targetType)
			.IsAssignableFrom(type);
	}

	public static bool AreAssignableFrom(this IEnumerable<Type> types, IEnumerable<Type> targetTypes)
		=> targetTypes.AreAssignableTo(types);

	public static bool AreAssignableTo(this IEnumerable<Type> types, IEnumerable<Type> targetTypes)
	{
		Guard.IsNotNull(types);
		Guard.IsNotNull(targetTypes);

		using var typesEnumerator = types.GetEnumerator();
		using var targetTypesEnumerator = targetTypes.GetEnumerator();

		while (typesEnumerator.MoveNext())
		{
			if (!targetTypesEnumerator.MoveNext()
				|| !targetTypesEnumerator.Current.IsAssignableFrom(typesEnumerator.Current))
			{
				return false;
			}
		}

		return !targetTypesEnumerator.MoveNext();
	}

	public static bool IsNullable(this Type type)
	{
		Guard.IsNotNull(type);

		return type.IsGenericType
			&& type.GetGenericTypeDefinition() == typeof(Nullable<>);
	}

	public static bool IsNullable(this Type nullableType, [NotNullWhen(true)] out Type? valueType)
	{
		if (nullableType.IsNullable())
		{
			valueType = nullableType.GetGenericArguments()[0];
			return true;
		}
		else
		{
			valueType = null;
			return false;
		}
	}

	public static bool HasGenericInterfaceDefinition(this Type type, Type interfaceType)
	{
		Guard.IsNotNull(type);

		return Array.Exists(type.GetInterfaces(), @interface
				=> @interface.IsGenericType
				&& @interface.GetGenericTypeDefinition() == interfaceType);
	}

	public static unsafe IntPtr GetFunctionPointer(this MethodBase method) => method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T> GetFunctionPointer<T>(this MethodBase method) => (delegate*<T>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T_in, T_out> GetFunctionPointer<T_in, T_out>(this MethodBase method) => (delegate*<T_in, T_out>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T1, T2, T_out> GetFunctionPointer<T1, T2, T_out>(this MethodBase method) => (delegate*<T1, T2, T_out>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T1, T2, T3, T_out> GetFunctionPointer<T1, T2, T3, T_out>(this MethodBase method) => (delegate*<T1, T2, T3, T_out>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T1, T2, T3, T4, T_out> GetFunctionPointer<T1, T2, T3, T4, T_out>(this MethodBase method) => (delegate*<T1, T2, T3, T4, T_out>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T1, T2, T3, T4, T5, T_out> GetFunctionPointer<T1, T2, T3, T4, T5, T_out>(this MethodBase method) => (delegate*<T1, T2, T3, T4, T5, T_out>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T1, T2, T3, T4, T5, T6, T_out> GetFunctionPointer<T1, T2, T3, T4, T5, T6, T_out>(this MethodBase method) => (delegate*<T1, T2, T3, T4, T5, T6, T_out>)method.MethodHandle.GetFunctionPointer();
	public static unsafe delegate*<T1, T2, T3, T4, T5, T6, T7, T_out> GetFunctionPointer<T1, T2, T3, T4, T5, T6, T7, T_out>(this MethodBase method) => (delegate*<T1, T2, T3, T4, T5, T6, T7, T_out>)method.MethodHandle.GetFunctionPointer();

	/// <summary>
	/// Finds a constructor with parameters that are either assignable to or assignable from the types in the provided parameters array
	/// </summary>
	/// <param name="type">The type of the object to construct</param>
	/// <param name="parameters">An array of parameter types that are either assignable to or from the parameters of a constructor for the target type</param>
	/// <param name="searchForStatic">Search for static constructors instead of instance constructors</param>
	/// <param name="throwOnFailure">Throw an exception on failure to find a matching constructor</param>
	/// <returns>A ConstructorInfo that best matches the provided arguments. Null on failure if throwOnFailure is set to false</returns>
	public static ConstructorInfo? MatchingConstructor(Type type, Type[]? parameters = null, bool searchForStatic = false, bool throwOnFailure = true)
	{
		Guard.IsNotNull(type);

		parameters ??= Array.Empty<Type>();
		var flags = searchForStatic ? (AccessTools.all & ~BindingFlags.Instance) : (AccessTools.all & ~BindingFlags.Static);

		return TryGetConstructor(type, flags, paramTypes => paramTypes.AreAssignableFrom(parameters))
			?? TryGetConstructor(type, flags, paramTypes => paramTypes.AreAssignableTo(parameters))
			?? (throwOnFailure ? ThrowHelper.ThrowInvalidOperationException<ConstructorInfo>(
				  $"No constructor found for type: {type}, parameters: {parameters.ToStringSafeEnumerable()}, static: {searchForStatic}, found constructors: {type.GetConstructors(flags).ToStringSafeEnumerable()}")
			: null);
	}

	private static ConstructorInfo TryGetConstructor(Type type, BindingFlags flags, Predicate<IEnumerable<Type>> predicate)
		=> type.GetConstructors(flags).FirstOrDefault(c
				=> predicate(c.GetParameters().Select(p => p.ParameterType)));

	private static Delegate MakeStructDefaultConstructor(Type type)
	{
		var dm = new DynamicMethod("StructDefaultConstructor", type, Array.Empty<Type>(), type, true);
		var il = dm.GetILGenerator();

		var variable = il.DeclareLocal(type);

		il.Emit(FishTranspiler.LocalVariable(variable).Address());
		il.Emit(FishTranspiler.New(type));
		il.Emit(FishTranspiler.LocalVariable(variable));
		il.Emit(FishTranspiler.Return);

		return dm.CreateDelegate(typeof(Func<>).MakeGenericType(type));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T New<T>()
		=> Create<T>.@new();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T_argument>(T_argument argument)
		=> Create<T_result, T_argument>.@new(argument);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T1, T2>(T1 arg1, T2 arg2)
		=> Create<T_result, T1, T2>.@new(arg1, arg2);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3)
		=> Create<T_result, T1, T2, T3>.@new(arg1, arg2, arg3);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
		=> Create<T_result, T1, T2, T3, T4>.@new(arg1, arg2, arg3, arg4);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
		=> Create<T_result, T1, T2, T3, T4, T5>.@new(arg1, arg2, arg3, arg4, arg5);

	private static class Create<T>
	{
		internal static Func<T> @new
			= typeof(T).IsValueType ? (Func<T>)MakeStructDefaultConstructor(typeof(T))
			: MatchingConstructor(typeof(T), Array.Empty<Type>())!.CreateDelegate<Func<T>>();
	}

	private static class Create<T_result, T_argument>
	{
		internal static Func<T_argument, T_result> @new
			= MatchingConstructor(typeof(T_result), new[] { typeof(T_argument) })!.CreateDelegate<Func<T_argument, T_result>>();
	}

	private static class Create<T_result, T1, T2>
	{
		internal static Func<T1, T2, T_result> @new
			= MatchingConstructor(typeof(T_result), new[] { typeof(T1), typeof(T2) })!.CreateDelegate<Func<T1, T2, T_result>>();
	}

	private static class Create<T_result, T1, T2, T3>
	{
		internal static Func<T1, T2, T3, T_result> @new
			= MatchingConstructor(typeof(T_result), new[] { typeof(T1), typeof(T2), typeof(T3) })!.CreateDelegate<Func<T1, T2, T3, T_result>>();
	}

	private static class Create<T_result, T1, T2, T3, T4>
	{
		internal static Func<T1, T2, T3, T4, T_result> @new
			= MatchingConstructor(typeof(T_result), new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) })!.CreateDelegate<Func<T1, T2, T3, T4, T_result>>();
	}

	private static class Create<T_result, T1, T2, T3, T4, T5>
	{
		internal static Func<T1, T2, T3, T4, T5, T_result> @new
			= MatchingConstructor(typeof(T_result), new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5) })!.CreateDelegate<Func<T1, T2, T3, T4, T5, T_result>>();
	}
}