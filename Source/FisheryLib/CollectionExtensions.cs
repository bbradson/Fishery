// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FisheryLib.Collections;

namespace FisheryLib;

[PublicAPI]
public static class CollectionExtensions
{
	public static bool TryGetItem<T>(this List<T> list, int index, [NotNullWhen(true)] out T? item)
	{
		if (list._size > index)
		{
			item = list._items[index]!;
			return true;
		}

		item = default;
		return false;
	}

	/// <summary>
	/// Taken from Span.this[] and slower than List.this[] due to the ArrayAdjustment
	/// </summary>
	internal static T GetItemUnchecked<T>(this List<T> list, int index)
		=> Unsafe.Add(
			ref Unsafe.AddByteOffset(ref Unsafe.As<Pinnable<T>>(list._items).Data, PerTypeValues<T>.ArrayAdjustment),
			index)!;

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

		if (enumerable is IList<T> iList)
			array.Fill(iList);
		else
			array.FillUsingEnumerable(enumerable);

		return array;
	}

	public static TElement[] ToArray<TEnumerable, TElement>(this TEnumerable enumerable, TElement[] destination)
		where TEnumerable : IList<TElement>
	{
		Guard.IsNotNull(destination);

		destination.Fill(enumerable);
		return destination;
	}

	public static T[] ToArray<T>(this IEnumerable<T> enumerable, T[] destination)
	{
		Guard.IsNotNull(destination);

		if (enumerable is IList<T> iList)
			destination.Fill(iList);
		else
			destination.FillUsingEnumerable(enumerable);

		return destination;
	}

	private static void FillUsingEnumerable<T>(this T[] array, IEnumerable<T> enumerable, int startIndex = 0,
		int count = -1)
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

	public static void Fill<TElement, TCollection>(this TElement[] array, TCollection collection, int startIndex = 0,
		int count = -1)
		where TCollection : IList<TElement>
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

		if (enumerable is IList<T> iList)
			array.Fill(iList, startIndex, count);
		else
			array.FillUsingEnumerable(enumerable, startIndex, count);
	}

	public static ref TValue GetReference<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(key) & int.MaxValue;
			// HashCode.Get can handle null, & int.MaxValue removes any sign

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

	public static ref TValue GetReference<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, ref TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(ref key) & int.MaxValue;

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && Equality.EqualsByRef(ref entry.key, ref key))
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
	/// <param name="dictionary">The instance</param>
	/// <param name="key">The key</param>
	/// <returns>ref TValue or Unsafe.NullRef{TValue}</returns>
	public static ref TValue TryGetReferenceUnsafe<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(key) & int.MaxValue;

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
	{
		if (dictionary.buckets != null)
		{
			var keyCode = HashCode.Get(ref key) & int.MaxValue;

			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && Equality.EqualsByRef(ref entry.key, ref key))
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
			var keyCode = HashCode.Get(ref key) & int.MaxValue;

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
			var keyCode = HashCode.Get(key) & int.MaxValue;

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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref TValue GetOrAddReference<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
		where TValue : new()
	{
		var keyCode = HashCode.Get(key) & int.MaxValue;

	StartOfLookup:
		if (dictionary.buckets != null)
		{
			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
					return ref entry.value;

				bucket = entry.next;
			}
		}

		dictionary.Add(key, Reflection.New<TValue>());
		goto StartOfLookup;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref TValue GetOrAddReference<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, ref TKey key)
		where TValue : new()
	{
		var keyCode = HashCode.Get(ref key) & int.MaxValue;

	StartOfLookup:
		if (dictionary.buckets != null)
		{
			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && Equality.EqualsByRef(ref entry.key, ref key))
					return ref entry.value;

				bucket = entry.next;
			}
		}

		dictionary.Add(key, Reflection.New<TValue>());
		goto StartOfLookup;
	}

	// public static bool TryGetOrAddReference<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key,
	// 	ref TValue reference)
	// {
	// 	var keyCode = HashCode.Get(ref key) & int.MaxValue;
	// 	var result = true;
	//
	// StartOfLookup:
	// 	if (dictionary.buckets != null)
	// 	{
	// 		var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
	// 		while (bucket >= 0)
	// 		{
	// 			ref var entry = ref dictionary.entries[bucket];
	// 			if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
	// 			{
	// 				reference.Value = ref entry.value;
	// 				return result;
	// 			}
	//
	// 			bucket = entry.next;
	// 		}
	// 	}
	//
	// 	result = false;
	// 	dictionary.Add(key, Reflection.New<TValue>());
	// 	goto StartOfLookup;
	// }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
		where TValue : new()
	{
		var keyCode = HashCode.Get(key) & int.MaxValue;

	StartOfLookup:
		if (dictionary.buckets != null)
		{
			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
					return entry.value;

				bucket = entry.next;
			}
		}

		dictionary.Add(key, Reflection.New<TValue>());
		goto StartOfLookup;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, ref TKey key)
		where TValue : new()
	{
		var keyCode = HashCode.Get(ref key) & int.MaxValue;

	StartOfLookup:
		if (dictionary.buckets != null)
		{
			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && Equality.EqualsByRef(ref entry.key, ref key))
					return entry.value;

				bucket = entry.next;
			}
		}

		dictionary.Add(key, Reflection.New<TValue>());
		goto StartOfLookup;
	}

	public static bool TryGetOrAddValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key,
		out TValue value)
	{
		var keyCode = HashCode.Get(ref key) & int.MaxValue;
		var result = true;

	StartOfLookup:
		if (dictionary.buckets != null)
		{
			var bucket = dictionary.buckets[keyCode % dictionary.buckets.Length];
			while (bucket >= 0)
			{
				ref var entry = ref dictionary.entries[bucket];
				if (entry.hashCode == keyCode && entry.key.Equals<TKey>(key))
				{
					value = entry.value;
					return result;
				}

				bucket = entry.next;
			}
		}

		result = false;
		dictionary.Add(key, Reflection.New<TValue>());
		goto StartOfLookup;
	}

	public static int RemoveWhere<TKey, TValue>(this Dictionary<TKey, TValue> dictionary,
		Predicate<KeyValuePair<TKey, TValue>> predicate)
	{
		Guard.IsNotNull(dictionary);
		Guard.IsNotNull(predicate);

		var entries = dictionary.entries;
		if (entries is null)
			return 0;

		var removedCount = 0;
		var entriesCount = entries.Length;

		for (var i = 0; i < entriesCount; i++)
		{
			ref var entry = ref entries[i];
			if (entry.hashCode < 0) // valid entries are stored with & int.MaxValue on hashCode
				continue;

			// predicate might modify collection, turning a ref invalid
			var keyValuePair = new KeyValuePair<TKey, TValue>(entry.key, entry.value);

			if (!predicate(keyValuePair))
				continue;

			if (dictionary.Remove(keyValuePair.Key))
				removedCount++;
		}

		return removedCount;
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
		=> throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref int Version<T>(this List<T> list) => ref list._version;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AddRangeFast<T>(this List<T> list, List<T> range)
	{
		if (range._size < 1)
			return;

		list.EnsureCapacity(list._size + range._size);

		range.UnsafeCopyTo(list, list._size);

		list._size += range._size;
		list._version++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void UnsafeCopyTo<T>(this List<T> source, List<T> destination, int destinationStartIndex)
		=> UnsafeBlockCopy(ref source._items[0], ref destination._items[destinationStartIndex], source._size);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void UnsafeCopyTo<T>(this T[] source, T[] destination, int destinationStartIndex)
		=> UnsafeBlockCopy(ref source[0], ref destination[destinationStartIndex], source.Length);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void UnsafeBlockCopy<T>(ref T source, ref T destination, int count)
		=> Unsafe.CopyBlock(ref Unsafe.As<T, byte>(ref destination),
			ref Unsafe.As<T, byte>(ref source),
			(uint)((nint)sizeof(T) * count));

	/// <summary>
	/// Returns a list's internal _items array. Keep in mind list.Version() needs to change every time items are
	/// modified.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T[] ItemsUnchecked<T>(this List<T> list) => list._items;

	/// <summary>
	/// Returns a span pointing to a list's internal _items array. Keep in mind list.Version() needs to change every
	/// time items are modified.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<T> AsSpanUnchecked<T>(this List<T> list) => new(list._items, 0, list._size);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<T> AsReadOnlySpan<T>(this List<T> list) => new(list._items, 0, list._size);

	public static void ReplaceContentsWith<T>(this List<T> list, [AllowNull] List<T> collection)
	{
		if (ReferenceEquals(list, collection))
			return;

		if (collection is null
			|| (collection._size is var collectionCount
				&& collectionCount == 0))
		{
			list.Clear();
			return;
		}

		if (collectionCount <= list._size)
		{
			if (collectionCount < list._size)
				list.RemoveRange(collectionCount, list._size - collectionCount);

			collection.UnsafeCopyTo(list, 0);
		}
		else
		{
			list._items = new T[collectionCount];
			collection.UnsafeCopyTo(list, 0);
			list._size = collectionCount;
		}

		list._version++;
	}

	public static void ReplaceContentsWith<TThis, TOther>(this List<TThis> list, [AllowNull] TOther collection)
		where TOther : ICollection<TThis>
	{
		if (typeof(TOther) == typeof(List<TThis>))
			list.ReplaceContentsWith(Unsafe.As<List<TThis>>(collection));

		if (ReferenceEquals(list, collection))
			return;

		if (collection is null
			|| (collection.Count is var collectionCount
				&& collectionCount == 0))
		{
			list.Clear();
			return;
		}

		if (collectionCount <= list._size)
		{
			if (collectionCount < list._size)
				list.RemoveRange(collectionCount, list._size - collectionCount);
			collection.CopyTo(list._items, 0);
		}
		else
		{
			list._items = new TThis[collectionCount];
			collection.CopyTo(list._items, 0);
			list._size = collectionCount;
		}

		list._version++;
	}

	public static FishTable<TKey, TSource> ToFishTable<TSource, TKey>(this IEnumerable<TSource> source,
		Func<TSource, TKey> keySelector)
		=> source.ToFishTable(keySelector, static item => item);

	public static FishTable<TKey, TValue> ToFishTable<TSource, TKey, TValue>(this IEnumerable<TSource> source,
		Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector)
	{
		Guard.IsNotNull(source);
		Guard.IsNotNull(keySelector);
		Guard.IsNotNull(valueSelector);

		var capacity = 0;
		if (source is ICollection iCollection)
			capacity = (int)(iCollection.Count * 1.5f);

		var fishTable = new FishTable<TKey, TValue>(capacity);
		fishTable.AddRange(source, keySelector, valueSelector);
		return fishTable;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clear<T>(this T[] array) => Array.Clear(array, 0, array.Length);

	// public static List<T> Copy<T>(this List<T> source)
	// {
	// 	Guard.IsNotNull(source);
	// 	
	// 	var destination = new List<T>(source._size);
	//
	// 	if (source._size == 0)
	// 		return destination;
	// 	
	// 	// Unsafe.CopyBlock(ref Unsafe.As<T, byte>(ref destination._items[0]),
	// 	// 	ref Unsafe.As<T, byte>(ref source._items[0]), (uint)sizeof(T) * (uint)source._size);
	// 	
	// 	FisheryLib.CollectionExtensions.UnsafeBlockCopy(ref source._items[0],	// does a type check that causes issues
	// 		ref destination._items[0], source._size);							// when called from transpiled methods
	// 	
	// 	destination._size = source._size;
	// 	
	// 	return destination;
	// }

	public static unsafe List<T> Copy<T>(this List<T> source)
	{
		Guard.IsNotNull(source);

		var destination = new List<T>(source._size);

		if (source._size == 0)
			return destination;

		fixed (void* destinationPointer = &destination._items[0], sourcePointer = &source._items[0])
			Unsafe.CopyBlock(destinationPointer, sourcePointer, (uint)(source._size * sizeof(T)));

		destination._size = source._size;

		return destination;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AddRangeFast<T>(this List<T> list, T[] range)
	{
		Guard.IsNotNull(list);
		Guard.IsNotNull(range);

		if (range.Length < 1)
			return;

		list.EnsureCapacity(list._size + range.Length);
		range.UnsafeCopyTo(list._items, list._size);

		list._size += range.Length;
		list._version++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Add(this List<char> list, string value)
	{
		Guard.IsNotNull(list);
		Guard.IsNotNull(value);

		if (value.Length < 1)
			return;

		list.EnsureCapacity(list._size + value.Length);
		UnsafeBlockCopy(ref value.m_firstChar,
			ref list._items[list._size], value.Length);

		list._size += value.Length;
		list._version++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StringBuilder Append(this StringBuilder builder, List<char> value)
		=> builder.Append(value._items, 0, value._size);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StringBuilder Append<T>(this StringBuilder builder, T? value)
		// ReSharper disable once RedundantToStringCallForValueType
		// ReSharper disable once CompareNonConstrainedGenericWithNull
		// ReSharper disable once HeapView.PossibleBoxingAllocation
		=> value == null ? builder : builder.Append(value.ToString());

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T GetReference<T>(this List<T> list, int index)
	{
		Guard.IsInRange(index, 0, list.Count);

		list._version++;
		return ref list._items[index];
	}

	[Obsolete("Renamed to GetReferenceUnsafe")]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T GetReferenceUnverifiable<T>(this List<T> list, int index) => ref list.GetReferenceUnsafe(index);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T GetReferenceUnsafe<T>(this List<T> list, int index) => ref list._items[index];

	public static IList TryNew(IEnumerable enumerable, Type type) => GetListExtensionMethods(type).New(enumerable);

	public static void TryAddRange<T>(this IList list, T collection, Type type) where T : ICollection
		=> GetListExtensionMethods(type).AddRange(list, collection);

	public static Array TryToArray(this IList list, Type type) => GetListExtensionMethods(type).ToArray(list);

	public static void RemoveAtFastUnordered<T>(this List<T> list, int index)
	{
		ref var lastBucket = ref list._items[list._size - 1];
		list[index] = lastBucket;
		lastBucket = default!;
		list._size--;
	}

	public static bool RemoveFastUnordered<T>(this List<T> list, T item)
	{
		var index = list.IndexOf(item);
		if (index < 0)
			return false;

		list.RemoveAtFastUnordered(index);
		return true;
	}

	public static void InsertFastUnordered<T>(this List<T> list, int index, T item)
	{
		if ((uint)index > (uint)list._size)
			ThrowHelper.ThrowArgumentOutOfRangeException();

		if (list._size == list._items.Length)
			list.EnsureCapacity(list._size + 1);

		ref var targetBucket = ref list._items[index];

		list._items[list._size] = targetBucket;
		targetBucket = item;
		list._size++;
		list._version++;
	}

	public static unsafe uint GetSizeEstimate<TKey, TValue>(this Dictionary<TKey, TValue> dictionary)
		=> dictionary.GetType().ComputeManagedObjectSizeEstimate()
			+ ((uint)sizeof(Dictionary<TKey, TValue>.Entry) * (uint)(dictionary.entries?.Length ?? 0))
			+ (sizeof(int) * (uint)(dictionary.buckets?.Length ?? 0));

	internal static unsafe uint ComputeManagedObjectSizeEstimate(this Type type)
	{
		var objectHeader = (uint)sizeof(void*) * 2u;
		var cachedSize = CachedManagedObjectSizes.GetOrAdd(type.TypeHandle.Value);
		// can't use GetOrAddReference here due to recursion

		if (cachedSize.Size == uint.MaxValue)
			UpdateCachedManagedObjectSize(type, ref cachedSize);

		return objectHeader + cachedSize.Size;
	}

	private static void UpdateCachedManagedObjectSize(Type type, ref ManagedObjectSize cachedSize)
	{
		cachedSize.Size = 0;
		var fields = type.GetFields(BindingFlags.Instance
			| BindingFlags.Public
			| BindingFlags.NonPublic);

		for (var i = fields.Length; i-- > 0;)
		{
			var fieldType = fields[i].FieldType;
			if (!typeof(ValueType).IsAssignableFrom(fieldType) || fieldType == typeof(ValueType))
				// IsValueType and IsSubclassOf somehow cause StackOverflows here
				cachedSize.Size += (uint)IntPtr.Size;
			else if (fieldType.IsPrimitive)
				cachedSize.Size += (uint)Marshal.SizeOf(fieldType);
			else if (fieldType != type)
				cachedSize.Size += fieldType.ComputeManagedObjectSizeEstimate();
			else
				cachedSize.Size += (uint)IntPtr.Size; // fuck it
		}

		var remainder = cachedSize.Size % (uint)IntPtr.Size;
		if (remainder != 0u)
			cachedSize.Size += (uint)IntPtr.Size - remainder;

		CachedManagedObjectSizes[type.TypeHandle.Value] = cachedSize;
	}

	private static FishTable<IntPtr, ManagedObjectSize> CachedManagedObjectSizes => _cachedManagedObjectSizes ??= new();

	[ThreadStatic]
	private static FishTable<IntPtr, ManagedObjectSize>? _cachedManagedObjectSizes;

	private struct ManagedObjectSize
	{
		public uint Size = uint.MaxValue;

		[UsedImplicitly]
		public ManagedObjectSize()
		{
		}
	}

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
		public override void AddRange(IList list, ICollection collection)
			=> ((List<T>)list).AddRange((IEnumerable<T>)collection);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override Array ToArray(IList list) => ((List<T>)list).ToArray();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override IList New(IEnumerable enumerable) => new List<T>((IEnumerable<T>)enumerable);
	}
}