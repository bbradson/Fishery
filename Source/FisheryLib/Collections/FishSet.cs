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
using FisheryLib.FunctionPointers;
using FisheryLib.Utility;
using JetBrains.Annotations;

namespace FisheryLib.Collections;

#pragma warning disable CS8766, CS8767
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public class FishSet<T> : ISet<T>, IReadOnlyCollection<T>, ICollection
{
	private const int FIBONACCI_HASH = -1640531527; // unchecked((int)(uint)Math.Round(uint.MaxValue / FishMath.PHI));
	
	internal T?[] _buckets;
	
	private unsafe delegate*<T?, int> _hashCodeGetter = GetHashCode<T?>.Default;
	private unsafe delegate*<T?, T?, bool> _equalityComparer = Equals<T?>.Default;

	private int
		_bucketBitShift,
		_count;
	
	internal Tails _tails;
	
	private int
		_wrapAroundMask,
		_version;

	private float _maxLoadFactor = 0.5f;

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

	object ICollection.SyncRoot => this;

	bool ICollection.IsSynchronized => false;

	public bool IsReadOnly => false;

	public event Action<T>?
		EntryAdded,
		EntryRemoved;

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

	private static T AssertKeyType(object key)
	{
		if (key is T tKey)
			return tKey;
		else if (key != null!)
			ThrowHelper.ThrowWrongKeyTypeArgumentException(key);

		return default!;
	}

	public bool this[T key]
	{
		[CollectionAccess(CollectionAccessType.Read)]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Contains(key);

		[CollectionAccess(CollectionAccessType.UpdatedContent)]
		set
		{
			if (value)
				InsertEntry(key, ReplaceBehaviour.Fail);
			else
				Remove(key);
		}
	}

	public FishSet() : this(0)
	{
	}

	public FishSet(int minimumCapacity) => Initialize(minimumCapacity);

	[MemberNotNull(nameof(_buckets))]
	[MemberNotNull(nameof(_tails))]
	private void Initialize(int minimumCapacity = 0)
	{
		minimumCapacity = minimumCapacity <= 4 ? 4 : FishMath.NextPowerOfTwo(minimumCapacity);
			// Mathf.NextPowerOfTwo(minimumCapacity);
		
		_buckets = new T?[minimumCapacity];
		_tails = new((uint)minimumCapacity);
		_bucketBitShift = 32 - FishMath.TrailingZeroCount((uint)_buckets.Length);
		_wrapAroundMask = _buckets.Length - 1;
	}

	public FishSet(IEnumerable<T> entries) : this(0)
	{
		foreach (var entry in entries)
			InsertEntry(entry, ReplaceBehaviour.Throw);
	}

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
		=> throw new NotImplementedException(); // (int)(uint)Math.Round(index * FishMath.PHI);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsTail(int bucketIndex)
		=> bucketIndex != GetBucketIndexForKeyAt(bucketIndex);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsTail(T entry, int bucketIndex)
		=> bucketIndex != GetBucketIndex(entry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool HasTail(int entryIndex) => !_tails.IsSoloOrEmpty(entryIndex);

	private void SetEntryWithoutTail(int bucketIndex, T entry)
	{
		_buckets[bucketIndex] = entry;
		_tails.SetSolo(bucketIndex);
	}
	
	private void SetBucketEmpty(int index)
	{
		_buckets[index] = default;
		_tails.SetEmpty(index);
	}

	private void SetEntryAsTail(int bucketIndex, T entry, int parentIndex, uint offset)
	{
		SetEntryWithoutTail(bucketIndex, entry);
		_tails[(uint)parentIndex] = offset;
	}

	private void SetParentTail(int entryIndex, uint newTail)
		=> _tails[(uint)GetParentBucketIndex(entryIndex)] = newTail;

	private bool InsertEntry(T entry, ReplaceBehaviour replaceBehaviour, bool shifting = false)
	{
		var addedNewEntry = InsertEntryInternal(entry, replaceBehaviour);

		if (shifting)
			return addedNewEntry;

		_version++;

		if (!addedNewEntry)
			return false;

		OnEntryAdded(entry);
		return true;
	}

	private void OnEntryAdded(T entry)
	{
		// _version++; handled separately
		_count++;
		EntryAdded?.Invoke(entry);
	}

	/// <summary>
	/// Returns true when adding a new entry, false for replacing an existing entry. AllowReplace: false causes throwing
	/// instead. Does not adjust Count, Version or invoke EntryAdded.
	/// </summary>
	private bool InsertEntryInternal(T entry, ReplaceBehaviour replaceBehaviour)
	{
	StartOfMethod:
		var bucketIndex = GetBucketIndex(entry);

		ref var bucket = ref _buckets[bucketIndex];
		if (IsBucketEmpty(bucketIndex))
		{
			SetEntryWithoutTail(bucketIndex, entry);
			return true;
		}

		if (TryReplaceValueOfMatchingKey(entry, replaceBehaviour, ref bucket!))
			return false;

		if (CheckResize())
			goto StartOfMethod;

		if (IsTail(bucket, bucketIndex))
		{
			var previousEntry = bucket;
			var tailingEntries = TryGetAndClearTailingEntries(bucketIndex);
			
			SetParentTail(bucketIndex, Tails.SOLO);
			SetEntryWithoutTail(bucketIndex, entry);
			InsertEntry(previousEntry, ReplaceBehaviour.Throw, true);
			
			if (tailingEntries != null)
				InsertEntries(tailingEntries, ReplaceBehaviour.Throw, true);

			return true;
		}
		else
		{
			return !TryFindTailIndexAndReplace(entry, ref bucketIndex, replaceBehaviour)
				&& InsertAsTail(entry, bucketIndex);
		}
	}

	private void InsertEntries(T[] tailingEntries, ReplaceBehaviour replaceBehaviour, bool shifting = false)
	{
		for (var i = 0; i < tailingEntries.Length; i++)
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

	private bool IsBucketEmpty(int bucketIndex) => IsBucketEmpty(bucketIndex, _tails, _buckets);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsBucketEmpty(int bucketIndex, Tails tails, T?[] buckets) => tails.IsEmpty(bucketIndex);

	private bool TryFindTailIndexAndReplace(T entry, ref int bucketIndex, ReplaceBehaviour replaceBehaviour)
	{
		while (true)
		{
			if (!TryContinueWithTail(ref bucketIndex))
				return false;
			
			if (TryReplaceValueOfMatchingKey(entry, replaceBehaviour, ref _buckets[bucketIndex]!))
				return true;
		}
	}

	private unsafe bool TryReplaceValueOfMatchingKey(T entry, ReplaceBehaviour replaceBehaviour, ref T bucket)
	{
		if (!_equalityComparer(bucket, entry))
			return false;

		switch (replaceBehaviour)
		{
			case ReplaceBehaviour.Throw:
				return ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(entry);
			case ReplaceBehaviour.Replace:
				bucket = entry;
				goto default;
			case ReplaceBehaviour.Fail:
			default:
				return true;
		}
	}

	private bool InsertAsTail(T entry, int bucketIndex)
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
		var tailingEntries = TryGetAndClearTailingEntries(bucketIndex);

		SetParentTail(bucketIndex, Tails.SOLO);
		SetEntryAsTail(bucketIndex, entry, parentIndex, offset);

		InsertEntry(previousEntry!, ReplaceBehaviour.Throw, true);
		
		if (tailingEntries != null)
			InsertEntries(tailingEntries, ReplaceBehaviour.Throw, true);

		return true;
	}

	private T[]? TryGetAndClearTailingEntries(int bucketIndex)
	{
		if (!HasTail(bucketIndex))
			return null;
		
		var tailingEntries = new T[GetTailCount(bucketIndex)];
		var i = 0;
		var tailIndex = GetTailIndex(bucketIndex);

		while (true)
		{
			ref var tailingBucket = ref _buckets[tailIndex];
			tailingEntries[i++] = tailingBucket!;
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

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void WarnForPossiblyExcessiveResizing(T entry)
		=> Log.Warning($"FishTable is resizing from a large number of clashing hashCodes. Last inserted key: '{
			entry}', count: '{_count}', bucket array length: '{
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool CheckResize()
	{
		if (_count <= (int)(_buckets.Length * _maxLoadFactor))
			return false;

		Resize();
		return true;
	}

	private void Resize()
	{
		var oldBuckets = _buckets;
		var oldTails = _tails;

		_tails = new((uint)_buckets.Length << 1);
		_buckets = new T?[_buckets.Length << 1];

		_bucketBitShift = 32 - FishMath.TrailingZeroCount((uint)_buckets.Length);
		_wrapAroundMask = _buckets.Length - 1;
		
		for (var i = 0; i < oldBuckets.Length; i++)
		{
			if (!IsBucketEmpty(i, oldTails, oldBuckets))
				InsertEntry(oldBuckets[i]!, ReplaceBehaviour.Throw, true);
		}
	}

	[CollectionAccess(CollectionAccessType.Read)]
	public unsafe bool Contains(T key)
	{
		var bucketIndex = GetBucketIndex(key);

		while (true)
		{
			if (_equalityComparer(key, _buckets[bucketIndex]))
				return true;
			
			if (!TryContinueWithTail(ref bucketIndex))
				return false;
		}
	}

	#region GetBucketIndexMethods

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private unsafe int GetBucketIndex(T key) => GetBucketIndex(_hashCodeGetter(key));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetBucketIndex(int hashCode) => (hashCode * FIBONACCI_HASH) >>> _bucketBitShift;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetBucketIndexForKeyAt(int index) => GetBucketIndex(_buckets[index]!);

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

	[CollectionAccess(CollectionAccessType.UpdatedContent)]
	public bool Add(T key)
	{
		Guard.IsNotNull(key);
		return InsertEntry(key, ReplaceBehaviour.Fail);
	}

	public void UnionWith(IEnumerable<T> other) => throw new NotImplementedException();

	public void IntersectWith(IEnumerable<T> other) => throw new NotImplementedException();

	public void ExceptWith(IEnumerable<T> other) => throw new NotImplementedException();

	public void SymmetricExceptWith(IEnumerable<T> other) => throw new NotImplementedException();

	public bool IsSubsetOf(IEnumerable<T> other) => throw new NotImplementedException();

	public bool IsSupersetOf(IEnumerable<T> other) => throw new NotImplementedException();

	public bool IsProperSupersetOf(IEnumerable<T> other) => throw new NotImplementedException();

	public bool IsProperSubsetOf(IEnumerable<T> other) => throw new NotImplementedException();

	public bool Overlaps(IEnumerable<T> other) => throw new NotImplementedException();

	public bool SetEquals(IEnumerable<T> other) => throw new NotImplementedException();

	[CollectionAccess(CollectionAccessType.ModifyExistingContent)]
	public bool Remove(T key)
	{
		if (!RemoveInternal(key))
			return false;

		OnEntryRemoved(key);
		return true;
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
			InsertEntry(entry!, ReplaceBehaviour.Throw, true);

			if (tailIndex < 0)
				return;

			entryIndex = tailIndex;
		}
	}
	
	private unsafe bool RemoveInternal(T key)
	{
		var bucketIndex = GetBucketIndex(key);
		
		while (true)
		{
			ref var entry = ref _buckets[bucketIndex];
				
			if (_equalityComparer(key, entry))
			{
				var tailIndex = HasTail(bucketIndex) ? GetTailIndex(bucketIndex) : -1;
				
				if (IsTail(entry!, bucketIndex))
					SetParentTail(bucketIndex, Tails.SOLO);

				SetBucketEmpty(bucketIndex);

				if (tailIndex != -1)
					EmplaceWithTailsBackward(tailIndex);

				return true;
			}

			if (TryContinueWithTail(ref bucketIndex))
				continue;

			return false;
		}
	}

	void ICollection<T>.Add(T item) => Add(item);

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
	public void CopyTo(T[] array, int arrayIndex)
	{
		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			if (IsBucketEmpty(i))
				continue;

			array[arrayIndex] = _buckets[i]!;
			arrayIndex++;
		}
	}

	private void OnEntryRemoved(T entry)
	{
		_version++;
		_count--;
		EntryRemoved?.Invoke(entry);
	}

	#region InterfaceMethods

	[CollectionAccess(CollectionAccessType.Read)]
	public Enumerator GetEnumerator() => new(this, Enumerator.KEY_VALUE_PAIR);

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
		=> new Enumerator(this, Enumerator.KEY_VALUE_PAIR);

	IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KEY_VALUE_PAIR);

	void ICollection.CopyTo(Array array, int index)
	{
		if (array is T[] typedArray)
		{
			CopyTo(typedArray, index);
			return;
		}

		var length = _buckets.Length;
		for (var i = 0; i < length; i++)
		{
			if (IsBucketEmpty(i))
				continue;

			((IList)array)[index] = _buckets[i];
			index++;
		}
	}

	#endregion

#region enums
	
	private enum ReplaceBehaviour
	{
		Replace,
		Throw,
		Fail
	}
	
#endregion


	#region Structs

	internal struct Tails(uint length)
	{
		public const uint
			EMPTY = 0u,
			SOLO = 1u;
		
		private NibbleArray _values = new(length);

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
	
	public record struct Enumerator : IEnumerator<T>
	{
		private readonly FishSet<T> _dictionary;
		private readonly int _version;
		private uint _index;
		private T? _current;

		internal const int DICT_ENTRY = 1;
		internal const int KEY_VALUE_PAIR = 2;

		internal Enumerator(FishSet<T> dictionary, int getEnumeratorRetType)
		{
			_dictionary = dictionary;
			_version = dictionary._version;
			_index = 0u;
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

				_current = _dictionary._buckets[_index - 1]!;
				return true;
			}

			_index = (uint)_dictionary._buckets.Length + 1u;
			_current = default;
			return false;
		}

		[CollectionAccess(CollectionAccessType.Read)]
		public T Current => _current!;

		public void Dispose()
		{
		}

		object IEnumerator.Current
		{
			get
			{
				if (_index == 0u || _index == (uint)_dictionary._buckets.Length + 1u)
					ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

				return _current!;
			}
		}

		void IEnumerator.Reset()
		{
			if (_version != _dictionary._version)
				ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

			_index = 0u;
			_current = default;
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
		internal static void ThrowWrongKeyTypeArgumentException(object? key)
		{
			Guard.IsNotNull(key);
			throw new ArgumentException($"The value \"{key}\" is not of type \"{
					typeof(T)}\" and cannot be used in this generic collection.",
				nameof(key));
		}

		[DoesNotReturn]
		internal static bool ThrowAddingDuplicateWithKeyArgumentException(T key)
		{
			Guard.IsNotNull(key);
			ThrowAddingDuplicateWithKeyArgumentException((object)key);
			return true;
		}
		
		[DoesNotReturn]
		internal static unsafe void ThrowFailedToFindParentInvalidOperationException(FishSet<T> fishTable,
			int childBucketIndex)
		{
			var entry = fishTable._buckets[childBucketIndex];
			throw new InvalidOperationException($"Failed to find parent index in FishTable for key: '{
				entry}', hashCode: '{fishTable._hashCodeGetter(entry)}', count: '{
					fishTable._count}', bucket array length: '{fishTable._buckets.Length}', tailing entries: '{
						fishTable.GetTailingEntriesCount()}'");
		}

		[DoesNotReturn]
		private static void ThrowAddingDuplicateWithKeyArgumentException(object key)
			=> throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
	}
}
#pragma warning restore CS8766, CS8767