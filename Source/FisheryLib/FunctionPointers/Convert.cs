// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security;
using FisheryLib.Collections;

namespace FisheryLib.FunctionPointers;

public static class Convert<TFrom>
{
	public static class To<TTo>
	{
		public static readonly unsafe delegate*<TFrom, IFormatProvider, TTo> Default
			= Convert.GetDefaultConverter<TFrom, TTo>();
	}

	/// <summary>
	/// Used when TFrom isn't a known sealed type, e.g. with just the IConvertible interface
	/// </summary>
	internal static class Fallback<TTo>
	{
		internal static readonly Func<TFrom, IFormatProvider, TTo> Func = Convert.GetDefaultConverterFunc<TFrom, TTo>();
		internal static TTo FallbackMethod(TFrom from, IFormatProvider provider) => Func(from, provider);
	}
}

internal static class Convert
{
	private static class FromObjectTo<TTo>
	{
		private static FishTable<RuntimeTypeHandle, FuncWrapper> _delegates = new();

		static FromObjectTo()
			=> _delegates.EntryAdded += static pair =>
			{
				var from = Type.GetTypeFromHandle(pair.Key);
				ref var entryRef = ref _delegates.GetReference(pair.Key);
				var to = typeof(TTo);

				entryRef.Func = CreateObjectConverter(from, to);
			};

		private static Func<object, IFormatProvider, TTo> CreateObjectConverter(Type from, Type to)
		{
			try
			{
				var dm = new DynamicMethod($"ObjectConverter_{from}_{to}", to,
					[typeof(object), typeof(IFormatProvider)], typeof(FromObjectTo<TTo>), true);
				var il = dm.GetILGenerator();

				il.Emit(FishTranspiler.This);

				if (from.IsValueType)
				{
					il.Emit(FishTranspiler.Call(typeof(FromObjectTo<TTo>), nameof(UnboxSafely),
						generics: [from]));
				}

				il.Emit(FishTranspiler.Argument(1));

				il.Emit(FishTranspiler.Call(GetConverterMethod(from, to)));

				il.Emit(FishTranspiler.Return);

				return (Func<object, IFormatProvider, TTo>)dm.CreateDelegate(typeof(Func<object, IFormatProvider, TTo>));
			}
			catch (Exception ex)
			{
				return ThrowHelper.ThrowInvalidOperationException<Func<object, IFormatProvider, TTo>>(
					$"Failed to compile object converter from {from.FullDescription()} to {
						to.FullDescription()}\n{ex}");
			}
		}

		public static TTo Method(object obj, IFormatProvider provider)
			=> _delegates.GetOrAdd(obj.GetType().TypeHandle).Func(obj, provider);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		private static ref T UnboxSafely<T>(object obj) where T : struct
			=> ref Unsafe.Unbox<T>(obj is T ? obj : default(T));

		private record struct FuncWrapper
		{
			public Func<object, IFormatProvider, TTo> Func;
		}
	}

	private static MethodInfo GetConverterMethod<TFrom>(Type to) => GetConverterMethod(typeof(TFrom), to);
	
	private static MethodInfo GetConverterMethod(Type from, Type to)
	{
		try
		{
			var info = _convertMethodsByType.TryGetValue(to, out var value)
				&& TryFindInterfaceMethod(from, value) is { } methodByName
				? methodByName
				: TryFindMethodThroughReflection(to, from);

			if (info is null)
				ThrowFailedToFindConverterInvalidOperationException(from, to);

			if (info.IsStatic)
			{
				if (info.GetParameters() is [_])
					info = GenerateWrapperMethod(from, to, info);
			}
			else if (info.GetParameters().Length > 1)
			{
				info = typeof(Convert)
					.GetMethod(nameof(ConvertToType), AccessTools.allDeclared)!
					.MakeGenericMethod(from, to);
			}

			return info;
		}
		catch (Exception e)
		{
			Log.Error($"Exception thrown while trying to get converter method:\n{e}");
			throw;
		}
	}

	private static MethodInfo? TryFindInterfaceMethod(Type from, MethodInfo interfaceMethod)
	{
		if (!typeof(IConvertible).IsAssignableFrom(from))
			return null;
		
		var interfaceMap = from.GetInterfaceMap(typeof(IConvertible));
		
		var index = Array.IndexOf(interfaceMap.InterfaceMethods, interfaceMethod);
		
		return index < 0 ? null : interfaceMap.TargetMethods[index];
	}

	[DoesNotReturn]
	private static MethodInfo ThrowFailedToFindConverterInvalidOperationException(Type from, Type to)
		=> ThrowHelper.ThrowInvalidOperationException<MethodInfo>($"Failed to find converter from type '{
			from.FullDescription()}' to type '{to.FullDescription()}'");

	private static MethodInfo? TryFindMethodThroughReflection(Type to, Type from)
	{
		var parameters = new[] { from };
		
		var fromMethods = from.GetMethods(BindingFlags.Public | BindingFlags.Static);
		if (TryFindMethod(fromMethods, "op_Implicit", parameters, to) is { } info)
			return info;
		
		var toMethods = to.GetMethods(BindingFlags.Public | BindingFlags.Static);
		return TryFindMethod(toMethods, "op_Implicit", parameters, from)
			?? TryFindMethod(fromMethods, "op_Explicit", parameters, to)
			?? TryFindMethod(toMethods, "op_Explicit", parameters, from)
			?? (!from.IsValueType
				? typeof(FromObjectTo<>).MakeGenericType(to).GetMethod(nameof(FromObjectTo<object>.Method))
				: ThrowFailedToFindConverterInvalidOperationException(from, to));
	}

	private static MethodInfo GenerateWrapperMethod(Type from, Type to, MethodBase method)
	{
		var dm = new DynamicMethod($"Wrapper_op_{from.Name}_{to.Name}", to,
			[from.IsValueType ? from.MakeByRefType() : from, typeof(IFormatProvider)], from, true);
		var il = dm.GetILGenerator();

		il.Emit(FishTranspiler.Argument(0));
		
		if (from.IsValueType)
			il.Emit(FishTranspiler.LoadIndirectly(from));
		
		il.Emit(FishTranspiler.Call(method));
		il.Emit(FishTranspiler.Return);

		return dm.CreateDelegate(from.IsValueType
			? typeof(RefConverterFunc<,>).MakeGenericType(from, to)
			: typeof(Func<,,>).MakeGenericType(from, typeof(IFormatProvider), to)).Method;
	}

	private static MethodInfo? TryFindMethod(MethodInfo[] array, string name, Type[] parameters, Type returnType)
	{
		for (var i = array.Length; i-- > 0;)
		{
			var method = array[i];
			if (method.Name != name || method.ReturnType != returnType)
				continue;

			var testedParameters = method.GetParameters();
			if (testedParameters.Length != parameters.Length)
				continue;

			var parametersMatch = true;
			for (var j = testedParameters.Length; j-- > 0;)
			{
				if (testedParameters[j].ParameterType == parameters[j])
					continue;

				parametersMatch = false;
				break;
			}

			if (parametersMatch)
				return method;
		}

		return null;
	}

	internal static Func<TFrom, IFormatProvider, TTo> GetDefaultConverterFunc<TFrom, TTo>()
		=> (Func<TFrom, IFormatProvider, TTo>)GetConverterMethod<TFrom>(typeof(TTo))
			.CreateDelegate(typeof(Func<TFrom, IFormatProvider, TTo>));

	internal static unsafe delegate*<TFrom, IFormatProvider, TTo> GetDefaultConverter<TFrom, TTo>()
		=> !typeof(TFrom).IsValueType
			&& (!typeof(TFrom).IsSealed || typeof(TFrom).IsInterface || typeof(TFrom).IsAbstract)
				? &Convert<TFrom>.Fallback<TTo>.FallbackMethod
				: (delegate*<TFrom, IFormatProvider, TTo>)GetConverterMethod<TFrom>(typeof(TTo))
					.MethodHandle.GetFunctionPointer();

	private static TTo ConvertToType<TFrom, TTo>(TFrom from, IFormatProvider provider) where TFrom : IConvertible
		=> (TTo)from.ToType(typeof(TTo), provider);

	private static readonly FishTable<Type, MethodInfo> _convertMethodsByType
		= typeof(IConvertible).GetMethods()
			.Where(static method
				=> method.GetParameters() is [{ ParameterType: var type }] && type == typeof(IFormatProvider))
			.ToFishTable(static info => info.ReturnType);
}

[SuppressMessage("ReSharper", "TypeParameterCanBeVariant")]
file delegate TReturn RefConverterFunc<TInstance, TReturn>(TInstance instance, IFormatProvider provider);