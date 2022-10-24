// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace FisheryLib.FunctionPointers;
public static class Convert<TFrom> where TFrom : IConvertible
{
	public static class To<TTo>
	{
		public static readonly unsafe delegate*<TFrom, IFormatProvider, TTo> Default = Convert.GetDefaultConverter<TFrom, TTo>();
	}

	/// <summary>
	/// Used when TFrom isn't a known sealed type, i.e. with just the IConvertible interface
	/// </summary>
	internal static class Fallback<TTo>
	{
		internal static readonly Func<TFrom, IFormatProvider, TTo> Func = Convert.GetDefaultConverterFunc<TFrom, TTo>();
		internal static TTo FallbackMethod(TFrom from, IFormatProvider provider) => Func(from, provider);
	}
}

internal static class Convert
{
	private static MethodInfo GetConverterMethod<TFrom>(Type to) where TFrom : IConvertible
	{
		var info = typeof(TFrom).GetMethod(GetConvertMethodName(to));
		if (info.GetParameters().Length > 1)
		{
			info = typeof(Convert)
				.GetMethod(nameof(ConvertToType), AccessTools.allDeclared)
				.MakeGenericMethod(new[] { typeof(TFrom), to });
		}

		return info;
	}

	internal static Func<TFrom, IFormatProvider, TTo> GetDefaultConverterFunc<TFrom, TTo>() where TFrom : IConvertible
		=> (Func<TFrom, IFormatProvider, TTo>)GetConverterMethod<TFrom>(typeof(TTo))
		.CreateDelegate(typeof(Func<TFrom, IFormatProvider, TTo>));

	internal static unsafe delegate*<TFrom, IFormatProvider, TTo> GetDefaultConverter<TFrom, TTo>() where TFrom : IConvertible
		=> !typeof(TFrom).IsValueType && (!typeof(TFrom).IsSealed || typeof(TFrom).IsInterface || typeof(TFrom).IsAbstract)
		? &Convert<TFrom>.Fallback<TTo>.FallbackMethod
		: (delegate*<TFrom, IFormatProvider, TTo>)GetConverterMethod<TFrom>(typeof(TTo))
		.MethodHandle.GetFunctionPointer();

	private static TTo ConvertToType<TFrom, TTo>(TFrom from, IFormatProvider provider) where TFrom : IConvertible
		=> (TTo)from.ToType(typeof(TTo), provider);

	private static string GetConvertMethodName(Type type)
		=> type == typeof(bool)
		? nameof(IConvertible.ToBoolean)
		: type == typeof(char)
		? nameof(IConvertible.ToChar)
		: type == typeof(sbyte)
		? nameof(IConvertible.ToSByte)
		: type == typeof(byte)
		? nameof(IConvertible.ToByte)
		: type == typeof(short)
		? nameof(IConvertible.ToInt16)
		: type == typeof(ushort)
		? nameof(IConvertible.ToUInt16)
		: type == typeof(int)
		? nameof(IConvertible.ToInt32)
		: type == typeof(uint)
		? nameof(IConvertible.ToUInt32)
		: type == typeof(long)
		? nameof(IConvertible.ToInt64)
		: type == typeof(ulong)
		? nameof(IConvertible.ToUInt64)
		: type == typeof(float)
		? nameof(IConvertible.ToSingle)
		: type == typeof(double)
		? nameof(IConvertible.ToDouble)
		: type == typeof(decimal)
		? nameof(IConvertible.ToDecimal)
		: type == typeof(DateTime)
		? nameof(IConvertible.ToDateTime)
		: type == typeof(string)
		? nameof(IConvertible.ToString)
		//: type == typeof(object)
		//? value
		: nameof(IConvertible.ToType);
}