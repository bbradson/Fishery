// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace FisheryLib.Collections;

/// <summary>
/// Indexable collection similar to a list, but with constant time Contains, Remove, Insert and IndexOf methods for any
/// argument type. Order of elements is not preserved on inserting and removing however. Displacement happens between
/// the target index and the last slot. X and Y are individually enforced to be unique
/// </summary>
[PublicAPI]
public class IndexedFishMap<TX, TY> : IList<(TX X, TY Y)>, IList, IReadOnlyList<(TX X, TY Y)>
	// IDictionary<TX, TY>, IDictionary<TY, TX>
{
	private readonly List<(TX X, TY Y)> _items = [];
	private readonly FishTable<TX, int> _xIndices = new();
	private readonly FishTable<TY, int> _yIndices = new();
	
	public (TX X, TY Y) this[int index]
	{
		get => _items[index];
		set
		{
			var previousValue = _items[index];
			_items[index] = value;
			
			_xIndices.Remove(previousValue.X);
			_xIndices.Add(value.X, index);
			
			_yIndices.Remove(previousValue.Y);
			_yIndices.Add(value.Y, index);
		}
	}
	
	public TY this[TX x]
	{
		get => _items[_xIndices[x]].Y;
		set
		{
		Start:
			if (!_xIndices.TryGetValue(x, out var index))
				index = _items.Count;

			if (!_yIndices.TryAdd(value, index))
			{
				if (RemoveIfNotEqual(_yIndices, value, index))
					goto Start;
				else
					return;
			}

			if (index == _items.Count)
			{
				_xIndices[x] = index;
				_items.Add((x, value));
			}
			else
			{
				_items[index] = (x, value);
			}
		}
	}
	
	public TX this[TY y]
	{
		get => _items[_yIndices[y]].X;
		set
		{
		Start:
			if (!_yIndices.TryGetValue(y, out var index))
				index = _items.Count;

			if (!_xIndices.TryAdd(value, index))
			{
				if (RemoveIfNotEqual(_xIndices, value, index))
					goto Start;
				else
					return;
			}

			if (index == _items.Count)
			{
				_yIndices[y] = index;
				_items.Add((value, y));
			}
			else
			{
				_items[index] = (value, y);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RemoveIfNotEqual<TKey>(FishTable<TKey, int> fishTable, TKey key, int index)
		=> fishTable[key] != index && fishTable.Remove(key);

	public List<(TX X, TY Y)> ReadOnlyList => _items;

	public List<(TX X, TY Y)>.Enumerator GetEnumerator() => _items.GetEnumerator();

	IEnumerator<(TX X, TY Y)> IEnumerable<(TX X, TY Y)>.GetEnumerator() => GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public void Add((TX X, TY Y) item)
	{
		if (_xIndices.ContainsKey(item.X) || !_yIndices.TryAdd(item.Y, _items.Count))
			ThrowHelper.ThrowInvalidOperationException();
		
		_xIndices.Add(item.X, _items.Count);

		_items.Add(item);
	}

	public int Add(object? value)
	{
		Add(EnsureCorrectType(value));
		return _items.Count - 1;
	}
	
	public bool TryAdd((TX X, TY Y) item)
	{
		if (_xIndices.ContainsKey(item.X) || !_yIndices.TryAdd(item.Y, _items.Count))
			return false;
		
		_xIndices.Add(item.X, _items.Count);
		
		_items.Add(item);
		return true;
	}

	public bool Contains(object? value) => IsCompatibleObject(value) && Contains(((TX X, TY Y))value);

	public void Clear()
	{
		_xIndices.Clear();
		_items.Clear();
	}

	public int IndexOf(object value) => IsCompatibleObject(value) ? IndexOf(((TX X, TY Y))value) : -1;

	public void Insert(int index, object value) => Insert(index, EnsureCorrectType(value));

	public void Remove(object value)
	{
		if (IsCompatibleObject(value))
			Remove(((TX X, TY Y))value);
	}

	public bool Contains((TX X, TY Y) item) => _xIndices.ContainsKey(item.X);

	public bool Contains(TX x) => _xIndices.ContainsKey(x);

	public bool Contains(TY y) => _yIndices.ContainsKey(y);

	public void CopyTo((TX X, TY Y)[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

	public bool Remove((TX X, TY Y) item)
	{
		if (!_xIndices.ContainsKey(item.X) || !_yIndices.Remove(item.Y))
			return false;
		
		_xIndices.Remove(item.X, out var index);

		_items.RemoveAtFastUnordered(index);
		if (index < _items.Count)
			_xIndices[_items[index].X] = _yIndices[_items[index].Y] = index;
		
		return true;
	}

	public bool Remove(TX x)
	{
		if (!_xIndices.Remove(x, out var index))
			return false;
		
		_yIndices.Remove(_items[index].Y);

		_items.RemoveAtFastUnordered(index);
		if (index < _items.Count)
			_xIndices[_items[index].X] = _yIndices[_items[index].Y] = index;
		
		return true;
	}

	public bool Remove(TY y)
	{
		if (!_yIndices.Remove(y, out var index))
			return false;
		
		_xIndices.Remove(_items[index].X);

		_items.RemoveAtFastUnordered(index);
		if (index < _items.Count)
			_xIndices[_items[index].X] = _yIndices[_items[index].Y] = index;
		
		return true;
	}

	public void CopyTo(Array array, int index) => ((IList)_items).CopyTo(array, index);

	public int Count => _items.Count;

	public object SyncRoot => this;

	public bool IsSynchronized => false;

	public bool IsReadOnly => false;

	public bool IsFixedSize => false;

	public int IndexOf((TX X, TY Y) item) => _xIndices.TryGetValue(item.X, out var index) ? index : -1;

	public int IndexOf(TX x) => _xIndices.TryGetValue(x, out var index) ? index : -1;

	public int IndexOf(TY y) => _yIndices.TryGetValue(y, out var index) ? index : -1;

	public void Insert(int index, (TX X, TY Y) item)
	{
		if (_xIndices.ContainsKey(item.X) || !_yIndices.TryAdd(item.Y, index))
			ThrowHelper.ThrowInvalidOperationException();
		
		_xIndices.Add(item.X, index);
		
		_items.InsertFastUnordered(index, item);
		
		var lastIndex = _items.Count - 1;
		_xIndices[_items[lastIndex].X] = _yIndices[_items[lastIndex].Y] = lastIndex;
	}

	public void RemoveAt(int index)
	{
		var item = _items[index];
		_items.RemoveAtFastUnordered(index);
		_xIndices.Remove(item.X);
		_yIndices.Remove(item.Y);
		
		if (index < _items.Count)
			_xIndices[_items[index].X] = _yIndices[_items[index].Y] = index;
	}

	object IList.this[int index]
	{
		get => this[index];
		set => this[index] = EnsureCorrectType(value);
	}
	
	private static bool IsCompatibleObject([NotNullWhen(true)] object? value)
		=> value is (TX, TY) || (value == null) & (default(TX) == null) & (default(TY) == null);

	private static (TX X, TY Y) EnsureCorrectType([NotNullWhen(true)] object? item)
		=> item is ValueTuple<TX, TY> t ? t : ThrowHelper.ThrowArgumentException<(TX X, TY Y)>();
}