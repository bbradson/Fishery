// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FisheryLib;
public static class CollectionExtensions
{
	public static bool TryGetItem<T>(this List<T> list, int index, [NotNullWhen(true)] out T? item)
	{
		if (list.Count > index)
		{
			item = list[index]!;
			return true;
		}

		item = default;
		return false;
	}

	/// <summary>
	/// Taken from Span<>.this[] and slower than List<>.this[] due to the ArrayAdjustment
	/// </summary>
	internal static T GetItemUnchecked<T>(this List<T> list, int index)
		=> Unsafe.Add(ref Unsafe.AddByteOffset(ref Unsafe.As<Pinnable<T>>(list._items).Data, PerTypeValues<T>.ArrayAdjustment), index)!;

	[StructLayout(LayoutKind.Sequential)]
	private sealed class Pinnable<T>
	{
		public T? Data;
	}
	private static class PerTypeValues<T>
	{
		public static readonly IntPtr ArrayAdjustment = MeasureArrayAdjustment();
		private static IntPtr MeasureArrayAdjustment()
		{
			var array = new T[1];
			return Unsafe.ByteOffset(ref Unsafe.As<Pinnable<T>>(array).Data!, ref array[0]);
		}
	}

	public static bool TryGetItem<T>(this T[] array, int index, out T? item)
	{
		if (array.Length > index)
		{
			item = array[index];
			return true;
		}

		item = default;
		return false;
	}

	public static T[] ToArray<T>(this IEnumerable<T> enumerable, int length)
	{
		var array = new T[length];

		if (enumerable is IList<T> ilist)
			array.Fill(ilist);
		else
			array.FillUsingEnumerable(enumerable);

		return array;
	}

	public static Telement[] ToArray<Tenumerable, Telement>(this Tenumerable enumerable, Telement[] destination)
		where Tenumerable : IList<Telement>
	{
		Guard.IsNotNull(destination);

		destination.Fill(enumerable);
		return destination;
	}

	public static T[] ToArray<T>(this IEnumerable<T> enumerable, T[] destination)
	{
		Guard.IsNotNull(destination);

		if (enumerable is IList<T> ilist)
			destination.Fill(ilist);
		else
			destination.FillUsingEnumerable(enumerable);

		return destination;
	}

	private static void FillUsingEnumerable<T>(this T[] array, IEnumerable<T> enumerable, int startIndex = 0, int count = -1)
	{
		var i = 0u;
		foreach (var item in enumerable)
		{
			if (i >= (uint)count || startIndex >= array.Length)
				break;

			array[startIndex++] = item;
			i++;
		}
	}

	public static void Fill<Telement, Tcollection>(this Telement[] array, Tcollection collection, int startIndex = 0, int count = -1)
		where Tcollection : IList<Telement>
	{
		Guard.IsNotNull(collection);

		if (count <= 0)
			count = collection.Count;

		if (count <= array.Length - startIndex)
		{
			collection.CopyTo(array, startIndex);
			return;
		}

		for (var listIndex = 0; listIndex < count && startIndex < array.Length; startIndex++, listIndex++)
			array[startIndex] = collection[listIndex];
	}

	public static void Fill<T>(this T[] array, IEnumerable<T> enumerable, int startIndex = 0, int count = -1)
	{
		Guard.IsNotNull(enumerable);

		if (enumerable is IList<T> ilist)
			array.Fill(ilist, startIndex, count);
		else
			array.FillUsingEnumerable(enumerable, startIndex, count);
	}

	public static ref TValue GetReference<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(key) & 0x7FFFFFFF; // HashCode.Get can handle null, & 0x7FFFFFFF removes any sign

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
					return ref entry.value;

				bucket = entry.next;
			}
		}
		return ref ThrowKeyNotFoundException<TKey, TValue>(key);
	}

	/// <summary>
	/// Returns a reference to the value field of an entry if the key exists and an Unsafe.NullRef{TValue} if not.
	/// Must be checked with Unsafe.IsNullRef before assigning to a regular var or field without ref.
	/// </summary>
	/// <param name="key">The key</param>
	/// <returns>ref TValue or Unsafe.NullRef{TValue}</returns>
	public static ref TValue TryGetReferenceUnsafe<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(key) & 0x7FFFFFFF;

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
					return ref entry.value;

				bucket = entry.next;
			}
		}
		return ref Unsafe.NullRef<TValue>();
	}

	public static ref TValue TryGetReferenceUnsafe<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, ref TKey key)
		where TKey : struct
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(ref key) & 0x7FFFFFFF;

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals(ref key))
					return ref entry.value;

				bucket = entry.next;
			}
		}
		return ref Unsafe.NullRef<TValue>();
	}

	public static TValue? TryGetValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, ref TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(ref key) & 0x7FFFFFFF;

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && Equality.EqualsByRef(ref entry.key, ref key))
					return entry.value;

				bucket = entry.next;
			}
		}
		return default;
	}

	[return: MaybeNull]
	public static TValue TryGetValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(key) & 0x7FFFFFFF;

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
					return entry.value;

				bucket = entry.next;
			}
		}
		return default;
	}

	[DoesNotReturn]
	private static ref V ThrowKeyNotFoundException<T, V>(T key)
	{
		Guard.IsNotNull(key);
		ThrowKeyNotFoundException(key);
		return ref Unsafe.NullRef<V>();
	}

	[DoesNotReturn]
	private static void ThrowKeyNotFoundException(object key)
		=> throw new KeyNotFoundException(
			string.Format("The given key '{0}' was not present in the dictionary.", key));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref int Version<T>(this List<T> list) => ref list._version;

	/// <summary>
	/// Returns a list's internal _items array. Keep in mind list.Version() needs to change every time items are modified.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T[] ItemsUnchecked<T>(this List<T> list) => list._items;

	/// <summary>
	/// Returns a span pointing to a list's internal _items array. Keep in mind list.Version() needs to change every time items are modified.
	/// </summary>
	public static Span<T> AsSpanUnchecked<T>(this List<T> list) => new(list._items, 0, list.Count);

	public static ReadOnlySpan<T> AsReadOnlySpan<T>(this List<T> list) => new(list._items, 0, list.Count);

	public static void ReplaceContentsWith<T, V>(this List<T> list, V collection) where V : ICollection<T>
	{
		if (ReferenceEquals(list, collection))
			return;

		if (collection is null
			|| (collection.Count is var collectionCount
			&& collectionCount == 0))
		{
			list.Clear();
			return;
		}

		if (collectionCount <= list.Count)
		{
			if (collectionCount < list.Count)
				list.RemoveRange(collectionCount, list.Count - collectionCount);
			collection.CopyTo(list._items, 0);
		}
		else
		{
			list._items = new T[collectionCount];
			collection.CopyTo(list._items, 0);
			list._size = collectionCount;
		}
		list._version++;
	}

	public static IList TryNew(IEnumerable enumerable, Type type) => GetListExtensionMethods(type).New(enumerable);

	public static void TryAddRange<T>(this IList list, T collection, Type type) where T : ICollection => GetListExtensionMethods(type).AddRange(list, collection);

	public static Array TryToArray(this IList list, Type type) => GetListExtensionMethods(type).ToArray(list);

	public static GenericBase GetListExtensionMethods(Type type)
		=> ListExtensionCache.TryGetValue(type)
		?? CreateListExtensionMethods(type);

	private static GenericBase CreateListExtensionMethods(Type type)
	{
		var methods = (GenericBase)Activator.CreateInstance(typeof(Generic<>).MakeGenericType(type));
		ListExtensionCache[type] = methods;
		return methods;
	}

	private static Dictionary<Type, GenericBase> ListExtensionCache => _listExtensionCache ??= new();

	[ThreadStatic]
	private static Dictionary<Type, GenericBase>? _listExtensionCache;

	public abstract class GenericBase
	{
		public abstract void AddRange(IList list, ICollection collection);
		public abstract Array ToArray(IList list);
		public abstract IList New(IEnumerable enumerable);
	}

	public class Generic<T> : GenericBase
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override void AddRange(IList list, ICollection collection) => ((List<T>)list).AddRange((IEnumerable<T>)collection);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override Array ToArray(IList list) => ((List<T>)list).ToArray();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override IList New(IEnumerable enumerable) => new List<T>((IEnumerable<T>)enumerable);
	}
}