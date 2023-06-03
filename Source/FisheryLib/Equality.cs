// /* Copyright (c) 2022 bradson
//  * This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at https://mozilla.org/MPL/2.0/.
//  */

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib;

public static class Equality
{
	/// <summary>
	/// Generic overload of Equals to avoid boxing. Makes use of function pointers for best performance and does not
	/// throw on null for reference types.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool Equals<T>([AllowNull] this T @this, [AllowNull] T other)
		=> FunctionPointers.Equals<T>.Default(@this!, other!);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool Equals<T>(this ref T @this, ref T other)
		where T : struct // required by the compiler for 'this ref'
		=> FunctionPointers.EqualsByRef<T>.Default(ref @this, ref other);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool EqualsByRef<T>([AllowNull] ref T @this, [AllowNull] ref T other)
		=> FunctionPointers.EqualsByRef<T>.Default(ref @this!, ref other!);
}