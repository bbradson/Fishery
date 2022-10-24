// Based on
// https://github.com/CommunityToolkit/dotnet/blob/main/CommunityToolkit.HighPerformance/Extensions/ReadOnlySpanExtensions.cs
// with additional methods and support for more types

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file on https://github.com/CommunityToolkit/dotnet for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib;
public static class ReadOnlySpanExtensions
{
	/// <summary>
	/// Returns a reference to an element at a specified index within a given <see cref="ReadOnlySpan{T}"/>, with no bounds checks.
	/// </summary>
	/// <typeparam name="T">The type of elements in the input <see cref="ReadOnlySpan{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="ReadOnlySpan{T}"/> instance.</param>
	/// <param name="i">The index of the element to retrieve within <paramref name="span"/>.</param>
	/// <returns>A reference to the element within <paramref name="span"/> at the index specified by <paramref name="i"/>.</returns>
	/// <remarks>This method doesn't do any bounds checks, therefore it is responsibility of the caller to ensure the <paramref name="i"/> parameter is valid.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T DangerousGetReferenceAt<T>(this ReadOnlySpan<T> span, int i)
		// Here we assume the input index will never be negative, so we do a (nint)(uint) cast
		// to force the JIT to skip the sign extension when going from int to native int.
		// On .NET Core 3.1, if we only use Unsafe.Add(ref r0, i), we get the following:
		// =============================
		// L0000: mov rax, [rcx]
		// L0003: movsxd rdx, edx
		// L0006: lea rax, [rax+rdx*4]
		// L000a: ret
		// =============================
		// Note the movsxd (move with sign extension) to expand the index passed in edx to
		// the whole rdx register. This is unnecessary and more expensive than just a mov,
		// which when done to a large register size automatically zeroes the upper bits.
		// With the (nint)(uint) cast, we get the following codegen instead:
		// =============================
		// L0000: mov rax, [rcx]
		// L0003: mov edx, edx
		// L0005: lea rax, [rax+rdx*4]
		// L0009: ret
		// =============================
		// Here we can see how the index is extended to a native integer with just a mov,
		// which effectively only zeroes the upper bits of the same register used as source.
		// These three casts are a bit verbose, but they do the trick on both 32 bit and 64
		// bit architectures, producing optimal code in both cases (they are either completely
		// elided on 32 bit systems, or result in the correct register expansion when on 64 bit).
		// We first do an unchecked conversion to uint (which is just a reinterpret-cast). We
		// then cast to nint, so that we can obtain an IntPtr value without the range check (since
		// uint could be out of range there if the original index was negative). The final result
		// is a clean mov as shown above. This will eventually be natively supported by the JIT
		// compiler (see https://github.com/dotnet/runtime/issues/38794), but doing this here
		// still ensures the optimal codegen even on existing runtimes (eg. .NET Core 2.1 and 3.1).
		=> ref Unsafe.Add(ref span.DangerousGetPinnableReference(), (nint)(uint)i);

	/// <summary>
	/// Returns a reference to an element at a specified index within a given <see cref="ReadOnlySpan{T}"/>, with no bounds checks.
	/// </summary>
	/// <typeparam name="T">The type of elements in the input <see cref="ReadOnlySpan{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="ReadOnlySpan{T}"/> instance.</param>
	/// <param name="i">The index of the element to retrieve within <paramref name="span"/>.</param>
	/// <returns>A reference to the element within <paramref name="span"/> at the index specified by <paramref name="i"/>.</returns>
	/// <remarks>This method doesn't do any bounds checks, therefore it is responsibility of the caller to ensure the <paramref name="i"/> parameter is valid.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T DangerousGetReferenceAt<T>(this ReadOnlySpan<T> span, nint i)
		=> ref Unsafe.Add(ref span.DangerousGetPinnableReference(), i);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T First<T>(this ReadOnlySpan<T> span)
		=> span[0];

	[return: MaybeNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T FirstOrDefault<T>(this ReadOnlySpan<T> span)
		=> !span.IsEmpty ? span[0]
		: default;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TSource First<TSource>(this ReadOnlySpan<TSource> span, delegate*<TSource, bool> predicate)
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

		return ThrowNotFoundException<TSource>();

	returnForOffset0:
		return Unsafe.Add(ref r0, offset);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TSource First<TSource>(this ReadOnlySpan<TSource> span, Predicate<TSource> predicate)
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

		return ThrowNotFoundException<TSource>();

	returnForOffset0:
		return Unsafe.Add(ref r0, offset);
	}

	[DoesNotReturn]
	private static ref T ThrowNotFoundException<T>() => throw new InvalidOperationException();

	[return: MaybeNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TSource FirstOrDefault<TSource>(this ReadOnlySpan<TSource> span, delegate*<TSource, bool> predicate)
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

	[return: MaybeNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TSource FirstOrDefault<TSource>(this ReadOnlySpan<TSource> span, Predicate<TSource> predicate)
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
	public static bool Any<TSource>(this ReadOnlySpan<TSource> span)
		=> !span.IsEmpty;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool Any<TSource>(this ReadOnlySpan<TSource> span, delegate*<TSource, bool> predicate)
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Any<TSource>(this ReadOnlySpan<TSource> span, Predicate<TSource> predicate)
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
	/// Counts the number of occurrences of a given value into a target <see cref="ReadOnlySpan{T}"/> instance.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="ReadOnlySpan{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="ReadOnlySpan{T}"/> instance to read.</param>
	/// <param name="value">The <typeparamref name="T"/> value to look for.</param>
	/// <returns>The number of occurrences of <paramref name="value"/> in <paramref name="span"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Count<T>(this ReadOnlySpan<T> span, T value)
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
	public static bool Contains<T>(this ReadOnlySpan<T> span, T value)
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
	public static int IndexOf<T>(this ReadOnlySpan<T> span, T value)
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
	/// Enumerates the items in the input <see cref="ReadOnlySpan{T}"/> instance, as pairs of reference/index values.
	/// This extension should be used directly within a <see langword="foreach"/> loop:
	/// <code>
	/// ReadOnlySpan&lt;int&gt; numbers = new[] { 1, 2, 3, 4, 5, 6, 7 };
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
	/// <param name="span">The source <see cref="ReadOnlySpan{T}"/> to enumerate.</param>
	/// <returns>A wrapper type that will handle the reference/index enumeration for <paramref name="span"/>.</returns>
	/// <remarks>The returned <see cref="ReadOnlySpanEnumerable{T}"/> value shouldn't be used directly: use this extension in a <see langword="foreach"/> loop.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpanEnumerable<T> Enumerate<T>(this ReadOnlySpan<T> span) => new(span);
}

public static class ReadOnlySpanReferenceExtensions
{
	/// <summary>
	/// Gets the index of an element of a given <see cref="ReadOnlySpan{T}"/> from its reference.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="ReadOnlySpan{T}"/>.</typeparam>
	/// <param name="span">The input <see cref="ReadOnlySpan{T}"/> to calculate the index for.</param>
	/// <param name="reference">The reference to the target item to get the index for.</param>
	/// <returns>The index of <paramref name="reference"/> within <paramref name="span"/>, or <c>-1</c>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int IndexOf<T>(this ReadOnlySpan<T> span, ref T reference)
	{
		ref var r0 = ref span.DangerousGetPinnableReference();
		var byteOffset = Unsafe.ByteOffset(ref r0, ref reference);

		var elementOffset = byteOffset / (nint)(uint)Unsafe.SizeOf<T>();

		return (nuint)elementOffset >= (uint)span.Length ? -1
			: (int)elementOffset;
	}

	/// <summary>
	/// Determines whether a reference points to an element within the <see cref="ReadOnlySpan{T}"/>.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="ReadOnlySpan{T}"/>.</typeparam>
	/// <param name="span">The input <see cref="ReadOnlySpan{T}"/> to locate the reference in.</param>
	/// <param name="reference">The reference to the target item to locate.</param>
	/// <returns>true if the reference points towards an element of <see cref="ReadOnlySpan{T}"/>; otherwise, false.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Contains<T>(this ReadOnlySpan<T> span, ref T reference)
		=> span.IndexOf(ref reference) >= 0;
}

public static class ReadOnlySpanStructExtensions
{
	/// <summary>
	/// Counts the number of occurrences of a given value into a target <see cref="ReadOnlySpan{T}"/> instance.
	/// </summary>
	/// <typeparam name="T">The type of items in the input <see cref="ReadOnlySpan{T}"/> instance.</typeparam>
	/// <param name="span">The input <see cref="ReadOnlySpan{T}"/> instance to read.</param>
	/// <param name="value">The <typeparamref name="T"/> value to look for.</param>
	/// <returns>The number of occurrences of <paramref name="value"/> in <paramref name="span"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Count<T>(this ReadOnlySpan<T> span, in T value)
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
	public static bool Contains<T>(this ReadOnlySpan<T> span, in T value)
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
	public static int IndexOf<T>(this ReadOnlySpan<T> span, in T value)
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