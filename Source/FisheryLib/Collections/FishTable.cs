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
using FisheryLib.Pools;
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
	
	public KeyCollection Keys => new(this);

	public ValueCollection Values => new(this);

	public int Version
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _version;
		set => _version = value;
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

	public uint CollisionCount => (uint)GetTailingEntriesCount();

	public event Action<KeyValuePair<TKey, TValue>>?
		EntryAdded,
		EntryRemoved;
	
	public Func<TKey, TValue> ValueInitializer { get; set; } = static _ => Reflection.New<TValue>();

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

	private sealed record KeyEventHandler(Action<TKey> Action) : IEventHandler<TKey>
	{
		public void Invoke(KeyValuePair<TKey, TValue> pair) => Action(pair.Key);
	}

	private sealed record ValueEventHandler(Action<TValue> Action) : IEventHandler<TValue>
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
	
	ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
	
	ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

	ICollection IDictionary.Keys => Keys;
	
	ICollection IDictionary.Values => Values;
	
	bool IDictionary.IsFixedSize => false;
	
	object ICollection.SyncRoot => this; // matching System.Collections.Generic.Dictionary
	
	bool ICollection.IsSynchronized => false;

	IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

	IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

	object IDictionary.this[object key]
	{
		get => this[AssertKeyType(key)]!;
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

	public unsafe TValue this[TKey key]
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
					return bucket.Value!;

				ContinueWithTailOrThrow(ref bucketIndex, key);
			}
		}

		[CollectionAccess(CollectionAccessType.UpdatedContent)]
		set => InsertEntry(key, value, ReplaceBehaviour.Replace);
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
	
	private static int GetJumpDistance(uint index)
		=> index switch
		{
			2 => 1, 3 => 2, 4 => 3, 5 => 4, 6 => 5, 7 => 8, 8 => 13, 9 => 21, 10 => 34, 11 => 55, 12 => 89, 13 => 144,
			14 => 233, 15 => 377, _ => MultiplyWithPhi(index)
		};

	[MethodImpl(MethodImplOptions.NoInlining)]
	[DoesNotReturn]
	private static int MultiplyWithPhi(uint index)
		=> throw new NotImplementedException($"Tried computing jump distance from a tails value of {index}");
	//	=> (int)(uint)Math.Round(index * FishMath.PHI);

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

	private void SetBucketEmpty(int index)
	{
		_buckets[index] = default;
		_tails.SetEmpty(index);
	}

	private void SetEntryAsTail(int bucketIndex, in Entry entry, int parentIndex, uint offset)
	{
		SetEntryWithoutTail(bucketIndex, entry);
		_tails[(uint)parentIndex] = offset;
	}

	private void SetParentTail(int entryIndex, uint newTail)
		=> _tails[(uint)GetParentBucketIndex(entryIndex)] = newTail;

	/// <summary>
	/// Returns true when adding a new entry, false for replacing an existing entry or failing to do so. Does not
	/// adjust Count, Version or invoke EntryAdded.
	/// </summary>
	private bool InsertEntry(TKey key, TValue? value, ReplaceBehaviour replaceBehaviour, bool shifting = false)
		=> InsertEntry(new(key, value), replaceBehaviour, shifting);

	/// <summary>
	/// Returns true when adding a new entry, false for replacing an existing entry or failing to do so. Does not
	/// adjust Count, Version or invoke EntryAdded.
	/// </summary>
	private bool InsertEntry(in Entry entry, ReplaceBehaviour replaceBehaviour, bool shifting = false)
	{
		var addedNewEntry = InsertEntryInternal(entry, replaceBehaviour);

		if (shifting)
			return addedNewEntry;

		_version++;

		if (!addedNewEntry)
			return false;

		OnEntryAdded(entry.AsKeyValuePair());
		return true;
	}

	private void OnEntryAdded(in KeyValuePair<TKey, TValue> entry)
	{
		// _version++; handled separately
		_count++;
		EntryAdded?.Invoke(entry);
	}

	/// <summary>
	/// Returns true when adding a new entry, false for replacing an existing entry or failing to do so. Does not
	/// adjust Count, Version or invoke EntryAdded.
	/// </summary>
	private bool InsertEntryInternal(in Entry entry, ReplaceBehaviour replaceBehaviour)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(entry.Key);

		ref var bucket = ref _buckets[bucketIndex];
		if (IsBucketEmpty(bucketIndex))
		{
			SetEntryWithoutTail(bucketIndex, entry);
			return true;
		}

		if (TryReplaceValueOfMatchingKey(entry, replaceBehaviour, ref bucket))
			return false;

		if (CheckResize())
			goto StartOfMethod;

		if (IsTail(ref bucket, bucketIndex))
		{
			var previousEntry = bucket;
			using var tailingEntries = TryGetAndClearTailingEntries(bucketIndex);
			
			SetParentTail(bucketIndex, Tails.SOLO);
			SetEntryWithoutTail(bucketIndex, entry);
			InsertEntry(previousEntry, ReplaceBehaviour.Throw, true);
			
			if (tailingEntries != default)
				InsertEntries(tailingEntries.List, ReplaceBehaviour.Throw, true);

			return true;
		}
		else
		{
			return !TryFindTailIndexAndReplace(entry, ref bucketIndex, replaceBehaviour)
				&& InsertAsTail(entry, bucketIndex);
		}
	}

	private void InsertEntries(List<Entry> tailingEntries, ReplaceBehaviour replaceBehaviour, bool shifting = false)
	{
		for (var i = 0; i < tailingEntries.Count; i++)
			InsertEntry(tailingEntries[i], replaceBehaviour, shifting);
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

	private bool TryFindTailIndexAndReplace(in Entry entry, ref int bucketIndex, ReplaceBehaviour replaceBehaviour)
	{
		while (true)
		{
			if (!TryContinueWithTail(ref bucketIndex))
				return false;
			
			if (TryReplaceValueOfMatchingKey(entry, replaceBehaviour, ref _buckets[bucketIndex]))
				return true;
		}
	}

	private unsafe bool TryReplaceValueOfMatchingKey(in Entry entry, ReplaceBehaviour replaceBehaviour,
		ref Entry bucket)
	{
		if (!_keyEqualityComparer(bucket.Key, entry.Key))
			return false;

		switch (replaceBehaviour)
		{
			case ReplaceBehaviour.Throw:
				return ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(entry.Key);
			case ReplaceBehaviour.Replace:
				bucket.Value = entry.Value;
				goto default;
			case ReplaceBehaviour.Return:
			default:
				return true;
		}
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
			InsertEntry(entry, ReplaceBehaviour.Throw, true);
			return true;
		}
		
		var previousEntry = _buckets[bucketIndex];
		using var tailingEntries = TryGetAndClearTailingEntries(bucketIndex);

		SetParentTail(bucketIndex, Tails.SOLO);
		SetEntryAsTail(bucketIndex, entry, parentIndex, offset);
		InsertEntry(previousEntry, ReplaceBehaviour.Throw, true);
		
		if (tailingEntries != default)
			InsertEntries(tailingEntries.List, ReplaceBehaviour.Throw, true);

		return true;
	}

	private PooledIList<List<Entry>> TryGetAndClearTailingEntries(int bucketIndex)
	{
		if (!HasTail(bucketIndex))
			return default;

		var tailingEntries = new PooledIList<List<Entry>>();
		var tailingEntriesList = tailingEntries.List;
		var tailIndex = GetTailIndex(bucketIndex);

		while (true)
		{
			ref var tailingBucket = ref _buckets[tailIndex];
			tailingEntriesList.Add(tailingBucket);
			tailingBucket = default;

			if (HasTail(tailIndex))
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

		return tailingEntries;
	}

	// probably fails due to varying jump distances
	/*private void EmplaceWithTailsForward(in Entry insertedEntry, int entryIndex)
	{
		var replacementEntry = insertedEntry;
		
		while (true)
		{
			if (entryIndex < 0)
			{
				InsertEntry(replacementEntry, false, true);
				return;
			}
			
			var tailIndex = HasTail(entryIndex) ? GetTailIndex(entryIndex) : -1;
			var previousEntry = _buckets[entryIndex];

			SetBucketEmpty(entryIndex);
			InsertEntry(replacementEntry, false, true);

			replacementEntry = previousEntry;
			entryIndex = tailIndex;
		}
	}*/

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
				InsertEntry(oldBuckets[i], ReplaceBehaviour.Throw, true);
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
	public unsafe bool TryGetOrAddValue(TKey key, out TValue value)
	{
		var result = true;

	StartOfLookup:
		var bucketIndex = GetBucketIndex(key);
		
		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];

			if (_keyEqualityComparer(key, bucket.Key))
			{
				value = bucket.Value!;
				return result;
			}

			if (ContinueWithTailOrAddNew(ref bucketIndex, key))
				continue;

			result = false;
			goto StartOfLookup;
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

	/// <summary>
	/// Returns a reference to a value field of an existing entry if one exists, otherwise to a new entry. This
	/// reference is only valid until the next time the collection gets modified. An invalid reference gives undefined
	/// behaviour and does not throw.
	/// </summary>
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

	/// <summary>
	/// Returns a reference to a value field of an existing entry if one exists, otherwise to a new entry. This
	/// reference is only valid until the next time the collection gets modified. An invalid reference gives undefined
	/// behaviour and does not throw.
	/// </summary>
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

	/// <summary>
	/// Returns a reference to a value field of an existing entry if one exists, otherwise throws a
	/// KeyNotFoundException. This reference is only valid until the next time the collection gets modified. An invalid
	/// reference gives undefined behaviour and does not throw.
	/// </summary>
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

	/// <summary>
	/// Returns a reference to a value field of an existing entry if one exists, otherwise throws a
	/// KeyNotFoundException. This reference is only valid until the next time the collection gets modified. An invalid
	/// reference gives undefined behaviour and does not throw.
	/// </summary>
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
	/// Returns a reference to a value field of an existing entry if one exists, otherwise an Unsafe.NullRef{TValue}.
	/// Unsafe.IsNullRef should be used to check for Unsafe.NullRef before assigning to a regular var or field without
	/// ref. Regular null checks throw. The returned reference is only valid until the next time the collection gets
	/// modified. An invalid reference gives undefined behaviour and does not throw.
	/// </summary>
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

	/// <summary>
	/// Returns a reference to a value field of an existing entry if one exists, otherwise an Unsafe.NullRef{TValue}.
	/// Unsafe.IsNullRef should be used to check for Unsafe.NullRef before assigning to a regular var or field without
	/// ref. Regular null checks throw. The returned reference is only valid until the next time the collection gets
	/// modified. An invalid reference gives undefined behaviour and does not throw.
	/// </summary>
	/// <returns>ref TValue or Unsafe.NullRef{TValue}</returns>
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

	[CollectionAccess(CollectionAccessType.Read)]
	public unsafe bool ContainsValue(TValue value)
	{
		var equalityComparer = Equals<TValue>.Default;
		
		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			if (!IsBucketEmpty(i) && equalityComparer(_buckets[i].Value!, value))
				return true;
		}

		return false;
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

	private int GetParentBucketIndex(int childBucketIndex, bool throwOnFailure = true)
	{
		var ancestorIndex = GetBucketIndexForKeyAt(childBucketIndex);

		var i = 0;

		while (true)
		{
			if (i++ > 32 || !HasTail(ancestorIndex))
			{
				if (throwOnFailure)
					ThrowHelper.ThrowFailedToFindParentInvalidOperationException(this, childBucketIndex);
				else
					return -1;
			}

			var nextIndex = GetTailIndex(ancestorIndex);
			if (nextIndex == childBucketIndex)
				break;
			
			ancestorIndex = nextIndex;
		}
		
		return ancestorIndex;
	}

	private bool HasParentBucket(int childBucketIndex) => GetParentBucketIndex(childBucketIndex, false) >= 0;

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

		this[key] = ValueInitializer(key);
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

		this[key] = ValueInitializer(key);
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
		InsertEntry(key, value, ReplaceBehaviour.Throw);
	}

	[CollectionAccess(CollectionAccessType.Read | CollectionAccessType.UpdatedContent)]
	public bool TryAdd(TKey key, TValue value)
	{
		Guard.IsNotNull(key);
		return InsertEntry(key, value, ReplaceBehaviour.Return);
	}

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public bool Remove(TKey key)
	{
		if (!RemoveInternal(key, out var removedEntry))
			return false;

		OnEntryRemoved(removedEntry.AsKeyValuePair());
		return true;
	}

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public bool Remove(TKey key, out TValue? value)
	{
		if (!RemoveInternal(key, out var removedEntry))
		{
			value = default;
			return false;
		}
		
		value = removedEntry.Value!;
		OnEntryRemoved(removedEntry.AsKeyValuePair());
		return true;
	}

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public int RemoveWhere(Predicate<KeyValuePair<TKey, TValue>> predicate)
	{
		Guard.IsNotNull(predicate);
		
		var removedCount = 0;
		
		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			if (IsBucketEmpty(i))
				continue;

			var entry = _buckets[i];

			if (!predicate(entry.AsKeyValuePair()))
				continue;

			if (!Remove(entry.Key))
				continue;
			
			removedCount++;
			i--; // extra check in case of emplacement after removal
		}
		
		return removedCount;
	}
	
	private void EmplaceWithTailsBackward(int entryIndex)
	{
		if (IsBucketEmpty(entryIndex))
			Utility.Diagnostics.ThrowHelper.ThrowInvalidOperationException("Tried to emplace entry bucket");
		
		while (true)
		{
			var tailIndex = HasTail(entryIndex) ? GetTailIndex(entryIndex) : -1;
			var entry = _buckets[entryIndex];

			SetBucketEmpty(entryIndex);
			InsertEntry(entry, ReplaceBehaviour.Throw, true);

			if (tailIndex < 0)
				return;

			entryIndex = tailIndex;
		}
	}

	private unsafe bool RemoveInternal(TKey key, out Entry removedEntry, bool checkValue = false,
		TValue? value = default)
	{
		var bucketIndex = GetBucketIndex(key);
		
		while (true)
		{
			ref var bucket = ref _buckets[bucketIndex];
				
			if (_keyEqualityComparer(key, bucket.Key))
			{
				if (checkValue && !value.Equals<TValue>(bucket.Value))
					goto OnFailure;

				removedEntry = bucket;
				
				var tailIndex = HasTail(bucketIndex) ? GetTailIndex(bucketIndex) : -1;
				
				if (IsTail(ref bucket, bucketIndex))
					SetParentTail(bucketIndex, Tails.SOLO);

				SetBucketEmpty(bucketIndex);

				if (tailIndex != -1)
					EmplaceWithTailsBackward(tailIndex);

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

#region enums
	
	private enum ReplaceBehaviour
	{
		Replace,
		Throw,
		Return
	}
	
#endregion

	#region Structs

	// [StructLayout(LayoutKind.Sequential, Pack = 1)]
	// matches KeyValuePair<T,V> and gets reinterpret cast into that in the Enumerator
	internal struct Entry(TKey key, TValue? value)
	{
		public TKey Key = key;
		public TValue? Value = value;

		[Pure]
		[UnscopedRef]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SecuritySafeCritical]
		public ref KeyValuePair<TKey, TValue> AsKeyValuePair()
			=> ref Unsafe.As<Entry, KeyValuePair<TKey, TValue>>(ref this);

		public override string ToString()
		{
			using var stringBuilder = new PooledStringBuilder(42);
			
			return stringBuilder
				.Append("FishTable<")
				.Append(typeof(TKey))
				.Append(", ")
				.Append(typeof(TValue))
				.Append(">.Entry { Key = ")
				.Append<TKey>(Key)
				.Append(", Value = ")
				.Append<TValue>(Value)
				.Append(" }").ToString();
		}
	}

	internal struct Tails(uint length)
	{
		public const uint
			EMPTY = 0u,
			SOLO = 1u;
		
		private NibbleArray _values = new(length);

		public uint Length => _values.Length;

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

	[PublicAPI]
	public readonly record struct KeyCollection(FishTable<TKey, TValue> Parent) : ICollection<TKey>, ICollection
	{
		public IEnumerator<TKey> GetEnumerator()
		{
			foreach (var entry in Parent)
				yield return entry.Key;
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public bool Contains(TKey item) => Parent.ContainsKey(item);

		public void CopyTo(TKey[] array, int arrayIndex)
		{
			foreach (var entry in Parent)
				array[arrayIndex++] = entry.Key;
		}

		public void CopyTo(Array array, int index)
		{
			if (array is TKey[] typedArray)
			{
				CopyTo(typedArray, index);
			}
			else
			{
				foreach (var entry in Parent)
					array.SetValue(entry.Key, index++);
			}
		}

		public bool Remove(TKey item) => Parent.Remove(item);

		public int RemoveWhere(Predicate<TKey> predicate)
		{
			Guard.IsNotNull(predicate);
			
			var removedCount = 0;
			var buckets = Parent._buckets;
			var tails = Parent._tails;
			var length = buckets.Length;
			
			for (var i = 0; i < length; i++)
			{
				if (Parent.IsBucketEmpty(i, tails, buckets))
					continue;

				var entry = buckets[i];

				if (!predicate(entry.Key))
					continue;

				if (!Parent.Remove(entry.Key))
					continue;
				
				removedCount++;
				i--; // extra check in case of emplacement after removal
			}
		
			return removedCount;
		}

		public int Count => Parent.Count;

		public bool IsReadOnly => true;

		void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException();

		void ICollection<TKey>.Clear() => throw new NotSupportedException();

		bool ICollection.IsSynchronized => false;

		object ICollection.SyncRoot => throw new NotSupportedException();
	}

	[PublicAPI]
	public readonly record struct ValueCollection(FishTable<TKey, TValue> Parent) : ICollection<TValue>, ICollection
	{
		public IEnumerator<TValue> GetEnumerator()
		{
			foreach (var entry in Parent)
				yield return entry.Value;
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public bool Contains(TValue item) => Parent.ContainsValue(item);

		public void CopyTo(TValue[] array, int arrayIndex)
		{
			foreach (var entry in Parent)
				array[arrayIndex++] = entry.Value;
		}

		public void CopyTo(Array array, int index)
		{
			if (array is TValue[] typedArray)
			{
				CopyTo(typedArray, index);
			}
			else
			{
				foreach (var entry in Parent)
					array.SetValue(entry.Value, index++);
			}
		}

		public unsafe bool Remove(TValue item)
		{
			var buckets = Parent._buckets;
			var tails = Parent._tails;
			var equalityComparer = Equals<TValue>.Default;
			var length = buckets.Length;
			
			for (var i = 0; i < length; i++)
			{
				if (Parent.IsBucketEmpty(i, tails, buckets))
					continue;

				var entry = buckets[i];

				if (!equalityComparer(entry.Value!, item))
					continue;

				if (Parent.Remove(entry.Key))
					return true;
			}
			
			return false;
		}

		public int RemoveWhere(Predicate<TValue> predicate)
		{
			Guard.IsNotNull(predicate);
			
			var removedCount = 0;
			var buckets = Parent._buckets;
			var tails = Parent._tails;
			var length = buckets.Length;
			
			for (var i = 0; i < length; i++)
			{
				if (Parent.IsBucketEmpty(i, tails, buckets))
					continue;

				var entry = buckets[i];

				if (!predicate(entry.Value!))
					continue;

				if (!Parent.Remove(entry.Key))
					continue;
				
				removedCount++;
				i--; // extra check in case of emplacement after removal
			}
		
			return removedCount;
		}

		public int Count => Parent.Count;

		public bool IsReadOnly => true;

		void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException();

		void ICollection<TValue>.Clear() => throw new NotSupportedException();

		bool ICollection.IsSynchronized => false;

		object ICollection.SyncRoot => throw new NotSupportedException();
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
		internal static bool ThrowAddingDuplicateWithKeyArgumentException<T>(T key)
		{
			Guard.IsNotNull(key);
			ThrowAddingDuplicateWithKeyArgumentException((object)key);
			return true;
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
			using var stringBuilder = new PooledStringBuilder(227);
			
			stringBuilder
				.Append("Failed to find parent index in FishTable<")
				.Append(typeof(TKey))
				.Append(", ")
				.Append(typeof(TValue))
				.Append("> for key: '")
				.Append<TKey>(entry.Key)
				.Append("', hashCode: '")
				.Append(fishTable._keyHashCodeGetter(entry.Key))
				.Append("', value: '")
				.Append<TValue>(entry.Value)
				.Append("', count: '")
				.Append(fishTable._count)
				.Append("', bucket array length: '")
				.Append(fishTable._buckets.Length)
				.Append("', total tailing entries count: '")
				.Append(fishTable.GetTailingEntriesCount())
				.Append("', known chain of tails:");

			var ancestorIndex = fishTable.GetBucketIndexForKeyAt(childBucketIndex);
			var i = 0;
			while (true)
			{
				var tailEntry = fishTable._buckets[ancestorIndex];

				stringBuilder.Append("\n{ index: '")
					.Append(ancestorIndex)
					.Append("' key: '")
					.Append<TKey>(tailEntry.Key)
					.Append("', hashCode: '")
					.Append(fishTable._keyHashCodeGetter(tailEntry.Key))
					.Append("', value: '")
					.Append<TValue>(tailEntry.Value)
					.Append(fishTable._tails.IsEmpty(ancestorIndex) ? " (empty) }" : " }");
				
				if (i++ > 32 || !fishTable.HasTail(ancestorIndex))
					break;
			
				ancestorIndex = fishTable.GetTailIndex(ancestorIndex);
			}
			
			throw new InvalidOperationException(stringBuilder.ToString());
		}

		[DoesNotReturn]
		private static void ThrowAddingDuplicateWithKeyArgumentException(object key)
			=> throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
	}
}
#pragma warning restore CS8766, CS8767