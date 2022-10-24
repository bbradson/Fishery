// Based on
// https://github.com/CommunityToolkit/dotnet/blob/main/CommunityToolkit.HighPerformance/Extensions/SpanExtensions.cs
// with additional methods and support for more types

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file on https://github.com/CommunityToolkit/dotnet for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib;
public static class SpanExtensions
{
	/// <summary>
	/// Returns a reference to an element at a specified index within a given <see cref="Span{T}"/>, with no bounds checks.
	/// </summary>
	/// <typeparam name="T">The type of elements in the input <see cref="Span{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="Span{T}"/> instance.</param>
	/// <param name="i">The index of the element to retrieve within <paramref name="span"/>.</param>
	/// <returns>A reference to the element within <paramref name="span"/> at the index specified by <paramref name="i"/>.</returns>
	/// <remarks>This method doesn't do any bounds checks, therefore it is responsibility of the caller to ensure the <paramref name="i"/> parameter is valid.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T DangerousGetReferenceAt<T>(this Span<T> span, int i)
		=> ref Unsafe.Add(ref span.DangerousGetPinnableReference(), (nint)(uint)i);

	/// <summary>
	/// Returns a reference to an element at a specified index within a given <see cref="Span{T}"/>, with no bounds checks.
	/// </summary>
	/// <typeparam name="T">The type of elements in the input <see cref="Span{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="Span{T}"/> instance.</param>
	/// <param name="i">The index of the element to retrieve within <paramref name="span"/>.</param>
	/// <returns>A reference to the element within <paramref name="span"/> at the index specified by <paramref name="i"/>.</returns>
	/// <remarks>This method doesn't do any bounds checks, therefore it is responsibility of the caller to ensure the <paramref name="i"/> parameter is valid.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T DangerousGetReferenceAt<T>(this Span<T> span, nint i)
		=> ref Unsafe.Add(ref span.DangerousGetPinnableReference(), i);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Sort<T>(this Span<T> span, IComparer<T>? comparer)
		=> ArraySortHelper<T>.Default.Sort(span, comparer);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Sort<TKey, TValue>(this Span<TValue> values, Span<TKey> keys, IComparer<TKey>? comparer)
		=> ArraySortHelper<TKey, TValue>.Default.Sort(keys, values, comparer);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T First<T>(this Span<T> span)
		=> ref span[0];

	[return: MaybeNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T FirstOrDefault<T>(this Span<T> span)
		=> !span.IsEmpty ? span[0]
		: default;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe ref TSource First<TSource>(this Span<TSource> span, delegate*<ref TSource, bool> predicate)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (predicate(ref Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;
			if (predicate(ref Unsafe.Add(ref r0, offset + 1)))
				return ref Unsafe.Add(ref r0, offset + 1);
			if (predicate(ref Unsafe.Add(ref r0, offset + 2)))
				return ref Unsafe.Add(ref r0, offset + 2);
			if (predicate(ref Unsafe.Add(ref r0, offset + 3)))
				return ref Unsafe.Add(ref r0, offset + 3);

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (predicate(ref Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;

			length--;
			offset++;
		}

		return ref ThrowNotFoundException<TSource>();

	returnForOffset0:
		return ref Unsafe.Add(ref r0, offset);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref TSource First<TSource>(this Span<TSource> span, Predicate<TSource> predicate)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (predicate(Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;
			if (predicate(Unsafe.Add(ref r0, offset + 1)))
				return ref Unsafe.Add(ref r0, offset + 1);
			if (predicate(Unsafe.Add(ref r0, offset + 2)))
				return ref Unsafe.Add(ref r0, offset + 2);
			if (predicate(Unsafe.Add(ref r0, offset + 3)))
				return ref Unsafe.Add(ref r0, offset + 3);

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (predicate(Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;

			length--;
			offset++;
		}

		return ref ThrowNotFoundException<TSource>();

	returnForOffset0:
		return ref Unsafe.Add(ref r0, offset);
	}

	[DoesNotReturn]
	private static ref T ThrowNotFoundException<T>() => throw new InvalidOperationException();

	[return: MaybeNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TSource FirstOrDefault<TSource>(this Span<TSource> span, delegate*<ref TSource, bool> predicate)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (predicate(ref Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;
			if (predicate(ref Unsafe.Add(ref r0, offset + 1)))
				return Unsafe.Add(ref r0, offset + 1);
			if (predicate(ref Unsafe.Add(ref r0, offset + 2)))
				return Unsafe.Add(ref r0, offset + 2);
			if (predicate(ref Unsafe.Add(ref r0, offset + 3)))
				return Unsafe.Add(ref r0, offset + 3);

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (predicate(ref Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;

			length--;
			offset++;
		}

		return default;

	returnForOffset0:
		return Unsafe.Add(ref r0, offset);
	}

	[return: MaybeNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TSource FirstOrDefault<TSource>(this Span<TSource> span, Predicate<TSource> predicate)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (predicate(Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;
			if (predicate(Unsafe.Add(ref r0, offset + 1)))
				return Unsafe.Add(ref r0, offset + 1);
			if (predicate(Unsafe.Add(ref r0, offset + 2)))
				return Unsafe.Add(ref r0, offset + 2);
			if (predicate(Unsafe.Add(ref r0, offset + 3)))
				return Unsafe.Add(ref r0, offset + 3);

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (predicate(Unsafe.Add(ref r0, offset)))
				goto returnForOffset0;

			length--;
			offset++;
		}

		return default;

	returnForOffset0:
		return Unsafe.Add(ref r0, offset);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Any<TSource>(this Span<TSource> span)
		=> !span.IsEmpty;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool Any<TSource>(this Span<TSource> span, delegate*<ref TSource, bool> predicate)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (predicate(ref Unsafe.Add(ref r0, offset))
				|| predicate(ref Unsafe.Add(ref r0, offset + 1))
				|| predicate(ref Unsafe.Add(ref r0, offset + 2))
				|| predicate(ref Unsafe.Add(ref r0, offset + 3)))
			{
				return true;
			}

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (predicate(ref Unsafe.Add(ref r0, offset)))
				return true;

			length--;
			offset++;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Any<TSource>(this Span<TSource> span, Predicate<TSource> predicate)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (predicate(Unsafe.Add(ref r0, offset))
				|| predicate(Unsafe.Add(ref r0, offset + 1))
				|| predicate(Unsafe.Add(ref r0, offset + 2))
				|| predicate(Unsafe.Add(ref r0, offset + 3)))
			{
				return true;
			}

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (predicate(Unsafe.Add(ref r0, offset)))
				return true;

			length--;
			offset++;
		}

		return false;
	}

	/// <summary>
	/// Counts the number of occurrences of a given value into a target <see cref="Span{T}"/> instance.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="Span{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="Span{T}"/> instance to read.</param>
	/// <param name="value">The <typeparamref name="T"/> value to look for.</param>
	/// <returns>The number of occurrences of <paramref name="value"/> in <paramref name="span"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Count<T>(this Span<T> span, T value)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint result = 0;
		nint offset = 0;

		// Main loop with 4 unrolled iterations
		while (length >= 4)
		{
			result += Unsafe.Add(ref r0, offset).Equals<T>(value).ToByte();
			result += Unsafe.Add(ref r0, offset + 1).Equals<T>(value).ToByte();
			result += Unsafe.Add(ref r0, offset + 2).Equals<T>(value).ToByte();
			result += Unsafe.Add(ref r0, offset + 3).Equals<T>(value).ToByte();

			length -= 4;
			offset += 4;
		}

		// Iterate over the remaining values and count those that match
		while (length > 0)
		{
			result += Unsafe.Add(ref r0, offset).Equals<T>(value).ToByte();

			length--;
			offset++;
		}

		return (int)result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this Span<T> span, T value)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (Unsafe.Add(ref r0, offset).Equals<T>(value)
				|| Unsafe.Add(ref r0, offset + 1).Equals<T>(value)
				|| Unsafe.Add(ref r0, offset + 2).Equals<T>(value)
				|| Unsafe.Add(ref r0, offset + 3).Equals<T>(value))
			{
				return true;
			}

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (Unsafe.Add(ref r0, offset).Equals<T>(value))
				return true;

			length--;
			offset++;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int IndexOf<T>(this Span<T> span, T value)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (Unsafe.Add(ref r0, offset).Equals<T>(value))
				return (int)offset;
			if (Unsafe.Add(ref r0, offset + 1).Equals<T>(value))
				return (int)offset + 1;
			if (Unsafe.Add(ref r0, offset + 2).Equals<T>(value))
				return (int)offset + 2;
			if (Unsafe.Add(ref r0, offset + 3).Equals<T>(value))
				return (int)offset + 3;

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (Unsafe.Add(ref r0, offset).Equals<T>(value))
				return (int)offset;

			length--;
			offset++;
		}

		return -1;
	}

	/// <summary>
	/// Enumerates the items in the input <see cref="Span{T}"/> instance, as pairs of reference/index values.
	/// This extension should be used directly within a <see langword="foreach"/> loop:
	/// <code>
	/// Span&lt;int&gt; numbers = new[] { 1, 2, 3, 4, 5, 6, 7 };
	///
	/// foreach (var item in numbers.Enumerate())
	/// {
	///     // Access the index and value of each item here...
	///     int index = item.Index;
	///     ref int value = ref item.Value;
	/// }
	/// </code>
	/// The compiler will take care of properly setting up the <see langword="foreach"/> loop with the type returned from this method.
	/// </summary>
	/// <typeparam name="T">The type of items to enumerate.</typeparam>
	/// <param name="span">The source <see cref="Span{T}"/> to enumerate.</param>
	/// <returns>A wrapper type that will handle the reference/index enumeration for <paramref name="span"/>.</returns>
	/// <remarks>The returned <see cref="SpanEnumerable{T}"/> value shouldn't be used directly: use this extension in a <see langword="foreach"/> loop.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static SpanEnumerable<T> Enumerate<T>(this Span<T> span) => new(span);
}

public static class SpanReferenceExtensions
{
	/// <summary>
	/// Gets the index of an element of a given <see cref="Span{T}"/> from its reference.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="Span{T}"/>.</typeparam>
	/// <param name="span">The input <see cref="Span{T}"/> to calculate the index for.</param>
	/// <param name="reference">The reference to the target item to get the index for.</param>
	/// <returns>The index of <paramref name="reference"/> within <paramref name="span"/>, or <c>-1</c>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int IndexOf<T>(this Span<T> span, ref T reference)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var byteOffset = Unsafe.ByteOffset(ref r0, ref reference);

		var elementOffset = byteOffset / (nint)(uint)Unsafe.SizeOf<T>();

		return (nuint)elementOffset >= (uint)span.Length ? -1
			: (int)elementOffset;
	}

	/// <summary>
	/// Determines whether a reference points to an element within the <see cref="Span{T}"/>.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="Span{T}"/>.</typeparam>
	/// <param name="span">The input <see cref="Span{T}"/> to locate the reference in.</param>
	/// <param name="reference">The reference to the target item to locate.</param>
	/// <returns>true if the reference points towards an element of <see cref="Span{T}"/>; otherwise, false.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this Span<T> span, ref T reference)
		=> span.IndexOf(ref reference) >= 0;
}

public static class SpanStructExtensions
{
	/// <summary>
	/// Counts the number of occurrences of a given value into a target <see cref="Span{T}"/> instance.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="Span{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="Span{T}"/> instance to read.</param>
	/// <param name="value">The <typeparamref name="T"/> value to look for.</param>
	/// <returns>The number of occurrences of <paramref name="value"/> in <paramref name="span"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Count<T>(this Span<T> span, in T value)
		where T : struct
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint result = 0;
		nint offset = 0;

		// Main loop with 4 unrolled iterations
		while (length >= 4)
		{
			result += Unsafe.Add(ref r0, offset).Equals(ref Unsafe.AsRef(in value)).ToByte();
			result += Unsafe.Add(ref r0, offset + 1).Equals(ref Unsafe.AsRef(in value)).ToByte();
			result += Unsafe.Add(ref r0, offset + 2).Equals(ref Unsafe.AsRef(in value)).ToByte();
			result += Unsafe.Add(ref r0, offset + 3).Equals(ref Unsafe.AsRef(in value)).ToByte();

			length -= 4;
			offset += 4;
		}

		// Iterate over the remaining values and count those that match
		while (length > 0)
		{
			result += Unsafe.Add(ref r0, offset).Equals(ref Unsafe.AsRef(in value)).ToByte();

			length--;
			offset++;
		}

		return (int)result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this Span<T> span, in T value)
		where T : struct
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (Unsafe.Add(ref r0, offset).Equals(ref Unsafe.AsRef(in value))
				|| Unsafe.Add(ref r0, offset + 1).Equals(ref Unsafe.AsRef(in value))
				|| Unsafe.Add(ref r0, offset + 2).Equals(ref Unsafe.AsRef(in value))
				|| Unsafe.Add(ref r0, offset + 3).Equals(ref Unsafe.AsRef(in value)))
			{
				return true;
			}

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (Unsafe.Add(ref r0, offset).Equals(ref Unsafe.AsRef(in value)))
				return true;

			length--;
			offset++;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int IndexOf<T>(this Span<T> span, in T value)
		where T : struct
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var length = (nint)(uint)span.Length;
		nint offset = 0;

		while (length >= 4)
		{
			if (Unsafe.Add(ref r0, offset).Equals(ref Unsafe.AsRef(in value)))
				return (int)offset;
			if (Unsafe.Add(ref r0, offset + 1).Equals(ref Unsafe.AsRef(in value)))
				return (int)offset + 1;
			if (Unsafe.Add(ref r0, offset + 2).Equals(ref Unsafe.AsRef(in value)))
				return (int)offset + 2;
			if (Unsafe.Add(ref r0, offset + 3).Equals(ref Unsafe.AsRef(in value)))
				return (int)offset + 3;

			length -= 4;
			offset += 4;
		}

		while (length > 0)
		{
			if (Unsafe.Add(ref r0, offset).Equals(ref Unsafe.AsRef(in value)))
				return (int)offset;

			length--;
			offset++;
		}

		return -1;
	}
}