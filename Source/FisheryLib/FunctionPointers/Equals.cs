// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Runtime.CompilerServices;

namespace FisheryLib.FunctionPointers;
public static class Equals<T>
{
	public static readonly unsafe delegate*<T, T, bool> Default = (delegate*<T, T, bool>)FunctionPointers.Equals.MakeDelegate(typeof(T));
}

public static class Equals
{
	public static class Methods
	{
		public static bool Byte(byte x, byte y)
			=> x == y;

		public static bool Int(int x, int y)
			=> x == y;

		public static bool Uint(uint x, uint y)
			=> x == y;

		public static bool Ushort(ushort x, ushort y)
			=> x == y;

		public static bool IntPtr(IntPtr x, IntPtr y)
			=> x == y;

		public static bool String(string x, string y)
			=> string.Equals(x, y);

		public static unsafe bool VoidPtr(void* x, void* y)
			=> x == y;

		public static bool RuntimeFieldHandle(RuntimeFieldHandle x, RuntimeFieldHandle y)
			=> x.Value == y.Value;

		public static bool RuntimeTypeHandle(RuntimeTypeHandle x, RuntimeTypeHandle y)
			=> x.Value == y.Value;

		public static bool RuntimeMethodHandle(RuntimeMethodHandle x, RuntimeMethodHandle y)
			=> x.Value == y.Value;

		public static bool Enum<T>(T x, T y) where T : struct
			=> JitHelpers.UnsafeEnumCast(x) == JitHelpers.UnsafeEnumCast(y);

		public static bool LongEnum<T>(T x, T y) where T : struct
			=> JitHelpers.UnsafeEnumCastLong(x) == JitHelpers.UnsafeEnumCastLong(y);

		public static bool EquatableValueType<T>(T x, T y) where T : struct, IEquatable<T>
			=> x.Equals(y);

		public static bool Nullable<T>(T? x, T? y) where T : struct, IEquatable<T>
			=> x.HasValue
			? y.HasValue && x.Value.Equals(y.Value)
			: !y.HasValue;

		public static bool EquatableReferenceType<T>(T x, T y) where T : IEquatable<T>
			=> x != null
			? y != null && x.Equals(y)
			: y == null;

		public static bool Reference<T>(T x, T y) where T : class
			=> x == y;

		public static bool Object<T>(T x, T y)
			=> x != null
			? y != null && x.Equals(y)
			: y == null;
	}

	internal static unsafe IntPtr MakeDelegate(Type type)
		=> _equalsMethods.Where(m => !m.IsGenericMethod)
			.FirstOrDefault(m
				=> m.GetParameters().First().ParameterType == type)
			is { } specializedMethod
		? specializedMethod.GetFunctionPointer()
		: type.IsAssignableTo(typeof(IEquatable<>), type)
		? GetMethod(
			type.IsValueType
			? nameof(Methods.EquatableValueType)
			: nameof(Methods.EquatableReferenceType))
			.MakeGenericMethod(type)/*.CreateDelegate(typeof(EqualsDelegate<>).MakeGenericType(type)).Method*/.GetFunctionPointer()
		: type.IsNullable(out var genericArgument)
			&& genericArgument.IsAssignableTo(typeof(IEquatable<>), genericArgument)
		? GetMethod(nameof(Methods.Nullable)).MakeGenericMethod(genericArgument)/*.CreateDelegate(typeof(EqualsDelegate<>).MakeGenericType(type)).Method*/.GetFunctionPointer()
		: GetMethod(
			type.IsEnum
			? Type.GetTypeCode(Enum.GetUnderlyingType(type))
				switch
				{
					TypeCode.Int16
					or TypeCode.SByte
					or TypeCode.Byte
					or TypeCode.UInt16
					or TypeCode.Int32
					or TypeCode.UInt32 => nameof(Methods.Enum),
					TypeCode.Int64
					or TypeCode.UInt64 => nameof(Methods.LongEnum),
					_ => ThrowHelper.ThrowNotSupportedException<string>()
				}
			: nameof(Methods.Object)
		).MakeGenericMethod(type)/*.CreateDelegate(typeof(EqualsDelegate<>).MakeGenericType(type)).Method*/.GetFunctionPointer();

	private static MethodInfo GetMethod(string name)
		=> Array.Find(_equalsMethods, m => m.Name == name);

	private static MethodInfo[] _equalsMethods
		= typeof(Methods)
		.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
		.Where(m
			=> m.ReturnType == typeof(bool)
			&& m.GetParameters().Length == 2)
		.ToArray();

	//internal delegate bool EqualsDelegate<T>(T x, T y);
}