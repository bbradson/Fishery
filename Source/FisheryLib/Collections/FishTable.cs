// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Loosely based on https://github.com/matthewcrews/FastDictionaryTest/tree/main
// which itself is based on
// https://probablydance.com/2018/05/28/a-new-fast-hash-table-in-response-to-googles-new-fast-hash-table/

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using FisheryLib.FunctionPointers;
using FisheryLib.Utility;
using JetBrains.Annotations;

namespace FisheryLib.Collections;

#pragma warning disable CS8766, CS8767
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public class FishTable<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>
{
	private const int FIBONACCI_HASH = -1640531527; // unchecked((int)(uint)Math.Round(uint.MaxValue / FishMath.PHI));
	
	internal Entry[] _buckets;
	
	private unsafe delegate*<ref TKey, int> _keyHashCodeByRefGetter = GetHashCodeByRef<TKey>.Default;
	private unsafe delegate*<ref TKey, ref TKey, bool> _keyEqualityByRefComparer = EqualsByRef<TKey>.Default;

	private int
		_bucketBitShift,
		_count;
	
	private unsafe delegate*<TKey?, int> _keyHashCodeGetter = GetHashCode<TKey?>.Default;
	private unsafe delegate*<TKey?, TKey?, bool> _keyEqualityComparer = Equals<TKey?>.Default;
	
	internal Tails _tails;
	
	private int
		_wrapAroundMask,
		_version;

	private float _maxLoadFactor = 0.5f;

	public ICollection<TKey> Keys => throw new NotImplementedException();

	public ICollection<TValue> Values => throw new NotImplementedException();

	public int Version
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _version;
		set
		{
			Guard.IsGreaterThan(value, _version);
			_version = value;
		}
	}

	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count;
	}

	public bool IsReadOnly => false;

	public unsafe uint SizeEstimate
		=> GetType().ComputeManagedObjectSizeEstimate()
			+ ((uint)sizeof(Entry) * (uint)_buckets.Length)
			+ ((_tails.Length + 1u) >> 1);

	public event Action<KeyValuePair<TKey, TValue>>?
		EntryAdded,
		EntryRemoved;

	public delegate void EntryEventHandler(TKey key, ref TValue value);

	public event Action<TKey>? KeyAdded
	{
		add => AddEvent<TKey, KeyEventHandler>(ref EntryAdded, value);
		remove => RemoveEvent(ref EntryAdded, value);
	}

	public event Action<TKey>? KeyRemoved
	{
		add => AddEvent<TKey, KeyEventHandler>(ref EntryRemoved, value);
		remove => RemoveEvent(ref EntryRemoved, value);
	}

	public event Action<TValue>? ValueAdded
	{
		add => AddEvent<TValue, ValueEventHandler>(ref EntryAdded, value);
		remove => RemoveEvent(ref EntryAdded, value);
	}

	public event Action<TValue>? ValueRemoved
	{
		add => AddEvent<TValue, ValueEventHandler>(ref EntryRemoved, value);
		remove => RemoveEvent(ref EntryRemoved, value);
	}
	
	private static void AddEvent<T, THandler>(ref Action<KeyValuePair<TKey, TValue>>? @event, Action<T>? value)
		where THandler : IEventHandler<T>
	{
		if (value != null)
			@event += Reflection.New<THandler, Action<T>>(value).Invoke;
	}

	private static void RemoveEvent<T>(ref Action<KeyValuePair<TKey, TValue>>? @event, Action<T>? value)
	{
		if (@event is null)
			return;

		var delegates = @event.GetInvocationList();
		for (var i = delegates.Length; i-- > 0;)
		{
			if (delegates[i].Target is not IEventHandler<T> handler || handler.Action != value)
				continue;

			@event -= (Action<KeyValuePair<TKey, TValue>>)delegates[i];
			return;
		}
	}

	private interface IEventHandler<in T>
	{
		public Action<T> Action { get; }
		public void Invoke(KeyValuePair<TKey, TValue> pair);
	}

	private record KeyEventHandler(Action<TKey> Action) : IEventHandler<TKey>
	{
		public void Invoke(KeyValuePair<TKey, TValue> pair) => Action(pair.Key);
	}

	private record ValueEventHandler(Action<TValue> Action) : IEventHandler<TValue>
	{
		public void Invoke(KeyValuePair<TKey, TValue> pair) => Action(pair.Value);
	}

	public float MaxLoadFactor
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _maxLoadFactor;
		set
		{
			Guard.IsInRange(value, 0f, 1f);
			_maxLoadFactor = value;
		}
	}

	ICollection IDictionary.Keys => throw new NotImplementedException();
	
	ICollection IDictionary.Values => throw new NotImplementedException();
	
	bool IDictionary.IsFixedSize => false;
	
	object ICollection.SyncRoot => this; // matching System.Collections.Generic.Dictionary
	
	bool ICollection.IsSynchronized => false;
	
	IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => throw new NotImplementedException();
	
	IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => throw new NotImplementedException();
	
	object? IDictionary.this[object key]
	{
		get => this[AssertKeyType(key)];
		set => this[AssertKeyType(key)] = AssertValueType(value);
	}

	private static TKey AssertKeyType(object key)
	{
		if (key is TKey tKey)
			return tKey;
		else if (key != null!)
			ThrowHelper.ThrowWrongKeyTypeArgumentException(key);

		return default!;
	}
	
	private static TValue AssertValueType(object? value)
	{
		if (value is TValue tValue)
			return tValue;
		else if (value != null!)
			ThrowHelper.ThrowWrongValueTypeArgumentException(value);
		
		return default!;
	}

	public unsafe TValue? this[TKey key]
	{
		[CollectionAccess(CollectionAccessType.Read)]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			var bucketIndex = GetBucketIndex(key);

			while (true)
			{
				ref var bucket = ref _buckets[bucketIndex];

				if (_keyEqualityComparer(key, bucket.Key))
					return bucket.Value;

				ContinueWithTailOrThrow(ref bucketIndex, key);
			}
		}

		[CollectionAccess(CollectionAccessType.UpdatedContent)]
		set => InsertEntry(key, value);
	}

	public FishTable() : this(0)
	{
	}

	public FishTable(int minimumCapacity) => Initialize(minimumCapacity);

	[MemberNotNull(nameof(_buckets))]
	[MemberNotNull(nameof(_tails))]
	private void Initialize(int minimumCapacity = 0)
	{
		minimumCapacity = minimumCapacity <= 4 ? 4 : FishMath.NextPowerOfTwo(minimumCapacity);
			// Mathf.NextPowerOfTwo(minimumCapacity);
		
		_buckets = new Entry[minimumCapacity];
		_tails = new((uint)minimumCapacity);
		_bucketBitShift = 32 - FishMath.TrailingZeroCount((uint)_buckets.Length);
		_wrapAroundMask = _buckets.Length - 1;
	}

	public FishTable(IEnumerable<KeyValuePair<TKey, TValue>> entries) : this(0)
		=> AddRange(entries);

	private int GetTailIndex(int entryIndex) => GetIndexForTail(entryIndex, _tails[(uint)entryIndex]);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetIndexForTail(int entryIndex, uint tail) => (entryIndex + GetJumpDistance(tail)) & _wrapAroundMask;

	#region Junk

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

	#endregion
	
	private static int GetJumpDistance(uint index)
		=> index switch
		{
			2 => 1, 3 => 2, 4 => 3, 5 => 4, 6 => 5, 7 => 8, 8 => 13, 9 => 21, 10 => 34, 11 => 55, 12 => 89, 13 => 144,
			14 => 233, 15 => 377, _ => MultiplyWithPhi(index)
		};

	[MethodImpl(MethodImplOptions.NoInlining)]
	[DoesNotReturn]
	private static int MultiplyWithPhi(uint index)
		=> throw new NotImplementedException(); // (int)(uint)Math.Round(index * FishMath.PHI);

	// private static int ThrowForInvalidIndex(byte index)
	// 	=> throw new($"Jump distance index for FishTable too large. Should be < 99, got {index} instead.");

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsTail(int bucketIndex)
		=> bucketIndex != GetBucketIndexForKeyAt(bucketIndex);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsTail(ref Entry entry, int bucketIndex)
		=> bucketIndex != GetBucketIndex(entry.Key);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool HasTail(int entryIndex) => !_tails.IsSoloOrEmpty(entryIndex);

	private void SetEntryWithoutTail(int bucketIndex, in Entry entry)
	{
		_buckets[bucketIndex] = entry;
		_tails.SetSolo(bucketIndex);
	}

	private void SetEntryAsTail(int bucketIndex, in Entry entry, int parentIndex, uint offset)
	{
		SetEntryWithoutTail(bucketIndex, entry);
		_tails[(uint)parentIndex] = offset;
	}

	private void SetParentTail(int entryIndex, uint newTail)
		=> _tails[(uint)GetParentBucketIndex(entryIndex)] = newTail;

	private void InsertEntry(TKey key, TValue? value, bool allowReplace = true, bool shifting = false)
		=> InsertEntry(new(key, value), allowReplace, shifting);

	private void InsertEntry(in Entry entry, bool allowReplace = true, bool shifting = false)
	{
		var addedNewEntry = InsertEntryInternal(entry, allowReplace);

		if (shifting)
			return;

		_version++;

		if (!addedNewEntry)
			return;

		OnEntryAdded(entry.AsKeyValuePair());
	}

	private void OnEntryAdded(in KeyValuePair<TKey, TValue> entry)
	{
		// _version++; handled separately
		_count++;
		EntryAdded?.Invoke(entry);
	}

	/// <summary>
	/// Returns true when adding a new entry, false for replacing an existing entry. AllowReplace: false causes throwing
	/// instead. Does not adjust Count, Version or invoke EntryAdded.
	/// </summary>
	private bool InsertEntryInternal(in Entry entry, bool allowReplace = true)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(entry.Key);

		ref var bucket = ref _buckets[bucketIndex];
		if (IsBucketEmpty(bucketIndex))
		{
			SetEntryWithoutTail(bucketIndex, entry);
			return true;
		}

		if (TryReplaceValueOfMatchingKey(entry, allowReplace, ref bucket))
			return false;

		if (CheckResize())
			goto StartOfMethod;

		if (IsTail(ref bucket, bucketIndex))
		{
			var previousEntry = bucket;
			var hasTail = !_tails.IsSolo(bucketIndex);
			var tailingEntries = hasTail ? new Entry[GetTailCount(bucketIndex)] : null;

			if (hasTail)
			{
				var i = 0;
				var tailIndex = GetTailIndex(bucketIndex);

				while (true)
				{
					ref var tailingBucket = ref _buckets[tailIndex];
					tailingEntries![i++] = tailingBucket;
					tailingBucket = default;

					if (!_tails.IsSoloOrEmpty(tailIndex))
					{
						var nextTailIndex = GetTailIndex(tailIndex);
						_tails.SetEmpty(tailIndex);
						tailIndex = nextTailIndex;
					}
					else
					{
						_tails.SetEmpty(tailIndex);
						break;
					}
				}
			}
			
			SetParentTail(bucketIndex, Tails.SOLO);
			SetEntryWithoutTail(bucketIndex, entry);

			InsertEntry(previousEntry, shifting: true);
			if (hasTail)
			{
				for (var i = 0; i < tailingEntries!.Length; i++)
					InsertEntry(tailingEntries[i], shifting: true);
			}

			return true;
		}
		else
		{
			return !TryFindTailIndexAndReplace(entry, ref bucketIndex, allowReplace)
				&& InsertAsTail(entry, bucketIndex);
		}
	}

	private int GetTailCount(int index)
	{
		var tailCount = 0;

		while (HasTail(index))
		{
			tailCount++;
			index = GetTailIndex(index);
		}

		return tailCount;
	}

	private bool IsBucketEmpty(int bucketIndex) => IsBucketEmpty(bucketIndex, _tails, _buckets);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsBucketEmpty(int bucketIndex, Tails tails, Entry[] buckets)
		=> tails.IsEmpty(bucketIndex) && VerifyAgainstNullBucket(bucketIndex, tails, buckets);

	private unsafe bool VerifyAgainstNullBucket(int bucketIndex, Tails tails, Entry[] buckets)
	{
		ref var bucket = ref buckets[bucketIndex];

		if (!_keyEqualityComparer(bucket.Key, default))
			ThrowHelper.ThrowEmptyBucketKeyInvalidOperationException(bucketIndex, bucket);

		if (bucket.Value.Equals<TValue>(default))
			return true;

		// GetReference methods can get here when using a default key and accessing the returned ref
		tails.SetSolo(bucketIndex);
		OnEntryAdded(bucket.AsKeyValuePair());	// technically late, but it's an edge case and there's
		return false;							// no good workaround without performance hit
	}

	private bool TryFindTailIndexAndReplace(in Entry entry, ref int bucketIndex, bool allowReplace)
	{
		while (true)
		{
			if (!TryContinueWithTail(ref bucketIndex))
				return false;
			
			if (TryReplaceValueOfMatchingKey(entry, allowReplace, ref _buckets[bucketIndex]))
				return true;
		}
	}

	private unsafe bool TryReplaceValueOfMatchingKey(in Entry entry, bool allowReplace, ref Entry bucket)
	{
		if (!_keyEqualityComparer(bucket.Key, entry.Key))
			return false;

		if (!allowReplace)
			ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(entry.Key);

		bucket.Value = entry.Value;
		return true;
	}

	private bool InsertAsTail(in Entry entry, int bucketIndex)
	{
		var parentIndex = bucketIndex;
		var offset = 2u;

		while (true)
		{
			bucketIndex = GetIndexForTail(parentIndex, offset);
			
			if (IsBucketEmpty(bucketIndex))
			{
				SetEntryAsTail(bucketIndex, entry, parentIndex, offset);
				return true;
			}
			
			if (IsTail(bucketIndex) && offset < _tails[(uint)GetParentBucketIndex(bucketIndex)])
				break;

			if (++offset <= 15u)
				continue;
			
			if (_buckets.Length > Count * 5)
				WarnForPossiblyExcessiveResizing(entry);

			Resize();
			InsertEntry(entry, false, true);
			return true;
		}
		
		var previousEntry = _buckets[bucketIndex];
		var hasTail = !_tails.IsSolo(bucketIndex);
		
		var tailingEntries = hasTail ? new Entry[GetTailCount(bucketIndex)] : null;

		if (hasTail)
		{
			var k = 0;
			var tailIndex = GetTailIndex(bucketIndex);

			while (true)
			{
				ref var tailingBucket = ref _buckets[tailIndex];
				tailingEntries![k] = tailingBucket;

				k++;
				tailingBucket = default;

				if (!_tails.IsSoloOrEmpty(tailIndex))
				{
					var nextTailIndex = GetTailIndex(tailIndex);
					_tails.SetEmpty(tailIndex);
					tailIndex = nextTailIndex;
				}
				else
				{
					_tails.SetEmpty(tailIndex);
					break;
				}
			}
		}

		SetParentTail(bucketIndex, Tails.SOLO);
		SetEntryAsTail(bucketIndex, entry, parentIndex, offset);

		InsertEntry(previousEntry, shifting: true);
		
		if (hasTail)
		{
			for (var k = 0; k < tailingEntries!.Length; k++)
				InsertEntry(tailingEntries[k], shifting: true);
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void WarnForPossiblyExcessiveResizing(Entry entry)
		=> Log.Warning($"FishTable is resizing from a large number of clashing hashCodes. Last inserted key: '{
			entry.Key}', value: '{entry.Value}', count: '{_count}', bucket array length: '{
				_buckets.Length}', tailing entries: '{GetTailingEntriesCount()}'");

	private int GetTailingEntriesCount()
	{
		var count = 0;
		for (var i = _buckets.Length; i-- > 0;)
		{
			if (!IsBucketEmpty(i) && IsTail(i))
				count++;
		}

		return count;
	}

	public void EnsureCapacity(int minimumSize)
	{
		if (_buckets.Length >= minimumSize)
			return;
		
		Resize(FishMath.NextPowerOfTwo(minimumSize));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool CheckResize()
	{
		if (_count <= (int)(_buckets.Length * _maxLoadFactor))
			return false;

		Resize();
		return true;
	}

	private void Resize() => Resize(_buckets.Length << 1);

	private void Resize(int newSize)
	{
		var oldBuckets = _buckets;
		var oldTails = _tails;

		_tails = new((uint)newSize);
		_buckets = new Entry[newSize];

		_bucketBitShift = 32 - FishMath.TrailingZeroCount((uint)newSize);
		_wrapAroundMask = _buckets.Length - 1;
		
		for (var i = 0; i < oldBuckets.Length; i++)
		{
			if (!IsBucketEmpty(i, oldTails, oldBuckets))
				InsertEntry(oldBuckets[i], false, true);
		}
	}

	#region GetterMethods

	[CollectionAccess(CollectionAccessType.Read)]
	public unsafe bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
	{
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
			{
				value = bucket.Value!;
				return true;
			}

			if (TryContinueWithTail(ref bucketIndex))
				continue;

			value = default;
			return false;
		}
	}

	[CollectionAccess(CollectionAccessType.Read)]
	public unsafe TValue? TryGetValue(TKey key)
	{
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
				return bucket.Value;

			if (!TryContinueWithTail(ref bucketIndex))
				return default;
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.UpdatedContent)]
	public unsafe TValue GetOrAdd(TKey key)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
				return bucket.Value!;

			if (!ContinueWithTailOrAddNew(ref bucketIndex, key))
				goto StartOfMethod;
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.UpdatedContent)]
	public unsafe TValue GetOrAdd(ref TKey key)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(ref key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityByRefComparer(ref key, ref bucket.Key))
				return bucket.Value!;

			if (!ContinueWithTailOrAddNew(ref bucketIndex, ref key))
				goto StartOfMethod;
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.UpdatedContent)]
	public unsafe ref TValue GetOrAddReference(TKey key)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
				return ref bucket.Value!;

			if (!ContinueWithTailOrAddNew(ref bucketIndex, key))
				goto StartOfMethod;
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.UpdatedContent)]
	public unsafe ref TValue GetOrAddReference(ref TKey key)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(ref key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityByRefComparer(ref key, ref bucket.Key))
				return ref bucket.Value!;

			if (!ContinueWithTailOrAddNew(ref bucketIndex, ref key))
				goto StartOfMethod;
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.ModifyExistingContent)]
	public unsafe ref TValue GetReference(TKey key)
	{
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
				return ref bucket.Value!;

			ContinueWithTailOrThrow(ref bucketIndex, key);
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.ModifyExistingContent)]
	public unsafe ref TValue GetReference(ref TKey key)
	{
		var bucketIndex = GetBucketIndex(ref key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityByRefComparer(ref key, ref bucket.Key))
				return ref bucket.Value!;

			ContinueWithTailOrThrow(ref bucketIndex, ref key);
		}
	}

	/// <summary>
	/// Returns a reference to the value field of an entry if the key exists and an Unsafe.NullRef{TValue} if not.
	/// Must be checked with Unsafe.IsNullRef before assigning to a regular var or field without ref.
	/// </summary>
	/// <param name="key">The key</param>
	/// <returns>ref TValue or Unsafe.NullRef{TValue}</returns>
	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.ModifyExistingContent)]
	public unsafe ref TValue? TryGetReferenceUnsafe(TKey key)
	{
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
				return ref bucket.Value;

			if (!TryContinueWithTail(ref bucketIndex))
				return ref Unsafe.NullRef<TValue?>();
		}
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.ModifyExistingContent)]
	public unsafe ref TValue? TryGetReferenceUnsafe(ref TKey key)
	{
		var bucketIndex = GetBucketIndex(ref key);

		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityByRefComparer(ref key, ref bucket.Key))
				return ref bucket.Value;

			if (!TryContinueWithTail(ref bucketIndex))
				return ref Unsafe.NullRef<TValue?>();
		}
	}

	#endregion

	[CollectionAccess(CollectionAccessType.Read)]
	public unsafe bool ContainsKey(TKey key)
	{
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			if (_keyEqualityComparer(key, _buckets[bucketIndex].Key))
				return true;
			
			if (!TryContinueWithTail(ref bucketIndex))
				return false;
		}
	}

	#region GetBucketIndexMethods

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private unsafe int GetBucketIndex(ref TKey key) => GetBucketIndex(_keyHashCodeByRefGetter(ref key));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private unsafe int GetBucketIndex(TKey key) => GetBucketIndex(_keyHashCodeGetter(key));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetBucketIndex(int hashCode) => (hashCode * FIBONACCI_HASH) >>> _bucketBitShift;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetBucketIndexForKeyAt(int index) => GetBucketIndex(_buckets[index].Key);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetParentBucketIndex(int childBucketIndex)
	{
		var ancestorIndex = GetBucketIndexForKeyAt(childBucketIndex);

		var i = 0;

		while (true)
		{
			if (i++ > 32)
				ThrowHelper.ThrowFailedToFindParentInvalidOperationException(this, childBucketIndex);
			
			var nextIndex = GetTailIndex(ancestorIndex);
			if (nextIndex == childBucketIndex)
				break;
			
			ancestorIndex = nextIndex;
		}
		
		return ancestorIndex;
	}

	#endregion

	#region ContinueWithTailMethods

	/// <summary>
	/// Tries setting the index to the tail's index and returns true on success. If none exists, inserts a new entry
	/// and returns false
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool ContinueWithTailOrAddNew(ref int entryIndex, TKey key)
	{
		if (TryContinueWithTail(ref entryIndex))
			return true;

		this[key] = Reflection.New<TValue>();
		return false;
	}

	/// <summary>
	/// Tries setting the index to the tail's index and returns true on success. If none exists, inserts a new entry
	/// and returns false
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool ContinueWithTailOrAddNew(ref int entryIndex, ref TKey key)
	{
		if (TryContinueWithTail(ref entryIndex))
			return true;

		this[key] = Reflection.New<TValue>();
		return false;
	}

	/// <summary>
	/// Tries setting the index to the tail's index. If none exists, throws instead
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ContinueWithTailOrThrow(ref int entryIndex, TKey key)
	{
		if (!TryContinueWithTail(ref entryIndex))
			ThrowHelper.ThrowKeyNotFoundException(key);
	}

	/// <summary>
	/// Tries setting the index to the tail's index. If none exists, throws instead
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ContinueWithTailOrThrow(ref int entryIndex, ref TKey key)
	{
		if (!TryContinueWithTail(ref entryIndex))
			ThrowHelper.ThrowKeyNotFoundException(key);
	}

	/// <summary>
	/// Returns false for solo or empty, otherwise sets the index to the tail index and returns true
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool TryContinueWithTail(ref int entryIndex)
	{
		if (!HasTail(entryIndex))
			return false;

		entryIndex = GetTailIndex(entryIndex);
		return true;
	}

	#endregion

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void Add(TKey key, TValue value)
	{
		Guard.IsNotNull(key);
		InsertEntry(key, value, false);
	}

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public bool Remove(TKey key)
	{
		if (!RemoveInternal(key, out var removedEntry))
			return false;

		OnEntryRemoved(removedEntry.AsKeyValuePair());
		return true;
	}

	private void EmplaceWithTails(int entryIndex)
	{
		ref var bucket = ref _buckets[entryIndex];
		var entry = bucket;

		Span<int> tailIndices = stackalloc int[GetTailCount(entryIndex)];

		if (HasTail(entryIndex))
		{
			tailIndices[0] = GetTailIndex(entryIndex);
			for (var i = 1; i < tailIndices.Length; i++)
				tailIndices[i] = GetTailIndex(tailIndices[i - 1]);
		}
		
		if (IsTail(ref entry, entryIndex))
			SetParentTail(entryIndex, Tails.SOLO);
		
		bucket = default;
		_tails.SetEmpty(entryIndex);
		InsertEntry(entry, shifting: true);
		
		for (var i = 1; i < tailIndices.Length; i++)
		{
			var tailIndex = tailIndices[i];
			ref var tailBucket = ref _buckets[tailIndex];
			var tailEntry = tailBucket;

			tailBucket = default;
			_tails.SetEmpty(tailIndex);
			InsertEntry(tailEntry, shifting: true);
		}
	}
	
	private unsafe bool RemoveInternal(TKey key, out Entry removedEntry, bool checkValue = false,
		TValue? value = default)
	{
		var bucketIndex = GetBucketIndex(key);
		
		while (true)
		{
			ref var entry = ref _buckets[bucketIndex];
				
			if (_keyEqualityComparer(key, entry.Key))
			{
				if (checkValue && !value.Equals<TValue>(entry.Value))
					goto OnFailure;

				removedEntry = entry;
				
				var tailIndex = -1;
				if (IsTail(ref entry, bucketIndex))
				{
					if (HasTail(bucketIndex))
						tailIndex = GetTailIndex(bucketIndex);
					else
						SetParentTail(bucketIndex, Tails.SOLO);
				}

				entry.Key = default!;
				entry.Value = default;
				_tails.SetEmpty(bucketIndex);

				if (tailIndex != -1)
					EmplaceWithTails(tailIndex);

				return true;
			}

			if (TryContinueWithTail(ref bucketIndex))
				continue;

		OnFailure:
			Unsafe.SkipInit(out removedEntry);
			return false;
		}
	}

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void AddRange(IList<KeyValuePair<TKey, TValue>> range)
	{
		Guard.IsNotNull(range);
		
		EnsureCapacity((int)(range.Count * 1.5f));
		for (var i = range.Count; i-- > 0;)
			Add(range[i]);
	}

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> range)
	{
		Guard.IsNotNull(range);
		
		if (range is IList<KeyValuePair<TKey, TValue>> iList)
			AddRange(iList);
		else
			AddRangeFromEnumerable(range);
	}

	private void AddRangeFromEnumerable(IEnumerable<KeyValuePair<TKey, TValue>> range)
	{
		Guard.IsNotNull(range);
		
		foreach (var item in range)
			Add(item);
	}

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void AddRange<TSource>(IList<TSource> range, Func<TSource, TKey> keySelector,
		Func<TSource, TValue> valueSelector)
	{
		Guard.IsNotNull(range);
		Guard.IsNotNull(keySelector);
		Guard.IsNotNull(valueSelector);
		
		EnsureCapacity((int)(range.Count * 1.5f));
		for (var i = range.Count; i-- > 0;)
		{
			var item = range[i];
			Add(keySelector(item), valueSelector(item));
		}
	}

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void AddRange<TSource>(IEnumerable<TSource> range, Func<TSource, TKey> keySelector,
		Func<TSource, TValue> valueSelector)
	{
		Guard.IsNotNull(range);
		Guard.IsNotNull(keySelector);
		Guard.IsNotNull(valueSelector);
		
		if (range is IList<TSource> iList)
			AddRange(iList, keySelector, valueSelector);
		else
			AddRangeFromEnumerable(range, keySelector, valueSelector);
	}

	private void AddRangeFromEnumerable<TSource>(IEnumerable<TSource> range, Func<TSource, TKey> keySelector,
		Func<TSource, TValue> valueSelector)
	{
		Guard.IsNotNull(range);
		Guard.IsNotNull(keySelector);
		Guard.IsNotNull(valueSelector);

		foreach (var item in range)
			Add(keySelector(item), valueSelector(item));
	}

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void AddRange(IList<TKey> keys, IList<TValue> values)
	{
		Guard.IsNotNull(keys);
		Guard.IsNotNull(values);
		
		var count = keys.Count;
		if (count != values.Count)
			ThrowHelper.ThrowInvalidKeysValuesSizeArgumentException();

		EnsureCapacity((int)(count * 1.5f));
		for (var i = count; i-- > 0;)
			Add(keys[i], values[i]);
	}

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public void AddRange(IEnumerable<TKey> keys, IEnumerable<TValue> values)
	{
		Guard.IsNotNull(keys);
		Guard.IsNotNull(values);

		if (keys is IList<TKey> keysList && values is IList<TValue> valuesList)
			AddRange(keysList, valuesList);
		else
			AddRangeFromEnumerable(keys, values);
	}

	private void AddRangeFromEnumerable(IEnumerable<TKey> keys, IEnumerable<TValue> values)
	{
		Guard.IsNotNull(keys);
		Guard.IsNotNull(values);

		using var keyEnumerator = keys.GetEnumerator();
		using var valueEnumerator = values.GetEnumerator();

		while (keyEnumerator.MoveNext())
		{
			if (!valueEnumerator.MoveNext())
				ThrowHelper.ThrowInvalidKeysValuesSizeArgumentException();

			Add(keyEnumerator.Current, valueEnumerator.Current);
		}
	}

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public void Clear()
	{
		// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
		if (_buckets is null)
			Initialize();
		
		Array.Clear(_buckets, 0, _buckets.Length);
		_tails.Reset();
		
		_count = 0;
		_version++;
	}

	[CollectionAccess(CollectionAccessType.Read)]
	public bool Contains(KeyValuePair<TKey, TValue> item)
		=> TryGetValue(item.Key, out var value)
			&& value.Equals<TValue>(item.Value);

	[CollectionAccess(CollectionAccessType.Read)]
	public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
	{
		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			if (IsBucketEmpty(i))
				continue;

			array[arrayIndex] = _buckets[i].AsKeyValuePair()!;
			arrayIndex++;
		}
	}

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public bool Remove(KeyValuePair<TKey, TValue> item)
	{
		if (!RemoveInternal(item.Key, out _, true, item.Value))
			return false;

		OnEntryRemoved(item);
		return true;
	}

	private void OnEntryRemoved(in KeyValuePair<TKey, TValue> entry)
	{
		_version++;
		_count--;
		EntryRemoved?.Invoke(entry);
	}

	#region InterfaceMethods

	[CollectionAccess(CollectionAccessType.Read)]
	public Enumerator GetEnumerator() => new(this, Enumerator.KEY_VALUE_PAIR);

	IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
		=> new Enumerator(this, Enumerator.KEY_VALUE_PAIR);

	IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KEY_VALUE_PAIR);

	bool IDictionary.Contains(object key)
	{
		Guard.IsNotNull(key);

		return key is TKey tKey && ContainsKey(tKey);
	}

	void IDictionary.Add(object key, object value) => Add(AssertKeyType(key), AssertValueType(value));

	IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DICT_ENTRY);

	void IDictionary.Remove(object key)
	{
		Guard.IsNotNull(key);

		if (key is TKey tKey)
			Remove(tKey);
	}

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
			if (IsBucketEmpty(i))
				continue;

			((IList)array)[index] = _buckets[i].AsKeyValuePair();
			index++;
		}
	}

	#endregion

	#region Structs

	// [StructLayout(LayoutKind.Sequential, Pack = 1)]
	// matches KeyValuePair<T,V> and gets reinterpret cast into that in the Enumerator
	internal struct Entry
	{
		public TKey Key;
		public TValue? Value;

		public Entry(TKey key, TValue? value)
		{
			Key = key;
			Value = value;
		}

		[Pure]
		[UnscopedRef]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public ref KeyValuePair<TKey, TValue> AsKeyValuePair()
			=> ref Unsafe.As<Entry, KeyValuePair<TKey, TValue>>(ref this);
	}

	internal struct Tails
	{
		public const uint
			EMPTY = 0u,
			SOLO = 1u;
		
		private NibbleArray _values;

		public uint Length => _values.Length;

		public Tails(uint length) => _values = new(length);

		public uint this[uint index]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _values[index];
			
			set => _values[index] = value;
		}

		public void Reset() => _values.Clear(); //_values.Initialize(15u);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsEmpty(int index) => _values[(uint)index] == EMPTY;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSolo(int index) => _values[(uint)index] == SOLO;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSoloOrEmpty(int index) => _values[(uint)index] <= 1u;

		public void SetSolo(int index) => _values[(uint)index] = SOLO;

		public void SetEmpty(int index) => _values[(uint)index] = EMPTY;
	}
	
	public record struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
	{
		private readonly FishTable<TKey, TValue> _dictionary;
		private readonly int _version;
		private uint _index;
		private KeyValuePair<TKey, TValue> _current;
		private readonly int _getEnumeratorRetType; // What should Enumerator.Current return?

		internal const int DICT_ENTRY = 1;
		internal const int KEY_VALUE_PAIR = 2;

		internal Enumerator(FishTable<TKey, TValue> dictionary, int getEnumeratorRetType)
		{
			_dictionary = dictionary;
			_version = dictionary._version;
			_index = 0u;
			_getEnumeratorRetType = getEnumeratorRetType;
			_current = default;
		}

		public bool MoveNext()
		{
			if (_version != _dictionary._version)
				ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

			while (_index < (uint)_dictionary._buckets.Length)
			{
				if (_dictionary.IsBucketEmpty((int)_index++))
					continue;

				_current = _dictionary._buckets[_index - 1].AsKeyValuePair()!;
				return true;
			}

			_index = (uint)_dictionary._buckets.Length + 1u;
			_current = default;
			return false;
		}

		[CollectionAccess(CollectionAccessType.Read)]
		public KeyValuePair<TKey, TValue> Current => _current;

		public void Dispose()
		{
		}

		object IEnumerator.Current
		{
			get
			{
				if (_index == 0u || _index == (uint)_dictionary._buckets.Length + 1u)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _getEnumeratorRetType == DICT_ENTRY
					? new DictionaryEntry(_current.Key!, _current.Value)
					: _current;
			}
		}

		void IEnumerator.Reset()
		{
			if (_version != _dictionary._version)
				ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

			_index = 0u;
			_current = default;
		}

		DictionaryEntry IDictionaryEnumerator.Entry
		{
			get
			{
				if (_index == 0u || _index == (uint)_dictionary._buckets.Length + 1u)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return new(_current.Key!, _current.Value);
			}
		}

		object? IDictionaryEnumerator.Key
		{
			get
			{
				if (_index == 0u || _index == (uint)_dictionary._buckets.Length + 1u)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _current.Key;
			}
		}

		object? IDictionaryEnumerator.Value
		{
			get
			{
				if (_index == 0u || _index == (uint)_dictionary._buckets.Length + 1u)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _current.Value;
			}
		}
	}

	#endregion

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
		internal static void ThrowEmptyBucketKeyInvalidOperationException(int bucketIndex, Entry bucket)
			=> throw new InvalidOperationException(
				$"Bucket marked as empty had non-default key. This should never happen. Index: '{
					bucketIndex}', key: '{bucket.Key}', value: '{bucket.Value}'");
		
		[DoesNotReturn]
		internal static void ThrowInvalidKeysValuesSizeArgumentException()
			=> Utility.Diagnostics.ThrowHelper.ThrowArgumentException(
				"Input collections have different sizes. AddRange expects a matching Count for keys and values");
		
		[DoesNotReturn]
		internal static unsafe void ThrowFailedToFindParentInvalidOperationException(FishTable<TKey, TValue> fishTable,
			int childBucketIndex)
		{
			var entry = fishTable._buckets[childBucketIndex];
			throw new InvalidOperationException($"Failed to find parent index in FishTable for key: '{
				entry.Key}', hashCode: '{fishTable._keyHashCodeGetter(entry.Key)}', value: '{entry.Value}', count: '{
					fishTable._count}', bucket array length: '{fishTable._buckets.Length}', tailing entries: '{
						fishTable.GetTailingEntriesCount()}'");
		}

		[DoesNotReturn]
		private static void ThrowAddingDuplicateWithKeyArgumentException(object key)
			=> throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
	}
}
#pragma warning restore CS8766, CS8767