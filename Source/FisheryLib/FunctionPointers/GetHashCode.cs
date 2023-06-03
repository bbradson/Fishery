// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace FisheryLib.FunctionPointers;

public static class GetHashCode<T>
{
	public static readonly unsafe delegate*<T, int> Default
		= (delegate*<T, int>)FunctionPointers.GetHashCode.HashCodeMethods.GetFunctionPointer(typeof(T));
}

public static class GetHashCode
{
	[UsedImplicitly(ImplicitUseTargetFlags.Members)]
	public static class Methods
	{
		public static int Byte(byte obj) => obj;

		public static int Int(int obj) => obj;

		public static int Uint(uint obj) => (int)obj;

		public static int Ushort(ushort obj) => obj;

		public static unsafe int VoidPtr(void* obj) => (int)obj;

		public static int Enum<T>(T obj) where T : struct => JitHelpers.UnsafeEnumCast(obj).GetHashCode();

		public static int SbyteEnum<T>(T obj) where T : struct => ((sbyte)JitHelpers.UnsafeEnumCast(obj)).GetHashCode();

		public static int ShortEnum<T>(T obj) where T : struct => ((short)JitHelpers.UnsafeEnumCast(obj)).GetHashCode();

		public static int LongEnum<T>(T obj) where T : struct => JitHelpers.UnsafeEnumCastLong(obj).GetHashCode();

		public static int ValueType<T>(T obj) where T : struct => obj.GetHashCode();

		public static unsafe int Nullable<T>(T? obj) where T : struct
			=> !obj.HasValue ? 0 : GetHashCode<T>.Default(obj.GetValueOrDefault());

		public static int Reference<T>(T obj) where T : class => RuntimeHelpers.GetHashCode(obj);

		public static int Object<T>(T? obj) where T : class => obj?.GetHashCode() ?? 0;
	}

	public static readonly MethodFactory.HashCode HashCodeMethods
		= new(typeof(Methods), "FisheryHashCodeMethod_", false);
}