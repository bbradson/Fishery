// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;

namespace FisheryLib.Utility;

public static class FishMath
{
	public const double PHI
		= 1.61803398874989484820458683436563811772030917980576286213544862270526046281890244970720720418939113748475;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int TrailingZeroCount(uint value)
		=> value == 0
			? 32
			: _trailingZeroCountDeBruijn[(int)(((value & unchecked(0 - value)) * 125613361) >> 27)];
	
	public static int NextPowerOfTwo(int value)
	{
#if NETCORE
			return 1L << (63 - BitOperations.LeadingZeroCount(value));
#else
		value--;
		value |= value >> 1;
		value |= value >> 2;
		value |= value >> 4;
		value |= value >> 8;
		value |= value >> 16;
		value++;
		return value;
#endif
	}
	
	private static byte[] _trailingZeroCountDeBruijn
		=
		[
			0, 1, 28, 2, 29, 14, 24, 3, 30, 22,
			20, 15, 25, 17, 4, 8, 31, 27, 13, 23,
			21, 19, 16, 7, 26, 12, 18, 6, 11, 5,
			10, 9
		];
}