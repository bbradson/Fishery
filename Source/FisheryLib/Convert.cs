// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace FisheryLib;
public static class Convert
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TTo To<TFrom, TTo>(TFrom from) where TFrom : IConvertible
		=> FunctionPointers.Convert<TFrom>.To<TTo>.Default(from, CultureInfo.InvariantCulture);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TTo To<TFrom, TTo>(TFrom from, IFormatProvider provider) where TFrom : IConvertible
		=> FunctionPointers.Convert<TFrom>.To<TTo>.Default(from, provider);
}