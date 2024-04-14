// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using FisheryLib.Pools;
using Verse;

namespace FisheryLib;

[PublicAPI]
public static class Reflection
{
	private static Assembly[] _allAssemblies = Array.Empty<Assembly>();
	private static object _allAssembliesLock = new();

	public static CodeInstructions GetCodeInstructions(Delegate method, ILGenerator? generator = null)
		=> GetCodeInstructions(method.Method, generator);

	public static CodeInstructions GetCodeInstructions(Expression<Action> method, ILGenerator? generator = null)
		=> GetCodeInstructions(SymbolExtensions.GetMethodInfo(method), generator);
	
	public static CodeInstructions GetCodeInstructions(MethodInfo method, ILGenerator? generator = null)
		=> PatchProcessor.GetCurrentInstructions(method, generator: generator);

	public static CodeInstructions MakeReplacementCall(Delegate method) => MakeReplacementCall(method.Method);

	public static CodeInstructions MakeReplacementCall(MethodInfo method)
	{
		if (!method.IsStatic)
			yield return FishTranspiler.This;

		foreach (var argument in method.GetParameters())
			yield return FishTranspiler.Argument(argument);

		yield return FishTranspiler.Call(method);
		yield return FishTranspiler.Return;
	}

	public static Assembly[] AllAssemblies
	{
		get
		{
			lock (_allAssembliesLock)
			{
				return _allAssemblies.Length == AppDomain.CurrentDomain.GetAssemblies().Length
					? _allAssemblies
					: _allAssemblies = AccessTools.AllAssemblies().ToArray();
			}
		}
	}

	public static Type? Type(string assembly, string typeFullName)
		=> System.Type.GetType(string.Concat(typeFullName, ", ", assembly));

	public static Type? Type(string assembly, string @namespace, string type)
		=> System.Type.GetType(StringHelper.Resolve($"{@namespace}.{type}, {assembly}"));

	public static Type? Type(string fullName)
	{
		var assemblies = AllAssemblies;

		for (var i = assemblies.Length; i-- > 0;)
		{
			if (assemblies[i].GetType(fullName) is { } type)
				return type;
		}

		return null;
	}

	public static MethodInfo MethodInfo<T>(T method) where T : Delegate => method.Method;

	public static MethodInfo? MethodInfo(string assembly, string type, string name, Type[]? parameters = null,
		Type[]? generics = null)
		=> AccessTools.Method(Type(assembly, type), name, parameters, generics);

	public static IEnumerable<MethodInfo> MethodsOfName(Type type, string name)
		=> MethodsOfNameInternal(type, name, true);

	private static void ThrowForMethodsOfName(Type? type, string name)
		=> ThrowHelper.ThrowArgumentException($"No methods found for {type.FullDescription()}:{name}");

	public static IEnumerable<MethodInfo> MethodsOfNameSilentFail(Type? type, string name)
		=> MethodsOfNameInternal(type, name, false);

	private static IEnumerable<MethodInfo> MethodsOfNameInternal(Type? type, string name, bool throwWhenEmpty)
	{
		var methods = type?.GetMethods(AccessTools.allDeclared);
		if (methods is null)
		{
			if (throwWhenEmpty)
				ThrowForMethodsOfName(type, name);
			
			yield break;
		}

		var count = 0;
		for (var i = 0; i < methods.Length; i++)
		{
			if (methods[i].Name != name)
				continue;

			count++;
			yield return methods[i];
		}
		
		if (throwWhenEmpty && count < 1)
			ThrowForMethodsOfName(type, name);
	}

	public static FieldInfo? FieldInfo(string assembly, string type, string name)
		=> AccessTools.Field(Type(assembly, type), name);

	public static MethodInfo? GetGenericMethod(this Type type, string name, params Type[] typeArguments)
		=> type.GetMethod(name)?.MakeGenericMethod(typeArguments);

	public static MethodInfo? GetGenericMethod(this Type type, string name, BindingFlags bindingAttr,
		params Type[] typeArguments)
		=> type.GetMethod(name, bindingAttr)?.MakeGenericMethod(typeArguments);

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
				|| !targetTypesEnumerator.Current!.IsAssignableFrom(typesEnumerator.Current))
				return false;
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

	public static Type GetDelegateReturnType(this Type type)
	{
		Guard.IsTypeAssignableToType(type, typeof(Delegate));
		return type.GetMethod("Invoke")!.ReturnType;
	}

	public static ParameterInfo[] GetDelegateParameters(this Type type)
	{
		Guard.IsTypeAssignableToType(type, typeof(Delegate));
		return type.GetMethod("Invoke")!.GetParameters();
	}

	public static bool HasGenericInterfaceDefinition(this Type type, Type interfaceType)
	{
		Guard.IsNotNull(type);

		return Array.Exists(type.GetInterfaces(), @interface
			=> @interface.IsGenericType
			&& @interface.GetGenericTypeDefinition() == interfaceType);
	}

	[SecuritySafeCritical]
	public static IntPtr GetFunctionPointer(this MethodBase method) => method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T> GetFunctionPointer<T>(this MethodBase method)
		=> (delegate*<T>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T_in, T_out> GetFunctionPointer<T_in, T_out>(this MethodBase method)
		=> (delegate*<T_in, T_out>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T1, T2, T_out> GetFunctionPointer<T1, T2, T_out>(this MethodBase method)
		=> (delegate*<T1, T2, T_out>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T1, T2, T3, T_out> GetFunctionPointer<T1, T2, T3, T_out>(this MethodBase method)
		=> (delegate*<T1, T2, T3, T_out>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T1, T2, T3, T4, T_out>
		GetFunctionPointer<T1, T2, T3, T4, T_out>(this MethodBase method)
		=> (delegate*<T1, T2, T3, T4, T_out>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T1, T2, T3, T4, T5, T_out>
		GetFunctionPointer<T1, T2, T3, T4, T5, T_out>(this MethodBase method)
		=> (delegate*<T1, T2, T3, T4, T5, T_out>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T1, T2, T3, T4, T5, T6, T_out>
		GetFunctionPointer<T1, T2, T3, T4, T5, T6, T_out>(this MethodBase method)
		=> (delegate*<T1, T2, T3, T4, T5, T6, T_out>)method.MethodHandle.GetFunctionPointer();

	[SecuritySafeCritical]
	public static unsafe delegate*<T1, T2, T3, T4, T5, T6, T7, T_out>
		GetFunctionPointer<T1, T2, T3, T4, T5, T6, T7, T_out>(this MethodBase method)
		=> (delegate*<T1, T2, T3, T4, T5, T6, T7, T_out>)method.MethodHandle.GetFunctionPointer();

	/// <summary>
	/// Finds a constructor with parameters that are either assignable to or assignable from the types in the provided
	/// parameters array
	/// </summary>
	/// <param name="type">The type of the object to construct</param>
	/// <param name="parameters">
	/// An array of parameter types that are either assignable to or from the parameters of a constructor for the
	/// target type
	/// </param>
	/// <param name="searchForStatic">Search for static constructors instead of instance constructors</param>
	/// <param name="throwOnFailure">Throw an exception on failure to find a matching constructor</param>
	/// <returns>
	/// A ConstructorInfo that best matches the provided arguments. Null on failure if throwOnFailure is set to false
	/// </returns>
	public static ConstructorInfo? MatchingConstructor(Type type, Type[]? parameters = null,
		bool searchForStatic = false, bool throwOnFailure = true)
	{
		Guard.IsNotNull(type);

		parameters ??= Array.Empty<Type>();
		var flags = searchForStatic ? AccessTools.all & ~BindingFlags.Instance : AccessTools.all & ~BindingFlags.Static;

		return TryGetConstructor(type, flags, paramTypes => paramTypes.AreAssignableFrom(parameters))
			?? TryGetConstructor(type, flags, paramTypes => paramTypes.AreAssignableTo(parameters))
			?? (throwOnFailure
				? ThrowHelper.ThrowInvalidOperationException<ConstructorInfo>(
					$"No constructor found for type: {type}, parameters: {
						parameters.ToStringSafeEnumerable()}, static: {searchForStatic}, found constructors: {
						type.GetConstructors(flags).ToStringSafeEnumerable()}")
				: null);
	}

	private static ConstructorInfo? TryGetConstructor(Type type, BindingFlags flags,
		Predicate<IEnumerable<Type>> predicate)
		=> type.GetConstructors(flags).FirstOrDefault(c
			=> predicate(c.GetParameters().Select(static p => p.ParameterType)));

	public static T? CreateConstructorDelegate<T>(Type type, params Type[] parameters) where T : Delegate
	{
		try
		{
			Guard.IsTypeAssignableToType(typeof(T), typeof(Delegate));
			var delegateReturnType = typeof(T).GetDelegateReturnType();

			var dm = new DynamicMethod($"FisheryConstructorFunc_{type.Name}{parameters.Length.ToString()}",
				delegateReturnType, parameters, type, true);
			var il = dm.GetILGenerator();
	
			var needsTempVar = type.IsValueType
				&& parameters.Length == 0
				&& !Array.Exists(type.GetConstructors(AccessTools.allDeclared & ~BindingFlags.Static),
					static c => c.GetParameters().Length == 0);
		
			var tempVariable = needsTempVar ? FishTranspiler.NewLocalVariable(type, il) : default;
	
			if (needsTempVar)
				il.Emit(tempVariable.Load().Address());
	
			for (var i = 0; i < parameters.Length; i++)
				il.Emit(FishTranspiler.Argument(i));
	
			il.Emit(FishTranspiler.New(type, parameters));
	
			if (needsTempVar)
				il.Emit(tempVariable.Load());

			if (delegateReturnType != type)
			{
				if (type.IsValueType
					&& (delegateReturnType == typeof(object)
					|| (delegateReturnType.IsInterface && type.IsAssignableTo(delegateReturnType))))
				{
					il.Emit(FishTranspiler.Box(type));
				}
				else if (delegateReturnType != typeof(IConvertible)
					&& delegateReturnType.IsAssignableTo(typeof(IConvertible))
					&& type.IsAssignableTo(typeof(IConvertible)))
				{
					il.Emit(FishTranspiler.Call(Array.Find(typeof(Convert).GetMethods(),
							static m => m.GetParameters().Length == 1 && m.Name == "Type")
						.MakeGenericMethod(type, delegateReturnType)));
				}
				else if (!type.IsAssignableTo(delegateReturnType))
				{
					il.Emit(FishTranspiler.Cast(delegateReturnType));
				}
			}

			il.Emit(FishTranspiler.Return);
	
			return (T)dm.CreateDelegate(typeof(T));
		}
		catch (Exception e)
		{
			Log.Error($"Failed creating a constructor delegate for {type.FullDescription()} with parameters {
				parameters.ToStringSafeEnumerable()}\n{e}\n{new StackTrace()}");
			return null;
		}
	}

	[SecuritySafeCritical]
	public static string FullDescription(this MemberInfo? memberInfo)
		=> memberInfo switch
		{
			MethodBase methodBase => GeneralExtensions.FullDescription(methodBase),
			Type type => GeneralExtensions.FullDescription(type),
			_ => FullDescriptionInternal(memberInfo)
		};
	
	private static string FullDescriptionInternal(MemberInfo? memberInfo)
	{
		if (memberInfo is null)
			return "NULL";

		using var pooledStringBuilder = new PooledStringBuilder();
		var stringBuilder = pooledStringBuilder.Builder;

		switch (memberInfo)
		{
			case FieldInfo fieldInfo:
				AppendFieldPrefix(fieldInfo, stringBuilder);
				break;
			case PropertyInfo propertyInfo:
				AppendPropertyPrefix(propertyInfo, stringBuilder);
				break;
			case EventInfo eventInfo:
				AppendEventPrefix(eventInfo, stringBuilder);
				break;
		}

		if (memberInfo.DeclaringType != null)
			stringBuilder.AppendType(memberInfo.DeclaringType).Append("::");
		
		stringBuilder.Append(memberInfo.Name);

		if (memberInfo is PropertyInfo propertyInfo1)
			AppendPropertyPostfix(stringBuilder, propertyInfo1);

		return pooledStringBuilder.ToString();
	}

	private static void AppendFieldPrefix(FieldInfo fieldInfo, StringBuilder stringBuilder)
	{
		if (fieldInfo.IsPublic)
			stringBuilder.Append("public ");

		if (fieldInfo is { IsLiteral: true, IsInitOnly: false })
			stringBuilder.Append("const ");
		else if (fieldInfo.IsStatic)
			stringBuilder.Append("static ");

		stringBuilder.AppendType(fieldInfo.FieldType).Append(' ');
	}

	private static void AppendPropertyPrefix(PropertyInfo propertyInfo, StringBuilder stringBuilder)
	{
		if ((propertyInfo.GetMethod?.IsPublic ?? false)
			|| (propertyInfo.SetMethod?.IsPublic ?? false))
		{
			stringBuilder.Append("public ");
		}

		if ((propertyInfo.GetMethod?.IsStatic ?? true)
			&& (propertyInfo.SetMethod?.IsStatic ?? true))
		{
			stringBuilder.Append("static ");
		}

		stringBuilder.AppendType(propertyInfo.PropertyType).Append(' ');
	}

	private static void AppendPropertyPostfix(StringBuilder stringBuilder, PropertyInfo propertyInfo)
	{
		stringBuilder.Append(" { ");

		var getMethod = propertyInfo.GetMethod;
		var setMethod = propertyInfo.SetMethod;
		
		if (getMethod is not null)
		{
			if (getMethod.IsPrivate && !(setMethod?.IsPrivate ?? true))
				stringBuilder.Append("private ");

			stringBuilder.Append("get; ");
		}

		if (setMethod is not null)
		{
			if (setMethod.IsPrivate && !(getMethod?.IsPrivate ?? true))
				stringBuilder.Append("private ");

			stringBuilder.Append("set; ");
		}

		stringBuilder.Append('}');
	}

	private static void AppendEventPrefix(EventInfo eventInfo, StringBuilder stringBuilder)
	{
		if ((eventInfo.AddMethod?.IsPublic ?? true)
			&& (eventInfo.RemoveMethod?.IsPublic ?? true))
		{
			stringBuilder.Append("public ");
		}

		if ((eventInfo.AddMethod?.IsStatic ?? true)
			&& (eventInfo.RemoveMethod?.IsStatic ?? true))
		{
			stringBuilder.Append("static ");
		}

		stringBuilder.Append("event ");

		stringBuilder.AppendType(eventInfo.EventHandlerType).Append(' ');
	}

	private static StringBuilder AppendType(this StringBuilder stringBuilder, Type type)
		=> stringBuilder.Append(type.FullDescription());

	private static T CreateInstanceUsingActivator<T>(params object?[] args)
		=> (T)Activator.CreateInstance(typeof(T),
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance, null,
			args, null, null);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T New<T>() => Create<T>.@new();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T_argument>(T_argument argument)
		=> Create<T_result, T_argument>.@new(argument);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T_result New<T_result, T1, T2>(T1 arg1, T2 arg2) => Create<T_result, T1, T2>.@new(arg1, arg2);

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
		internal static volatile Func<T> @new = SimpleFunc;

		static Create() => Task.Run(ConstructorTask);

		private static T SimpleFunc() => (T)Activator.CreateInstance(typeof(T), true);

		private static void ConstructorTask()
		{
			if (CreateConstructorDelegate<Func<T>>(typeof(T), Array.Empty<Type>()) is { } newFunc)
				@new = newFunc;
		}
	}

	private static class Create<TResult, TArgument>
	{
		internal static volatile Func<TArgument, TResult> @new = SimpleFunc;

		static Create() => Task.Run(ConstructorTask);

		private static TResult SimpleFunc(TArgument argument) => CreateInstanceUsingActivator<TResult>(argument);

		private static void ConstructorTask()
		{
			if (CreateConstructorDelegate<Func<TArgument, TResult>>(typeof(TResult), typeof(TArgument)) is { } newFunc)
				@new = newFunc;
		}
	}

	private static class Create<TResult, T1, T2>
	{
		internal static volatile Func<T1, T2, TResult> @new = SimpleFunc;

		static Create() => Task.Run(ConstructorTask);

		private static TResult SimpleFunc(T1 first, T2 second) => CreateInstanceUsingActivator<TResult>(first, second);

		private static void ConstructorTask()
		{
			if (CreateConstructorDelegate<Func<T1, T2, TResult>>(typeof(TResult), typeof(T1), typeof(T2)) is { } newFunc)
				@new = newFunc;
		}
	}

	private static class Create<TResult, T1, T2, T3>
	{
		internal static volatile Func<T1, T2, T3, TResult> @new = SimpleFunc;

		static Create() => Task.Run(ConstructorTask);

		private static TResult SimpleFunc(T1 first, T2 second, T3 third)
			=> CreateInstanceUsingActivator<TResult>(first, second, third);

		private static void ConstructorTask()
		{
			if (CreateConstructorDelegate<Func<T1, T2, T3, TResult>>(typeof(TResult), typeof(T1), typeof(T2), typeof(T3))
				is { } newFunc)
			{
				@new = newFunc;
			}
		}
	}

	private static class Create<TResult, T1, T2, T3, T4>
	{
		internal static volatile Func<T1, T2, T3, T4, TResult> @new = SimpleFunc;

		static Create() => Task.Run(ConstructorTask);

		private static TResult SimpleFunc(T1 first, T2 second, T3 third, T4 fourth)
			=> CreateInstanceUsingActivator<TResult>(first, second, third, fourth);

		private static void ConstructorTask()
		{
			if (CreateConstructorDelegate<Func<T1, T2, T3, T4, TResult>>(typeof(TResult), typeof(T1), typeof(T2), typeof(T3),
				typeof(T4)) is { } newFunc)
			{
				@new = newFunc;
			}
		}
	}

	private static class Create<TResult, T1, T2, T3, T4, T5>
	{
		internal static volatile Func<T1, T2, T3, T4, T5, TResult> @new = SimpleFunc;

		static Create() => Task.Run(ConstructorTask);

		private static TResult SimpleFunc(T1 first, T2 second, T3 third, T4 fourth, T5 fifth)
			=> CreateInstanceUsingActivator<TResult>(first, second, third, fourth, fifth);

		private static void ConstructorTask()
		{
			if (CreateConstructorDelegate<Func<T1, T2, T3, T4, T5, TResult>>(typeof(TResult), typeof(T1), typeof(T2),
				typeof(T3), typeof(T4), typeof(T5)) is { } newFunc)
			{
				@new = newFunc;
			}
		}
	}
}