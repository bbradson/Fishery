// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Security;

namespace FisheryLib.FunctionPointers;

public abstract class MethodFactory(MethodInfo[] methods, string namePrefix, bool byRef)
{
	protected readonly MethodInfo[] _methods = methods;

	protected readonly ConcurrentDictionary<Type, MethodInfo> _methodsByType = new();

	protected readonly string _namePrefix = namePrefix;

	protected readonly bool _byRef = byRef;

	public MethodInfo GetGeneric(string name, params Type[] typeArguments)
		=> GetNamed(name).MakeGenericMethod(typeArguments);

	public MethodInfo GetNamed(string name)
	{
		foreach (var method in _methods)
		{
			if (method.Name == name)
				return method;
		}

		return ThrowHelper.ThrowArgumentException<MethodInfo>($"No method found with name: {name}");
	}

	protected MethodInfo? GetSpecializedMethod(Type type)
	{
		if (_byRef)
			type = type.MakeByRefType();

		for (var i = 0; i < _methods.Length; i++)
		{
			var method = _methods[i];
			if (!method.IsGenericMethod && method.GetParameters()[0].ParameterType == type)
				return method;
		}

		return null;
	}

	public MethodInfo GetForType(Type type)
		=> _methodsByType.TryGetValue(type, out var value)
			? value
			: _methodsByType[type] = GetOrMakeMethodForType(type);

	protected abstract MethodInfo GetOrMakeMethodForType(Type type);

	[SecuritySafeCritical]
	public IntPtr GetFunctionPointer(Type type) => GetForType(type).GetFunctionPointer();

	protected static MethodInfo[] GetMethodInfoArray(Type type, Func<MethodInfo, bool> predicate)
		=> type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
			.Where(predicate).ToArray();

	public new class Equals(Type type, string dynamicMethodNamePrefix, bool byRef)
		: MethodFactory(GetMethodInfoArraySafely(type), dynamicMethodNamePrefix, byRef)
	{
		protected override MethodInfo GetOrMakeMethodForType(Type type)
			=> GetSpecializedMethod(type)
				?? (type.IsAssignableTo(typeof(IEquatable<>), type)
					? GetGeneric(type.IsValueType
						? nameof(FunctionPointers.Equals.Methods.EquatableValueType)
						: nameof(FunctionPointers.Equals.Methods.EquatableReferenceType), type)
					: type.IsNullable(out var genericArgument)
					&& genericArgument.IsAssignableTo(typeof(IEquatable<>), genericArgument)
						? GetGeneric(nameof(FunctionPointers.Equals.Methods.Nullable), genericArgument)
						: type.IsEnum
							? GetGeneric(Type.GetTypeCode(Enum.GetUnderlyingType(type))
								switch
								{
									TypeCode.Int16
										or TypeCode.SByte
										or TypeCode.Byte
										or TypeCode.UInt16
										or TypeCode.Int32
										or TypeCode.UInt32 => nameof(FunctionPointers.Equals.Methods.Enum),
									TypeCode.Int64
										or TypeCode.UInt64 => nameof(FunctionPointers.Equals.Methods.LongEnum),
									_ => ThrowHelper.ThrowNotSupportedException<string>()
								}, type)
							: type.IsValueType
								? TryGetMethodByName(type) ?? CompileEqualsMethod(type)
								: GetGeneric(nameof(FunctionPointers.Equals.Methods.Object), type));
		
		protected MethodInfo? TryGetMethodByName(Type type)
		{
			var methods = type.GetMethods(AccessTools.allDeclared);
			if (_byRef)
				type = type.MakeByRefType();

			MethodInfo? methodThatNeedsWrapper = null;
			
			for (var i = 0; i < methods.Length; i++)
			{
				var method = methods[i];
				if (method.Name is not ("Equals" or "op_Equality")
					|| method.ReturnType != typeof(bool))
					continue;

				var parameters = method.GetParameters();
				var isGood = (parameters.Length == 2 && method.IsStatic) || parameters.Length == 1;
				for (var j = 0; j < parameters.Length; j++)
				{
					if (parameters[j].ParameterType != type)
						isGood = false;
				}

				if (!isGood)
					continue;

				if (_byRef || method.IsStatic)
					return method;
				else
					methodThatNeedsWrapper = method;
			}

			return methodThatNeedsWrapper != null ? CompileWrapperMethod(type, methodThatNeedsWrapper) : null;
		}

		protected MethodInfo CompileWrapperMethod(Type type, MethodInfo method)
		{
#if DEBUG
			Log.Message($"Method {method.FullDescription()} determined to be in need of wrapper");
#endif

			return GetGeneric(nameof(FunctionPointers.Equals.Methods.ValueType), type);
			
			// var dm = new DynamicMethod($"{_namePrefix}{type.Name}", typeof(bool),
			// 	new[] { type, type }, type, true);
			// var il = dm.GetILGenerator();
			//
			// il.Emit(FishTranspiler.ArgumentAddress(0));
			// il.Emit(FishTranspiler.Argument(1));
			// il.Emit(FishTranspiler.Call(method)); // throws NullRef somehow
			// il.Emit(FishTranspiler.Return);
			//
			// return dm.CreateDelegate(typeof(Func<,,>).MakeGenericType(type, type, typeof(bool))).Method;
		}

		protected MethodInfo CompileEqualsMethod(Type type)
		{
			try
			{
				var dm = new DynamicMethod($"{_namePrefix}{type.Name}", typeof(bool),
					_byRef ? [type.MakeByRefType(), type.MakeByRefType()] : [type, type], type, true);
				var il = dm.GetILGenerator();

				var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

				var needsJump = fields.Length > 1;

				var falseLabel = needsJump ? il.DefineLabel() : default;
				var trueLabel = needsJump ? il.DefineLabel() : default;

				for (var i = 0; i < fields.Length; i++)
				{
					var field = fields[i];

					for (var j = 0; j < 2; j++)
					{
						il.Emit(FishTranspiler.Argument(j));
						il.Emit(FishTranspiler.Field(field));
					}

					il.Emit(FishTranspiler.Call(GetForType(field.FieldType)));

					if (needsJump)
					{
						il.Emit(i == fields.Length - 1
							? FishTranspiler.GoTo_Short(trueLabel)
							: FishTranspiler.IfFalse_Short(falseLabel));
					}
				}

				if (needsJump)
				{
					il.MarkLabel(falseLabel);
					il.Emit(FishTranspiler.Constant(0));

					il.MarkLabel(trueLabel);
				}

				il.Emit(FishTranspiler.Return);

				return dm.CreateDelegate(
						(_byRef ? typeof(ByRefFunc<,,>) : typeof(Func<,,>)).MakeGenericType(type, type, typeof(bool)))
					.Method;
			}
			catch (Exception e)
			{
				return LogErrorAndReturnFallback(type, e);
			}
		}

		private MethodInfo LogErrorAndReturnFallback(Type type, Exception e)
		{
			Log.Error($"Failed compiling specialized Equals method for {
				type.FullDescription()}. Returning fallback instead.\n{e}{new StackTrace()}");

			return GetGeneric(nameof(FunctionPointers.Equals.Methods.ValueType), type);
		}
		
		private static MethodInfo[] GetMethodInfoArraySafely(Type type)
		{
			try
			{
				return GetMethodInfoArray(type, static m
					=> m.ReturnType == typeof(bool)
					&& m.GetParameters().Length == 2);
			}
			catch (Exception e)
			{
				Log.Error($"Exception while preparing method info array for type {type}:\n{e}");
				throw;
			}
		}

		private delegate TResult ByRefFunc<T1, T2, out TResult>(ref T1 arg1, ref T2 arg2);
	}
	
	public class HashCode(Type type, string dynamicMethodNamePrefix, bool byRef)
		: MethodFactory(GetMethodInfoArraySafely(type), dynamicMethodNamePrefix, byRef)
	{
		protected override MethodInfo GetOrMakeMethodForType(Type type)
			=> GetSpecializedMethod(type)
				?? (type.IsNullable(out var genericArgument)
					&& genericArgument.IsAssignableTo(typeof(IEquatable<>), genericArgument)
						? GetGeneric(nameof(FunctionPointers.GetHashCode.Methods.Nullable), genericArgument)
						: type.IsEnum
							? GetGeneric(Type.GetTypeCode(Enum.GetUnderlyingType(type))
								switch
								{
									TypeCode.Int16 => nameof(FunctionPointers.GetHashCode.Methods.ShortEnum),
									TypeCode.SByte => nameof(FunctionPointers.GetHashCode.Methods.SbyteEnum),
									TypeCode.Byte
										or TypeCode.UInt16
										or TypeCode.Int32
										or TypeCode.UInt32 => nameof(FunctionPointers.GetHashCode.Methods.Enum),
									TypeCode.Int64
										or TypeCode.UInt64 => nameof(FunctionPointers.GetHashCode.Methods.LongEnum),
									_ => ThrowHelper.ThrowNotSupportedException<string>()
								}, type)
							: type.IsValueType
								? TryGetMethodByName(type) ?? CompileHashCodeMethod(type)
								: GetGeneric(nameof(FunctionPointers.GetHashCode.Methods.Object), type));

		protected MethodInfo? TryGetMethodByName(Type type)
		{
			var methods = type.GetMethods(AccessTools.allDeclared);
			if (_byRef)
				type = type.MakeByRefType();
			
			MethodInfo? methodThatNeedsWrapper = null;
			
			for (var i = 0; i < methods.Length; i++)
			{
				var method = methods[i];
				if (method.Name != "GetHashCode"
					|| method.ReturnType != typeof(int))
					continue;

				var parameters = method.GetParameters();
				var isGood = (parameters.Length == 1 && method.IsStatic) || parameters.Length == 0;
				for (var j = 0; j < parameters.Length; j++)
				{
					if (parameters[j].ParameterType != type)
						isGood = false;
				}

				if (!isGood)
					continue;
				
				if (_byRef || method.IsStatic)
					return method;
				else
					methodThatNeedsWrapper = method;
			}

			return methodThatNeedsWrapper != null ? CompileWrapperMethod(type, methodThatNeedsWrapper) : null;
		}

		protected MethodInfo CompileWrapperMethod(Type type, MethodInfo method)
		{
#if DEBUG
			Log.Message($"Method {method.FullDescription()} determined to be in need of wrapper");
#endif

			return GetGeneric(nameof(FunctionPointers.GetHashCode.Methods.ValueType), type);
			
			// var dm = new DynamicMethod($"{_namePrefix}{type.Name}", typeof(int),
			// 	new[] { type }, type, true);
			// var il = dm.GetILGenerator();
			//
			// il.Emit(FishTranspiler.ArgumentAddress(0));
			// il.Emit(FishTranspiler.Call(method));
			// il.Emit(FishTranspiler.Return);
			//
			// return dm.CreateDelegate(typeof(HashCodeGetter<>).MakeGenericType(type)).Method;
		}

		private MethodInfo CompileHashCodeMethod(Type type)
		{
			try
			{
				var dm = new DynamicMethod($"{_namePrefix}{type.Name}", typeof(int),
					_byRef ? [type.MakeByRefType()] : [type], type, true);
				var il = dm.GetILGenerator();

				var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

				for (var i = 0; i < fields.Length; i++)
				{
					var field = fields[i];

					il.Emit(FishTranspiler.Argument(0));
					il.Emit(FishTranspiler.Field(field));

					if (field.FieldType != typeof(int))
						il.Emit(FishTranspiler.Call(GetForType(field.FieldType)));

					if (i < 1)
						continue;

					il.Emit(FishTranspiler.Call<Func<int, int, int>>(i == 1
						? global::FisheryLib.HashCode.Combine
						: global::FisheryLib.HashCode.CombineWith));
				}

				il.Emit(FishTranspiler.Return);

				return dm.CreateDelegate((_byRef ? typeof(ByRefHashCodeGetter<>) : typeof(HashCodeGetter<>))
					.MakeGenericType(type)).Method;
			}
			catch (Exception e)
			{
				return LogErrorAndReturnFallback(type, e);
			}
		}

		private MethodInfo LogErrorAndReturnFallback(Type type, Exception e)
		{
			Log.Error($"Failed compiling specialized GetHashCode method for {
				type.FullDescription()}. Returning fallback instead.\n{e}{new StackTrace()}");

			return GetGeneric(nameof(FunctionPointers.GetHashCode.Methods.ValueType), type);
		}

		private static MethodInfo[] GetMethodInfoArraySafely(Type type)
		{
			try
			{
				return GetMethodInfoArray(type, static m
					=> m.ReturnType == typeof(int)
					&& m.GetParameters().Length == 1);
			}
			catch (Exception e)
			{
				Log.Error($"Exception while preparing method info array for type {type}:\n{e}");
				throw;
			}
		}

		private delegate int ByRefHashCodeGetter<T>(ref T arg);
		private delegate int HashCodeGetter<in T>(T arg);
	}
}