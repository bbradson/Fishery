// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security;
using JetBrains.Annotations;

namespace FisheryLib.FunctionPointers;

public static class Equals<T>
{
	public static readonly unsafe delegate*<T, T, bool> Default
		= (delegate*<T, T, bool>)FunctionPointers.Equals.EqualsMethods.GetFunctionPointer(typeof(T));
}

public static class Equals
{
	[UsedImplicitly(ImplicitUseTargetFlags.Members)]
	public static class Methods
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Byte(byte x, byte y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Int(int x, int y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Uint(uint x, uint y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Ushort(ushort x, ushort y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IntPtr(IntPtr x, IntPtr y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool String(string x, string y) => string.Equals(x, y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe bool VoidPtr(void* x, void* y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public static bool RuntimeFieldHandle(RuntimeFieldHandle x, RuntimeFieldHandle y) => x.Value == y.Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public static bool RuntimeTypeHandle(RuntimeTypeHandle x, RuntimeTypeHandle y) => x.Value == y.Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public static bool RuntimeMethodHandle(RuntimeMethodHandle x, RuntimeMethodHandle y) => x.Value == y.Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Label(Label x, Label y) => x.label == y.label;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Enum<T>(T x, T y) where T : struct
			=> JitHelpers.UnsafeEnumCast(x) == JitHelpers.UnsafeEnumCast(y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool LongEnum<T>(T x, T y) where T : struct
			=> JitHelpers.UnsafeEnumCastLong(x) == JitHelpers.UnsafeEnumCastLong(y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool EquatableValueType<T>(T x, T y) where T : struct, IEquatable<T> => x.Equals(y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Nullable<T>(T? x, T? y) where T : struct, IEquatable<T>
			=> x.HasValue
				? y.HasValue && x.Value.Equals(y.Value)
				: !y.HasValue;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool EquatableReferenceType<T>(T? x, T? y) where T : class, IEquatable<T>
			=> x != null
				? y != null && x.Equals(y)
				: y == null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Reference<T>(T x, T y) where T : class => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Object<T>(T? x, T? y) where T : class
			=> x != null
				? y != null && x.Equals(y)
				: y == null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ValueType<T>(T x, T y) where T : struct => x.Equals(y);
	}

	public static readonly MethodFactory.Equals EqualsMethods = new(typeof(Methods), "FisheryEqualsMethod_", false);
}