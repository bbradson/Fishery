// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security;
using JetBrains.Annotations;

namespace FisheryLib.FunctionPointers;

public static class EqualsByRef<T>
{
	public static readonly unsafe delegate*<ref T, ref T, bool> Default
		= (delegate*<ref T, ref T, bool>)EqualsByRef.EqualsMethods.GetFunctionPointer(typeof(T));
}

[SuppressMessage("Naming", "CA1720")]
public static class EqualsByRef
{
	[UsedImplicitly(ImplicitUseTargetFlags.Members)]
	public static class Methods
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Byte(ref byte x, ref byte y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Int(ref int x, ref int y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Uint(ref uint x, ref uint y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Ushort(ref ushort x, ref ushort y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IntPtr(ref IntPtr x, ref IntPtr y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SuppressMessage("Globalization", "CA1307")]
		[SuppressMessage("Globalization", "CA1309")]
		public static bool String(ref string x, ref string y) => string.Equals(x, y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe bool VoidPtr(ref void* x, ref void* y) => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public static bool RuntimeFieldHandle(ref RuntimeFieldHandle x, ref RuntimeFieldHandle y) => x.Value == y.Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public static bool RuntimeTypeHandle(ref RuntimeTypeHandle x, ref RuntimeTypeHandle y) => x.Value == y.Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public static bool RuntimeMethodHandle(ref RuntimeMethodHandle x, ref RuntimeMethodHandle y)
			=> x.Value == y.Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Label(ref Label x, ref Label y) => x.label == y.label;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Enum<T>(ref T x, ref T y) where T : struct
			=> JitHelpers.UnsafeEnumCast(x) == JitHelpers.UnsafeEnumCast(y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool LongEnum<T>(ref T x, ref T y) where T : struct
			=> JitHelpers.UnsafeEnumCastLong(x) == JitHelpers.UnsafeEnumCastLong(y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool EquatableValueType<T>(ref T x, ref T y) where T : struct, IEquatable<T> => x.Equals(y);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Nullable<T>(ref T? x, ref T? y) where T : struct, IEquatable<T>
			=> x.HasValue
				? y.HasValue && x.Value.Equals(y.Value)
				: !y.HasValue;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool EquatableReferenceType<T>(ref T? x, ref T? y) where T : class, IEquatable<T>
			=> x != null
				? y != null && x.Equals(y)
				: y == null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Reference<T>(ref T x, ref T y) where T : class => x == y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Object<T>(ref T? x, ref T? y) where T : class
			=> x != null
				? y != null && x.Equals(y)
				: y == null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ValueType<T>(ref T x, ref T y) where T : struct => x.Equals(y);
	}

	public static readonly MethodFactory.Equals EqualsMethods = new(typeof(Methods), "FisheryEqualsByRefMethod_", true);
}