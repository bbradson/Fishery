// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib;
public static class HashCode
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int Get<T>([AllowNull] T obj)
		=> FunctionPointers.GetHashCode<T>.Default(obj!);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int Get<T>([AllowNull] ref T obj)
		=> FunctionPointers.GetHashCodeByRef<T>.Default(ref obj!);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Combine<T_first, T_second>(T_first first, T_second second)
		//where T_first : notnull where T_second : notnull
		=> Combine(Get(first), Get(second));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Combine(int first, int second)
		=> unchecked((1009 * 9176) + first).CombineWith(second); //https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations/34006336#34006336

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Combine<T_first, T_second, T_third>(T_first first, T_second second, T_third third)
		//where T_first : notnull where T_second : notnull where T_third : notnull
		=> Combine(first, second).CombineWith(third);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Combine(int first, int second, int third)
		=> Combine(first, second).CombineWith(third);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Combine<T_first, T_second, T_third, T_fourth>(T_first first, T_second second, T_third third, T_fourth fourth)
		//where T_first : notnull where T_second : notnull where T_third : notnull where T_fourth : notnull
		=> Combine(first, second, third).CombineWith(fourth);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Combine(int first, int second, int third, int fourth)
		=> Combine(first, second, third).CombineWith(fourth);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int CombineWith(this int first, int second)
		=> unchecked((first * 9176) + second);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int CombineWith<T>(this int first, T second)
		//where T : notnull
		=> unchecked((first * 9176) + Get(second));
}