// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;

namespace FisheryLib.Collections;

[PublicAPI]
public record struct NibbleArray
{
	private uint _length;
	private byte[] _data;

	public uint this[uint index]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			Guard.IsLessThan(index, _length);
			
			return (index & 1u) == 0u ? _data[index >> 1] & 0b0000_1111u : (uint)_data[index >> 1] >> 4;
		}
		set
		{
			Guard.IsLessThan(value, 16u);
			Guard.IsLessThan(index, _length);
			
			_data[index >> 1] = (byte)((index & 1u) == 0u
				? (_data[index >> 1] & 0b1111_0000u) | value
				: (_data[index >> 1] & 0b0000_1111u) | (value << 4));
		}
	}

	public uint Length
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _length;
	}

	public NibbleArray(uint length)
	{
		_length = length;
		_data = new byte[(length + 1u) >> 1];
	}

	public void Resize(uint newSize)
	{
		_length = newSize;
		Array.Resize(ref _data, (int)(newSize + 1u) >> 1);
	}

	public void Clear() => _data.Clear();
	
	public void Initialize(uint value)
	{
		Guard.IsLessThan(value, 16u);
		Unsafe.InitBlockUnaligned(ref _data[0u], (byte)(value | (value << 4)), (uint)_data.Length);
	}
}