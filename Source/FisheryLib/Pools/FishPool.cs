// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;

namespace FisheryLib.Pools;

public static class FishPool<T> where T : IFishPoolable
{
	[ThreadStatic]
	private static T?[]? _itemsThreadStatic;

	[ThreadStatic]
	private static int _itemCountThreadStatic;

	private static object _lockObject = new();
	private static T?[] _itemsGlobal = new T[16];
	private static int _itemCountGlobal;

	public static T Get()
	{
		if (_itemCountThreadStatic == 0)
			FetchThroughLock();

		ref var itemBucket = ref _itemsThreadStatic![--_itemCountThreadStatic];
		var item = itemBucket;
		itemBucket = default;
		return item!;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FetchThroughLock()
	{
		var itemsThreadStatic = _itemsThreadStatic ??= InitializeItemsThreadStatic();
		var createNew = false;
		
		lock (_lockObject)
		{
			if (_itemCountGlobal != 0)
			{
				for (var i = 8; i-- > 0;)
				{
					itemsThreadStatic[i] = _itemsGlobal[--_itemCountGlobal];
					_itemsGlobal[_itemCountGlobal] = default;
				}
			}
			else
			{
				createNew = true;
			}
		}

		if (createNew)
		{
			for (var i = 8; i-- > 0;)
				itemsThreadStatic[i] = Reflection.New<T>();
		}
		
		_itemCountThreadStatic = 8;
	}

	public static void Return(T item)
	{
		item.Reset();

		var itemsThreadStatic = _itemsThreadStatic ??= InitializeItemsThreadStatic();
		
		if (_itemCountThreadStatic == 16)
			PushThroughLock();

		itemsThreadStatic[_itemCountThreadStatic++] = item;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void PushThroughLock()
	{
		var itemsThreadStatic = _itemsThreadStatic!;
		
		lock (_lockObject)
		{
			if (_itemCountGlobal + 8 > _itemsGlobal.Length)
				ExpandGlobalItems();
			
			for (var i = 16; i-- > 8;)
			{
				ref var itemBucket = ref itemsThreadStatic[i];
				var item = itemBucket;
				itemBucket = default;

				_itemsGlobal[_itemCountGlobal++] = item;
			}
		}
		
		_itemCountThreadStatic = 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T?[] InitializeItemsThreadStatic() => new T?[16];

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ExpandGlobalItems() => Array.Resize(ref _itemsGlobal, _itemsGlobal.Length << 1);
}