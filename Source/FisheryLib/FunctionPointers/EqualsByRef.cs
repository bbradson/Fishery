// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Runtime.CompilerServices;

namespace FisheryLib.FunctionPointers;
public static class EqualsByRef<T>
{
	public static readonly unsafe delegate*<ref T, ref T, bool> Default = (delegate*<ref T, ref T, bool>)EqualsByRef.MakeDelegate(typeof(T));
}

public static class EqualsByRef
{
	public static class Methods
	{
		public static bool Byte(ref byte x, ref byte y)
			=> x == y;

		public static bool Int(ref int x, ref int y)
			=> x == y;

		public static bool Uint(ref uint x, ref uint y)
			=> x == y;

		public static bool Ushort(ref ushort x, ref ushort y)
			=> x == y;

		public static bool IntPtr(ref IntPtr x, ref IntPtr y)
			=> x == y;

		public static bool String(ref string x, ref string y)
			=> string.Equals(x, y);

		public static unsafe bool VoidPtr(ref void* x, ref void* y)
			=> x == y;

		public static bool RuntimeFieldHandle(ref RuntimeFieldHandle x, ref RuntimeFieldHandle y)
			=> x.Value == y.Value;

		public static bool RuntimeTypeHandle(ref RuntimeTypeHandle x, ref RuntimeTypeHandle y)
			=> x.Value == y.Value;

		public static bool RuntimeMethodHandle(ref RuntimeMethodHandle x, ref RuntimeMethodHandle y)
			=> x.Value == y.Value;

		public static bool Enum<T>(ref T x, ref T y) where T : struct
			=> JitHelpers.UnsafeEnumCast(x) == JitHelpers.UnsafeEnumCast(y);

		public static bool LongEnum<T>(ref T x, ref T y) where T : struct
			=> JitHelpers.UnsafeEnumCastLong(x) == JitHelpers.UnsafeEnumCastLong(y);

		public static bool EquatableValueType<T>(ref T x, ref T y) where T : struct, IEquatable<T>
			=> x.Equals(y);

		public static bool Nullable<T>(ref T? x, ref T? y) where T : struct, IEquatable<T>
			=> x.HasValue
			? y.HasValue && x.Value.Equals(y.Value)
			: !y.HasValue;

		public static bool EquatableReferenceType<T>(ref T x, ref T y) where T : IEquatable<T>
			=> x != null
			? y != null && x.Equals(y)
			: y == null;

		public static bool Reference<T>(ref T x, ref T y) where T : class
			=> x == y;

		public static bool Object<T>(ref T x, ref T y)
			=> x != null
			? y != null && x.Equals(y)
			: y == null;
	}

	internal static unsafe IntPtr MakeDelegate(Type type)
		=> _equalsMethods.Where(m => !m.IsGenericMethod)
			.FirstOrDefault(m
				=> m.GetParameters().First().ParameterType.GetElementType() == type)
			is { } specializedMethod
		? specializedMethod.GetFunctionPointer()
		: type.IsAssignableTo(typeof(IEquatable<>), type)
		? GetMethod(
			type.IsValueType
			? nameof(Methods.EquatableValueType)
			: nameof(Methods.EquatableReferenceType))
			.MakeGenericMethod(type).GetFunctionPointer()
		: type.IsNullable(out var genericArgument)
			&& genericArgument.IsAssignableTo(typeof(IEquatable<>), genericArgument)
		? GetMethod(nameof(Methods.Nullable)).MakeGenericMethod(genericArgument).GetFunctionPointer()
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
		).MakeGenericMethod(type).GetFunctionPointer();

	private static MethodInfo GetMethod(string name)
		=> Array.Find(_equalsMethods, m => m.Name == name);

	private static MethodInfo[] _equalsMethods
		= typeof(Methods)
		.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
		.Where(m
			=> m.ReturnType == typeof(bool)
			&& m.GetParameters().Length == 2)
		.ToArray();
}