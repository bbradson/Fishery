// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Runtime.CompilerServices;

namespace FisheryLib.FunctionPointers;
public static class GetHashCodeByRef<T>
{
	public static readonly unsafe delegate*<ref T, int> Default = (delegate*<ref T, int>)GetHashCodeByRef.MakeDelegate(typeof(T));
}

public static class GetHashCodeByRef
{
	public static class Methods
	{
		public static int Byte(ref byte obj)
			=> obj;

		public static int Int(ref int obj)
			=> obj;

		public static int Uint(ref uint obj)
			=> (int)obj;

		public static int Ushort(ref ushort obj)
			=> obj;

		public static unsafe int VoidPtr(ref void* obj)
			=> (int)obj;

		public static int Enum<T>(ref T obj) where T : struct
			=> JitHelpers.UnsafeEnumCast(obj).GetHashCode();

		public static int SbyteEnum<T>(ref T obj) where T : struct
			=> ((sbyte)JitHelpers.UnsafeEnumCast(obj)).GetHashCode();

		public static int ShortEnum<T>(ref T obj) where T : struct
			=> ((short)JitHelpers.UnsafeEnumCast(obj)).GetHashCode();

		public static int LongEnum<T>(ref T obj) where T : struct
			=> JitHelpers.UnsafeEnumCastLong(obj).GetHashCode();

		public static int ValueType<T>(ref T obj) where T : struct
			=> obj.GetHashCode();

		public static int Nullable<T>(ref T? obj) where T : struct
			=> obj.GetHashCode();

		public static int Reference<T>(ref T obj) where T : class
			=> RuntimeHelpers.GetHashCode(obj);

		public static int Object<T>(ref T obj)
			=> obj?.GetHashCode() ?? 0;
	}

	internal static unsafe IntPtr MakeDelegate(Type type)
		=> _getHashCodeMethods.Where(m => !m.IsGenericMethod)
			.FirstOrDefault(m
				=> m.GetParameters().First().ParameterType.GetElementType() == type)
			is { } specializedMethod
		? specializedMethod.GetFunctionPointer()
		: type.IsNullable(out var genericArgument)
			&& genericArgument.IsAssignableTo(typeof(IEquatable<>), genericArgument)
		? GetMethod(nameof(Methods.Nullable)).MakeGenericMethod(genericArgument).GetFunctionPointer()
		: GetMethod(
			type.IsEnum
			? Type.GetTypeCode(Enum.GetUnderlyingType(type))
				switch
			{
				TypeCode.Int16 => nameof(Methods.ShortEnum),
				TypeCode.SByte => nameof(Methods.SbyteEnum),
				TypeCode.Byte
				or TypeCode.UInt16
				or TypeCode.Int32
				or TypeCode.UInt32 => nameof(Methods.Enum),
				TypeCode.Int64
				or TypeCode.UInt64 => nameof(Methods.LongEnum),
				_ => ThrowHelper.ThrowNotSupportedException<string>()
			}
			: type.IsValueType
			? nameof(Methods.ValueType)
			: nameof(Methods.Object)
		).MakeGenericMethod(type).GetFunctionPointer();

	private static MethodInfo GetMethod(string name)
		=> Array.Find(_getHashCodeMethods, m => m.Name == name);

	private static MethodInfo[] _getHashCodeMethods
		= typeof(Methods)
		.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
		.Where(m
			=> m.ReturnType == typeof(int)
			&& m.GetParameters().Length == 1)
		.ToArray();
}