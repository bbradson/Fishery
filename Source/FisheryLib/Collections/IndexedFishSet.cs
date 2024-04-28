// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib.Collections;

/// <summary>
/// Indexable collection similar to a list, but with constant time Contains, Remove, Insert and IndexOf methods. Order
/// of elements is not preserved on inserting and removing however. Displacement happens between the target index and
/// the last slot
/// </summary>
[PublicAPI]
public class IndexedFishSet<T> : IList<T>, IList, IReadOnlyList<T>
{
	private readonly List<T> _items = [];
	private readonly FishTable<T, int> _indices;
	
	public T this[int index]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _items[index];
		set
		{
			var previousValue = _items[index];
			_items[index] = value;
			_indices.Remove(previousValue);
			_indices.Add(value, index);
		}
	}

	public List<T> ReadOnlyList
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _items;
	}

	public ReadOnlySpan<T> Span
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _items.AsReadOnlySpan();
	}

	public IndexedFishSet() : this(static () => [])
	{
	}

	public IndexedFishSet(Func<FishTable<T, int>> indicesInitializer) => _indices = indicesInitializer();

	public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

	IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public void Add(T item)
	{
		if (!_indices.TryAdd(item, _items.Count))
			ThrowHelper.ThrowInvalidOperationException();
		
		_items.Add(item);
	}

	public int Add(object? value)
	{
		Add(EnsureCorrectType(value));
		return _items.Count - 1;
	}
	
	public bool TryAdd(T item)
	{
		if (!_indices.TryAdd(item, _items.Count))
			return false;
		
		_items.Add(item);
		return true;
	}

	public bool Contains(object? value) => IsCompatibleObject(value) && Contains((T)value);

	public void Clear()
	{
		_indices.Clear();
		_items.Clear();
	}

	public int IndexOf(object value) => IsCompatibleObject(value) ? IndexOf((T)value) : -1;

	public void Insert(int index, object value) => Insert(index, EnsureCorrectType(value));

	public void Remove(object value)
	{
		if (IsCompatibleObject(value))
			Remove((T)value);
	}

	public bool Contains(T item) => _indices.ContainsKey(item);

	public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

	public bool Remove(T item)
	{
		if (!_indices.Remove(item, out var index))
			return false;

		_items.RemoveAtFastUnordered(index);
		if (index < _items.Count)
			_indices[_items[index]] = index;
		
		return true;
	}

	public void CopyTo(Array array, int index) => ((IList)_items).CopyTo(array, index);

	public int Count => _items.Count;

	public object SyncRoot => this;

	public bool IsSynchronized => false;

	public bool IsReadOnly => false;

	public bool IsFixedSize => false;

	public int IndexOf(T item) => _indices.TryGetValue(item, out var index) ? index : -1;

	public void Insert(int index, T item)
	{
		if (!_indices.TryAdd(item, index))
			ThrowHelper.ThrowInvalidOperationException();
		
		_items.InsertFastUnordered(index, item);
		
		var lastIndex = _items.Count - 1;
		_indices[_items[lastIndex]] = lastIndex;
	}

	public void RemoveAt(int index)
	{
		var item = _items[index];
		_items.RemoveAtFastUnordered(index);
		_indices.Remove(item);
		
		if (index < _items.Count)
			_indices[_items[index]] = index;
	}

	object IList.this[int index]
	{
		get => this[index]!;
		set => this[index] = EnsureCorrectType(value);
	}
	
	private static bool IsCompatibleObject([NotNullWhen(true)] object? value)
		=> value is T || (value == null && default(T) == null);

	private static T EnsureCorrectType([NotNullWhen(true)] object? item)
		=> item is T t ? t : ThrowHelper.ThrowArgumentException<T>();
}