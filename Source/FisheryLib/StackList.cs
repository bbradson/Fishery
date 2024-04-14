// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;

namespace FisheryLib;

#pragma warning disable CS9081, CS9080
[PublicAPI]
public unsafe ref struct StackList<T> where T : unmanaged
{
	public const int MAX_LENGTH = 1024;
	
	private Span<T> _span;

	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private set;
	}
		
	public T this[int index]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			Guard.IsLessThan(index, Count);
			return _span[index];
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			Guard.IsLessThan(index, Count);
			_span[index] = value;
		}
	}

	public void Add(T value)
	{
		if (++Count >= _span.Length)
			Expand();

		_span[Count - 1] = value;
	}

	private void Expand()
	{
		var newLength = _span.Length * 2;
		var newSpan = newLength > MAX_LENGTH ? new T[newLength] : stackalloc T[newLength];
		_span.CopyTo(newSpan);
		_span = newSpan;
	}

	public void RemoveAt(int index)
	{
		Guard.IsLessThan(index, Count);
		Count--;

		if (index == Count)
			return;

		_span[index..Count].CopyTo(_span);
	}

	public T[] ToArray()
	{
		var array = new T[Count];
		_span[..Count].CopyTo(array);
		return array;
	}

	public List<T> ToList()
	{
		var list = new List<T>(Count);
		_span[..Count].CopyTo(list._items);
		return list;
	}

	public StackList() => _span = stackalloc T[4];

	public StackList(int capacity) => _span = capacity > MAX_LENGTH ? new T[capacity] : stackalloc T[capacity];
}
#pragma warning restore CS9081