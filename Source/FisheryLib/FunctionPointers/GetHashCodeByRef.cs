// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib.FunctionPointers;

public static class GetHashCodeByRef<T>
{
	public static readonly unsafe delegate*<ref T, int> Default
		= (delegate*<ref T, int>)GetHashCodeByRef.HashCodeMethods.GetFunctionPointer(typeof(T));
}

[SuppressMessage("Naming", "CA1720")]
public static class GetHashCodeByRef
{
	[UsedImplicitly(ImplicitUseTargetFlags.Members)]
	public static class Methods
	{
		public static int Byte(ref byte obj) => obj;

		public static int Int(ref int obj) => obj;

		public static int Uint(ref uint obj) => (int)obj;

		public static int Ushort(ref ushort obj) => obj;

		public static unsafe int VoidPtr(ref void* obj) => (int)obj;

		public static int Enum<T>(ref T obj) where T : struct => JitHelpers.UnsafeEnumCast(obj).GetHashCode();

		public static int SbyteEnum<T>(ref T obj) where T : struct
			=> ((sbyte)JitHelpers.UnsafeEnumCast(obj)).GetHashCode();

		public static int ShortEnum<T>(ref T obj) where T : struct
			=> ((short)JitHelpers.UnsafeEnumCast(obj)).GetHashCode();

		public static int LongEnum<T>(ref T obj) where T : struct => JitHelpers.UnsafeEnumCastLong(obj).GetHashCode();

		public static int ValueType<T>(ref T obj) where T : struct => obj.GetHashCode();

		public static int Nullable<T>(ref T? obj) where T : struct => obj.GetHashCode();

		public static int Reference<T>(ref T obj) where T : class => RuntimeHelpers.GetHashCode(obj);

		public static int Object<T>(ref T? obj) where T : class => obj?.GetHashCode() ?? 0;
	}

	public static readonly MethodFactory.HashCode HashCodeMethods
		= new(typeof(Methods), "FisheryHashCodeByRefMethod_", true);
}