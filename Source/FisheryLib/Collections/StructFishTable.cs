// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Based on https://github.com/matthewcrews/FastDictionaryTest/tree/main
// which itself is based on
// https://probablydance.com/2018/05/28/a-new-fast-hash-table-in-response-to-googles-new-fast-hash-table/

#if false

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FisheryLib.FunctionPointers;
using JetBrains.Annotations;

namespace FisheryLib.Collections;

#pragma warning disable CS8766, CS8767
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct StructFishTable<TKey, TValue> : ICollection
	//: IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>
{
	private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn
		=> new byte[]
		{
			0, 1, 28, 2, 29, 14, 24, 3, 30, 22,
			20, 15, 25, 17, 4, 8, 31, 27, 13, 23,
			21, 19, 16, 7, 26, 12, 18, 6, 11, 5,
			10, 9
		};
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int TrailingZeroCount(uint value)
		=> value == 0
			? 32
			: Unsafe.AddByteOffset(ref TrailingZeroCountDeBruijn.DangerousGetPinnableReference(),
				(IntPtr)(int)(((value & unchecked(0 - value)) * 125613361) >> 27));

	internal Entry[] _buckets;
	
	private unsafe delegate*<ref TKey, int> _keyHashCodeByRefGetter = GetHashCodeByRef<TKey>.Default;
	private unsafe delegate*<ref TKey, ref TKey, bool> _keyEqualityByRefComparer = EqualsByRef<TKey>.Default;

	private int
		_bucketBitShift,
		_count;
	
	private unsafe delegate*<TKey, int> _keyHashCodeGetter = GetHashCode<TKey>.Default;
	private unsafe delegate*<TKey, TKey, bool> _keyEqualityComparer = Equals<TKey>.Default;
	
	private int
		_wrapAroundMask,
		_version;

	//private KeyCollection _keys;

	//private ValueCollection _values;

	public ICollection<TKey> Keys => throw new NotImplementedException();

	public ICollection<TValue> Values => throw new NotImplementedException();

	public int Version
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _version;
	}

	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count;
	}

	public bool IsReadOnly => false;

	// ICollection IDictionary.Keys => throw new NotImplementedException();
	//
	// ICollection IDictionary.Values => throw new NotImplementedException();
	//
	// bool IDictionary.IsFixedSize => false;
	object ICollection.SyncRoot => this; // matching System.Collections.Generic.Dictionary
	bool ICollection.IsSynchronized => false;
	//
	// IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => throw new NotImplementedException();
	//
	// IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => throw new NotImplementedException();
	//
	// object? IDictionary.this[object key]
	// {
	// 	get
	// 		=> key is TKey tKey
	// 			? this[tKey]
	// 			: ThrowHelper.ThrowKeyNotFoundException<object, TValue>(key);
	//
	// 	set
	// 	{
	// 		if (key is TKey tKey)
	// 		{
	// 			switch (value)
	// 			{
	// 				case TValue tValue:
	// 					this[tKey] = tValue;
	// 					break;
	// 				case null:
	// 					this[tKey] = default;
	// 					break;
	// 				default:
	// 					ThrowHelper.ThrowWrongValueTypeArgumentException(value);
	// 					break;
	// 			}
	// 		}
	// 		else
	// 		{
	// 			ThrowHelper.ThrowWrongKeyTypeArgumentException(key);
	// 		}
	// 	}
	// }

	public unsafe TValue? this[TKey key]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			var hashCode = _keyHashCodeGetter(key);
			var entryIndex = ComputeBucketIndex(hashCode);
			
			while (true)
			{
				ref var entry = ref _buckets[entryIndex];
				
				if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
					return entry.Value;
			
				if (entry.IsLast)
					ThrowHelper.ThrowKeyNotFoundException(key);
			
				SetNextEntryIndex(ref entryIndex, entry.Next);
			}
		}
		set
		{
			AddEntry(key, value);
			Resize();
		}
	}

	public StructFishTable() : this(0)
	{
	}

	public StructFishTable(int minimumCapacity) => Initialize(minimumCapacity);

	[MemberNotNull(nameof(_buckets))]
	private void Initialize(int minimumCapacity = 0)
	{
		//_buckets = new Entry[minimumCapacity <= 1 ? 1 : Mathf.NextPowerOfTwo(minimumCapacity)];
		
		_buckets = new Entry[4];
		for (var i = 0; i < _buckets.Length; i++)
			_buckets[i].Next = byte.MaxValue;

		_bucketBitShift = 32 - TrailingZeroCount((uint)_buckets.Length);
		_wrapAroundMask = _buckets.Length - 1;
	}

	public StructFishTable(IEnumerable<KeyValuePair<TKey, TValue>> entries) : this(0)
	{
		foreach (var entry in entries)
		{
			AddEntry(entry.Key, entry.Value);
			Resize();
		}
	}

	private void SetNextEntryIndex(ref int entryIndex, byte next) => entryIndex = GetNextEntryIndex(entryIndex, next);

	private int GetNextEntryIndex(int entryIndex, byte next) => (entryIndex + next /*GetJumpDistance(next)*/) & _wrapAroundMask;

	// private static long GetJumpDistanceLong(byte index)
	// 	=> index switch
	// 	{
	// 		0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7,
	// 		8 => 8, 9 => 9, 10 => 10, 11 => 11, 12 => 12, 13 => 13, 14 => 14,
	// 		15 => 15, 16 => 21, 17 => 28, 18 => 36, 19 => 45, 20 => 55, 21 => 66,
	// 		22 => 78, 23 => 91, 24 => 105, 25 => 120, 26 => 136, 27 => 153,
	// 		28 => 171, 29 => 190, 30 => 210, 31 => 231, 32 => 253, 33 => 276,
	// 		34 => 300, 35 => 325, 36 => 351, 37 => 378, 38 => 406, 39 => 435,
	// 		40 => 465, 41 => 496, 42 => 528, 43 => 561, 44 => 595, 45 => 630,
	// 		46 => 666, 47 => 703, 48 => 741, 49 => 780, 50 => 820, 51 => 861,
	// 		52 => 903, 53 => 946, 54 => 990, 55 => 1035, 56 => 1081, 57 => 1128,
	// 		58 => 1176, 59 => 1225, 60 => 1275, 61 => 1326, 62 => 1378, 63 => 1431,
	// 		64 => 1485, 65 => 1540, 66 => 1596, 67 => 1653, 68 => 1711, 69 => 1770,
	// 		70 => 1830, 71 => 1891, 72 => 1953, 73 => 2016, 74 => 2080, 75 => 2145,
	// 		76 => 2211, 77 => 2278, 78 => 2346, 79 => 2415, 80 => 2485, 81 => 2556,
	// 		82 => 3741, 83 => 8385, 84 => 18915, 85 => 42486, 86 => 95703, 87 => 215496,
	// 		88 => 485605, 89 => 1091503, 90 => 2456436, 91 => 5529475, 92 => 12437578,
	// 		93 => 27986421, 94 => 62972253, 95 => 141700195, 96 => 318819126, 97 => 717314626,
	// 		98 => 1614000520, 99 => 3631437253, 100 => 8170829695, 101 => 18384318876, 102 => 41364501751,
	// 		103 => 93070021080, 104 => 209407709220, 105 => 471167588430, 106 => 1060127437995, 107 => 2385287281530,
	// 		108 => 5366895564381, 109 => 12075513791265, 110 => 27169907873235, 111 => 61132301007778,
	// 		112 => 137547673121001, 113 => 309482258302503, 114 => 696335090510256, 115 => 1566753939653640,
	// 		116 => 3525196427195653, 117 => 7931691866727775, 118 => 17846306747368716,
	// 		119 => 40154190394120111, 120 => 90346928493040500, 121 => 203280588949935750,
	// 		122 => 457381324898247375, 123 => 1029107980662394500, 124 => 2315492957028380766,
	// 		125 => 5209859150892887590, > 125 => throw new()
	// 	};
	
	// private static int GetJumpDistance(byte index)
	// 	=> index switch
	// 	{
	// 		0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, 9 => 9, 10 => 10, 11 => 11,
	// 		12 => 12, 13 => 13, 14 => 14, 15 => 15, 16 => 21, 17 => 28, 18 => 36, 19 => 45, 20 => 55, 21 => 66,
	// 		22 => 78, 23 => 91, 24 => 105, 25 => 120, 26 => 136, 27 => 153, 28 => 171, 29 => 190, 30 => 210, 31 => 231,
	// 		32 => 253, 33 => 276, 34 => 300, 35 => 325, 36 => 351, 37 => 378, 38 => 406, 39 => 435, 40 => 465,
	// 		41 => 496, 42 => 528, 43 => 561, 44 => 595, 45 => 630, 46 => 666, 47 => 703, 48 => 741, 49 => 780,
	// 		50 => 820, 51 => 861, 52 => 903, 53 => 946, 54 => 990, 55 => 1035, 56 => 1081, 57 => 1128, 58 => 1176,
	// 		59 => 1225, 60 => 1275, 61 => 1326, 62 => 1378, 63 => 1431, 64 => 1485, 65 => 1540, 66 => 1596, 67 => 1653,
	// 		68 => 1711, 69 => 1770, 70 => 1830, 71 => 1891, 72 => 1953, 73 => 2016, 74 => 2080, 75 => 2145, 76 => 2211,
	// 		77 => 2278, 78 => 2346, 79 => 2415, 80 => 2485, 81 => 2556, 82 => 3741, 83 => 8385, 84 => 18915,
	// 		85 => 42486, 86 => 95703, 87 => 215496, 88 => 485605, 89 => 1091503, 90 => 2456436, 91 => 5529475,
	// 		92 => 12437578, 93 => 27986421, 94 => 62972253, 95 => 141700195, 96 => 318819126, 97 => 717314626,
	// 		98 => 1614000520, 255 => int.MaxValue, > 98 => ThrowForInvalidIndex(index)
	// 	};

	private static int ThrowForInvalidIndex(byte index)
		=> throw new($"Jump distance index for FishTable too large. Should be < 99, got {index} instead.");

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int ComputeBucketIndex(int hashCode) => (hashCode * -1640531527) >>> _bucketBitShift;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private unsafe bool IsTail(int bucketIndex)
		=> bucketIndex != ComputeBucketIndex(_keyHashCodeGetter(_buckets[bucketIndex].Key));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetParentBucketIndex(int hashCode, int childBucketIndex)
	{
		var ancestorIndex = ComputeBucketIndex(hashCode);
		while (true)
		{
			var nextIndex = GetNextEntryIndex(ancestorIndex, _buckets[ancestorIndex].Next);
			if (nextIndex == childBucketIndex)
				break;
			
			ancestorIndex = nextIndex;
		}
		return ancestorIndex;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private unsafe byte DistanceFromParent(int bucketIndex)
		=> _buckets[GetParentBucketIndex(_keyHashCodeGetter(_buckets[bucketIndex].Key), bucketIndex)].Next;
	
	private void SetEntry(byte next, int hashCode, TKey key, TValue? value, int bucketIndex)
	{
		ref var entry = ref _buckets[bucketIndex];
		entry.Next = next;
		entry.Key = key;
		entry.Value = value;
	}
	
	private unsafe void RemoveFromList(int entryIndex)
	{
		ref var entry = ref _buckets[entryIndex];
		var parentEntryIndex = GetParentBucketIndex(_keyHashCodeGetter(entry.Key), entryIndex);
		
		_buckets[parentEntryIndex].Next
			= entry.IsLast ? (byte)0 : (byte)(_buckets[parentEntryIndex].Next + entry.Next);
	}

	private unsafe void AddEntry(TKey key, TValue? value) => AddEntry(_keyHashCodeGetter(key), key, value);
	
	private unsafe void AddEntry(int hashCode, TKey key, TValue? value)
	{
		_version++;
		
		var entryIndex = ComputeBucketIndex(hashCode);
		ref var entry = ref _buckets[entryIndex];
		if (entry.IsAvailable)
		{
			SetEntry(0, hashCode, key, value, entryIndex);
			_count++;
		}
		else if (_keyHashCodeGetter(entry.Key) == hashCode && _keyEqualityComparer(entry.Key, key))
		{
			entry.Value = value;
		}
		else if (IsTail(entryIndex))
		{
			var previousEntry = _buckets[entryIndex];
			RemoveFromList(entryIndex);
			SetEntry(0, hashCode, key, value, entryIndex);
			AddEntry(_keyHashCodeGetter(previousEntry.Key), previousEntry.Key, previousEntry.Value);
			_count++;
		}
		else
		{
			ListSearch(hashCode, key, value, entryIndex);
		}
	}
	
	private unsafe void ListSearch(int hashCode, TKey key, TValue? value, int bucketIndex)
	{
		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];
			if (_keyHashCodeGetter(bucket.Key) == hashCode && _keyEqualityComparer(bucket.Key, key))
			{
				bucket.Value = value;
				return;
			}
			if (bucket.IsLast | bucket.IsEmpty)
				break;
			
			SetNextEntryIndex(ref bucketIndex, bucket.Next);
		}
		InsertIntoNextEmptyBucket(bucketIndex, hashCode, key, value, 1, bucketIndex + 1);
	}
	
	private unsafe void InsertIntoNextEmptyBucket(int parentIndex, int hashCode, TKey key, TValue? value, byte offset, int bucketIndex)
	{
		while (true)
		{
			if (bucketIndex < _buckets.Length)
			{
				if (_buckets[bucketIndex].IsAvailable)
				{
					SetEntry(0, hashCode, key, value, bucketIndex);
					_count++;
					_buckets[parentIndex].Next = offset;
					return;
				}
				if (IsTail(bucketIndex) && (uint)offset > DistanceFromParent(bucketIndex))
					break;

				bucketIndex++;
				offset++;
			}
			else
			{
				bucketIndex = 0;
			}
		}
		var previousEntry = _buckets[bucketIndex];
		RemoveFromList(bucketIndex);
		SetEntry(0, hashCode, key, value, bucketIndex);
		_buckets[parentIndex].Next = offset;
		AddEntry(_keyHashCodeGetter(previousEntry.Key), previousEntry.Key, previousEntry.Value);
	}
	
	private void Resize()
	{
		if (_count <= (_buckets.Length >> 2) * 3)
			return;
		var oldBuckets = _buckets;
		
		_buckets = new Entry[_buckets.Length << 1];
		for (var i = 0; i < _buckets.Length; i++)
			_buckets[i].Next = byte.MaxValue;

		_bucketBitShift = 64 - TrailingZeroCount((uint)_buckets.Length);
		_wrapAroundMask = _buckets.Length - 1;
		_count = 0;
		for (var i = 0; i < oldBuckets.Length; i++)
		{
			var bucket = oldBuckets[i];
			if (bucket.IsEntry)
				AddEntry(bucket.Key, bucket.Value);
		}
	}

	public unsafe bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
	{
		var hashCode = _keyHashCodeGetter(key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];
				
			if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
			{
				value = entry.Value!;
				return true;
			}

			if (entry.IsLast | entry.IsEmpty)
			{
				value = default;
				return false;
			}

			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}

	public unsafe TValue? TryGetValue(TKey key)
	{
		var hashCode = _keyHashCodeGetter(key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];
				
			if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
				return entry.Value;
			
			if (entry.IsLast | entry.IsEmpty)
				return default;
			
			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}
	
	public unsafe TValue GetOrAdd(TKey key)
	{
		while (true)
		{
			var hashCode = _keyHashCodeGetter(key);
			var entryIndex = ComputeBucketIndex(hashCode);
			
			while (true)
			{
				ref var entry = ref _buckets[entryIndex];
				
				if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
					return entry.Value!;
			
				if (entry.IsLast | entry.IsEmpty)
				{
					this[key] = Reflection.New<TValue>();
					break;
				}

				SetNextEntryIndex(ref entryIndex, entry.Next);
			}
		}
	}
	
	public unsafe TValue GetOrAdd(ref TKey key)
	{
		while (true)
		{
			var hashCode = _keyHashCodeByRefGetter(ref key);
			var entryIndex = ComputeBucketIndex(hashCode);
			
			while (true)
			{
				ref var entry = ref _buckets[entryIndex];
				
				if ((hashCode == _keyHashCodeByRefGetter(ref entry.Key)) & _keyEqualityByRefComparer(ref key, ref entry.Key))
					return entry.Value!;
			
				if (entry.IsLast | entry.IsEmpty)
				{
					this[key] = Reflection.New<TValue>();
					break;
				}

				SetNextEntryIndex(ref entryIndex, entry.Next);
			}
		}
	}

	public unsafe ref TValue GetOrAddReference(TKey key)
	{
		while (true)
		{
			var hashCode = _keyHashCodeGetter(key);
			var entryIndex = ComputeBucketIndex(hashCode);
			
			while (true)
			{
				ref var entry = ref _buckets[entryIndex];
				
				if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
					return ref entry.Value!;
			
				if (entry.IsLast | entry.IsEmpty)
				{
					this[key] = Reflection.New<TValue>();
					break;
				}

				SetNextEntryIndex(ref entryIndex, entry.Next);
			}
		}
	}

	public unsafe ref TValue GetOrAddReference(ref TKey key)
	{
		while (true)
		{
			var hashCode = _keyHashCodeByRefGetter(ref key);
			var entryIndex = ComputeBucketIndex(hashCode);
			
			while (true)
			{
				ref var entry = ref _buckets[entryIndex];

				if ((hashCode == _keyHashCodeByRefGetter(ref entry.Key))
					& _keyEqualityByRefComparer(ref key, ref entry.Key))
				{
					return ref entry.Value!;
				}

				if (entry.IsLast | entry.IsEmpty)
				{
					this[key] = Reflection.New<TValue>();
					break;
				}

				SetNextEntryIndex(ref entryIndex, entry.Next);
			}
		}
	}

	public unsafe ref TValue GetReference(TKey key)
	{
		var hashCode = _keyHashCodeGetter(key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];
				
			if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
				return ref entry.Value!;
			
			if (entry.IsLast | entry.IsEmpty)
				ThrowHelper.ThrowKeyNotFoundException(key);
			
			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}

	public unsafe ref TValue GetReference(ref TKey key)
	{
		var hashCode = _keyHashCodeByRefGetter(ref key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];
				
			if ((hashCode == _keyHashCodeByRefGetter(ref entry.Key)) & _keyEqualityByRefComparer(ref key, ref entry.Key))
				return ref entry.Value!;
			
			if (entry.IsLast | entry.IsEmpty)
				ThrowHelper.ThrowKeyNotFoundException(key);
			
			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}

	/// <summary>
	/// Returns a reference to the value field of an entry if the key exists and an Unsafe.NullRef{TValue} if not.
	/// Must be checked with Unsafe.IsNullRef before assigning to a regular var or field without ref.
	/// </summary>
	/// <param name="key">The key</param>
	/// <returns>ref TValue or Unsafe.NullRef{TValue}</returns>
	public unsafe ref TValue? TryGetReferenceUnsafe(TKey key)
	{
		var hashCode = _keyHashCodeGetter(key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];
				
			if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
				return ref entry.Value;
			
			if (entry.IsLast | entry.IsEmpty)
				return ref Unsafe.NullRef<TValue?>();
			
			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}
	
	public unsafe ref TValue? TryGetReferenceUnsafe(ref TKey key)
	{
		var hashCode = _keyHashCodeByRefGetter(ref key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];

			if ((hashCode == _keyHashCodeByRefGetter(ref entry.Key))
				& _keyEqualityByRefComparer(ref key, ref entry.Key))
			{
				return ref entry.Value;
			}

			if (entry.IsLast | entry.IsEmpty)
				return ref Unsafe.NullRef<TValue?>();
			
			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}

	public unsafe bool ContainsKey(TKey key)
	{
		var hashCode = _keyHashCodeGetter(key);
		var entryIndex = ComputeBucketIndex(hashCode);
			
		while (true)
		{
			ref var entry = ref _buckets[entryIndex];
				
			if ((hashCode == _keyHashCodeGetter(entry.Key)) & _keyEqualityComparer(key, entry.Key))
				return true;
			
			if (entry.IsLast | entry.IsEmpty)
				return false;
			
			SetNextEntryIndex(ref entryIndex, entry.Next);
		}
	}

#if false
	public void Add(TKey key, TValue value)
	{
		Guard.IsNotNull(key);

		if (
			_count
			== _buckets.Length) // throwing DuplicateKeyExceptions happens later, so Expanding occurs based on expected
			Expand();           // behaviour instead of matching actual results like in System.Collections.Generic.

		var keyCode = (uint)HashCode.Get(key);
		ref var entry = ref _buckets[FastModulo(keyCode, (uint)_buckets.Length)];

		while (entry.Next != null)
		{
			if (entry.HashCode == keyCode && entry.Key.Equals<TKey>(key))
				ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(key);

			if (entry.Next == EntryReference.Empty)
				break;

			entry = ref entry.Next.Entry;
		}

		if (entry.Next == null) // means the entry is empty
			entry = new(keyCode, key, value);
		else // if (entry.Next == _freeSlot) // occupied entry with an empty slot in entry.Next
			entry.Next = new(keyCode, key, value);
		// entry.Next != _freeSlot would be a threading issue, as this was just checked in the loop. Unhandled.

		_count++;
		_version++;
	}

	private void Expand()
	{
		var newLength = Mathf.NextPowerOfTwo(_buckets.Length + 1);
		var newArray = new Entry[newLength];

		for (var i = 0; i < _buckets.Length; i++)
		{
			var entry = _buckets[i];

			var nextEntry = entry.Next;
			if (nextEntry is null)
				continue;

		BeforeAdding:
			ref var newEntry = ref newArray[FastModulo(entry.HashCode, (uint)newLength)];

			while (newEntry.Next != null)
			{
				if (newEntry.HashCode == entry.HashCode && newEntry.Key.Equals<TKey>(entry.Key))
					ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(entry.Key);

				if (newEntry.Next == EntryReference.Empty)
					break;

				newEntry = ref newEntry.Next.Entry;
			}

			entry.Next = EntryReference.Empty;

			if (newEntry.Next == null)
				newEntry = entry;
			else
				newEntry.Next = new(entry);

			if (nextEntry != EntryReference.Empty)
			{
				entry = nextEntry.Entry;
				goto BeforeAdding;
			}
		}

		_buckets = newArray;
	}

	private ref Entry TryFindEntry(TKey key)
	{
		var keyCode = (uint)HashCode.Get(key);
		ref var entry = ref _buckets[FastModulo(keyCode, (uint)_buckets.Length)];

		while (entry.HashCode != keyCode || !entry.Key.Equals<TKey>(key))
		{
			if (entry.Next != EntryReference.Empty && entry.Next != null)
				entry = ref entry.Next.Entry;
			else
				return ref Unsafe.NullRef<Entry>();
		}

		return ref entry;
	}

	private void RemoveEntry(ref Entry entry)
	{
		entry = entry.Next != EntryReference.Empty && entry.Next != null
			? entry.Next.Entry
			: default;

		_count--;
		_version++;
	}

	public bool Remove(TKey key)
	{
		ref var entry = ref TryFindEntry(key);
		if (Unsafe.IsNullRef(ref entry))
			return false;

		RemoveEntry(ref entry);
		return true;
	}

	public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
#endif

	public void Clear()
	{
		if (_buckets is null)
			Initialize();
		
		Array.Clear(_buckets, 0, _buckets.Length);
		for (var i = 0; i < _buckets.Length; i++)
			_buckets[i].Next = byte.MaxValue;
		
		_count = 0;
		_version++;
	}

	public bool Contains(KeyValuePair<TKey, TValue> item)
		=> TryGetValue(item.Key, out var value)
			&& value.Equals<TValue>(item.Value);

	public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
	{
		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			ref var entry = ref _buckets[i];
			if (entry.IsEmpty)
				continue;

			array[arrayIndex] = new(entry.Key, entry.Value!);
			arrayIndex++;
		}
	}

#if false
	public bool Remove(KeyValuePair<TKey, TValue> item)
	{
		ref var entry = ref TryFindEntry(item.Key);

		if (Unsafe.IsNullRef(ref entry)
			|| !entry.Value.Equals<TValue>(item.Value))
		{
			return false;
		}

		RemoveEntry(ref entry);
		return true;
	}
#endif

	public Enumerator GetEnumerator() => new(this, Enumerator.KEY_VALUE_PAIR);

	// IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
	// 	=> new Enumerator(this, Enumerator.KEY_VALUE_PAIR);

	IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KEY_VALUE_PAIR);

#if false
	bool IDictionary.Contains(object key)
	{
		Guard.IsNotNull(key);

		return key is TKey tkey
			&& ContainsKey(tkey);
	}

	void IDictionary.Add(object key, object value) => throw new NotImplementedException();

	//IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DICT_ENTRY);

	void IDictionary.Remove(object key)
	{
		Guard.IsNotNull(key);

		if (key is TKey tkey)
			Remove(tkey);
	}
#endif

	void ICollection.CopyTo(Array array, int index)
	{
		if (array is KeyValuePair<TKey, TValue>[] typedArray)
		{
			CopyTo(typedArray, index);
			return;
		}
		
		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			ref var entry = ref _buckets[i];
			if (entry.IsEmpty)
				continue;

			((IList)array)[index] = new KeyValuePair<TKey,TValue>(entry.Key, entry.Value!);
			index++;
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct Entry
	{
		public byte Next;
		public TKey Key;
		public TValue? Value;
		
		internal bool IsTombstone
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Next == 254;
		}

		internal bool IsEmpty
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Next == byte.MaxValue;
		}

		internal bool IsEntry
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Next < 254u;
		}

		internal bool IsOccupied
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Next <= 254u;
		}

		internal bool IsAvailable
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Next >= 254u;
		}

		internal bool IsLast
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Next == 0;
		}
	}
	
	public record struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
	{
		private readonly StructFishTable<TKey, TValue> _dictionary;
		private readonly int _version;
		private int _index;
		private KeyValuePair<TKey, TValue> _current;
		private readonly int _getEnumeratorRetType; // What should Enumerator.Current return?

		internal const int DICT_ENTRY = 1;
		internal const int KEY_VALUE_PAIR = 2;

		internal Enumerator(StructFishTable<TKey, TValue> dictionary, int getEnumeratorRetType)
		{
			_dictionary = dictionary;
			_version = dictionary._version;
			_index = 0;
			_getEnumeratorRetType = getEnumeratorRetType;
			_current = default;
		}

		public bool MoveNext()
		{
			if (_version != _dictionary._version)
				ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

			// Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
			// dictionary.count+1 could be negative if dictionary.count is int.MaxValue
			while ((uint)_index < (uint)_dictionary._buckets.Length)
			{
				ref var entry = ref _dictionary._buckets[_index++];

				if (entry.IsEmpty)
					continue;

				_current = new(entry.Key, entry.Value!);
				return true;
			}

			_index = _dictionary._buckets.Length + 1;
			_current = default;
			return false;
		}

		public KeyValuePair<TKey, TValue> Current => _current;

		public void Dispose()
		{
		}

		object IEnumerator.Current
		{
			get
			{
				if (_index == 0 || _index == _dictionary._buckets.Length + 1)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _getEnumeratorRetType == DICT_ENTRY
					? new DictionaryEntry(_current.Key!, _current.Value)
					: new KeyValuePair<TKey, TValue>(_current.Key, _current.Value);
			}
		}

		void IEnumerator.Reset()
		{
			if (_version != _dictionary._version)
				ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

			_index = 0;
			_current = default;
		}

		DictionaryEntry IDictionaryEnumerator.Entry
		{
			get
			{
				if (_index == 0 || _index == _dictionary._buckets.Length + 1)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return new(_current.Key!, _current.Value);
			}
		}

		object? IDictionaryEnumerator.Key
		{
			get
			{
				if (_index == 0 || _index == _dictionary._buckets.Length + 1)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _current.Key;
			}
		}

		object? IDictionaryEnumerator.Value
		{
			get
			{
				if (_index == 0 || _index == _dictionary._buckets.Length + 1)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _current.Value;
			}
		}
	}

	private static class ThrowHelper
	{
		[DoesNotReturn]
		internal static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion()
			=> throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

		[DoesNotReturn]
		internal static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen()
			=> throw new InvalidOperationException("Enumeration has either not started or has already finished.");

		[DoesNotReturn]
		internal static void ThrowKeyNotFoundException<T>(T key)
		{
			Guard.IsNotNull(key);
			ThrowKeyNotFoundException((object)key);
		}

		[DoesNotReturn]
		internal static V ThrowKeyNotFoundException<T, V>(T key)
		{
			Guard.IsNotNull(key);
			ThrowKeyNotFoundException((object)key);
			return default;
		}

		[DoesNotReturn]
		private static void ThrowKeyNotFoundException(object key)
			=> throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

		[DoesNotReturn]
		internal static void ThrowWrongValueTypeArgumentException(object value)
			=> throw new ArgumentException(
				$"The value \"{value}\" is not of type \"{
					typeof(TValue)}\" and cannot be used in this generic collection.",
				nameof(value));

		[DoesNotReturn]
		internal static void ThrowWrongKeyTypeArgumentException(object? key)
		{
			Guard.IsNotNull(key);
			throw new ArgumentException($"The value \"{key}\" is not of type \"{
					typeof(TKey)}\" and cannot be used in this generic collection.",
				nameof(key));
		}

		[DoesNotReturn]
		internal static void ThrowAddingDuplicateWithKeyArgumentException<T>(T key)
		{
			Guard.IsNotNull(key);
			ThrowAddingDuplicateWithKeyArgumentException((object)key);
		}

		[DoesNotReturn]
		private static void ThrowAddingDuplicateWithKeyArgumentException(object key)
			=> throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
	}
}

#pragma warning restore CS8766, CS8767

#endif