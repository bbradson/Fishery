using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FisheryLib;
#region ArraySortHelper for single arrays

internal sealed class ArraySortHelper<T> : IArraySortHelper<T>
{
	public static IArraySortHelper<T> Default { get; } = CreateArraySortHelper();

	private static IArraySortHelper<T> CreateArraySortHelper()
		=> typeof(IComparable<T>).IsAssignableFrom(typeof(T))
		? (IArraySortHelper<T>)Activator.CreateInstance(typeof(GenericArraySortHelper<>).MakeGenericType(typeof(T)))
		: new ArraySortHelper<T>();

	#region IArraySortHelper<T> Members

	public void Sort(Span<T> keys, IComparer<T>? comparer)
	{
		// Add a try block here to detect IComparers (or their
		// underlying IComparables, etc) that are bogus.
		try
		{
			comparer ??= Comparer<T>.Default;
			IntrospectiveSort(keys, comparer.Compare);
		}
		catch (IndexOutOfRangeException)
		{
			SortUtils.ThrowArgumentException_BadComparer(comparer);
		}
		catch (Exception e)
		{
			ThrowHelper.ThrowInvalidOperationException(SortUtils.InvalidOperation_IComparerFailed, e);
		}
	}

	#endregion

	public static void Sort(Span<T> keys, Comparison<T> comparer)
	{
		Debug.Assert(comparer != null, "Check the arguments in the caller!");

		// Add a try block here to detect bogus comparisons
		try
		{
			IntrospectiveSort(keys, comparer!);
		}
		catch (IndexOutOfRangeException)
		{
			SortUtils.ThrowArgumentException_BadComparer(comparer);
		}
		catch (Exception e)
		{
			ThrowHelper.ThrowInvalidOperationException(SortUtils.InvalidOperation_IComparerFailed, e);
		}
	}

	private static void SwapIfGreater(Span<T> keys, Comparison<T> comparer, int i, int j)
	{
		Debug.Assert(i != j);

		if (comparer(keys[i], keys[j]) > 0)
		{
			(keys[j], keys[i]) = (keys[i], keys[j]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap(Span<T> a, int i, int j)
	{
		Debug.Assert(i != j);

		(a[j], a[i]) = (a[i], a[j]);
	}

	public static void IntrospectiveSort(Span<T> keys, Comparison<T> comparer)
	{
		Debug.Assert(comparer != null);

		if (keys.Length > 1)
		{
			IntroSort(keys, 2 * (BitOperations.Log2((uint)keys.Length) + 1), comparer!);
		}
	}

	private static void IntroSort(Span<T> keys, int depthLimit, Comparison<T> comparer)
	{
		Debug.Assert(!keys.IsEmpty);
		Debug.Assert(depthLimit >= 0);
		Debug.Assert(comparer != null);

		var partitionSize = keys.Length;
		while (partitionSize > 1)
		{
			if (partitionSize <= SortUtils.INTROSORT_SIZE_THRESHOLD)
			{

				if (partitionSize == 2)
				{
					SwapIfGreater(keys, comparer!, 0, 1);
					return;
				}

				if (partitionSize == 3)
				{
					SwapIfGreater(keys, comparer!, 0, 1);
					SwapIfGreater(keys, comparer!, 0, 2);
					SwapIfGreater(keys, comparer!, 1, 2);
					return;
				}

				InsertionSort(keys[..partitionSize], comparer!);
				return;
			}

			if (depthLimit == 0)
			{
				HeapSort(keys[..partitionSize], comparer!);
				return;
			}
			depthLimit--;

			var p = PickPivotAndPartition(keys[..partitionSize], comparer!);

			// Note we've already partitioned around the pivot and do not have to move the pivot again.
			IntroSort(keys[(p + 1)..partitionSize], depthLimit, comparer!);
			partitionSize = p;
		}
	}

	private static int PickPivotAndPartition(Span<T> keys, Comparison<T> comparer)
	{
		Debug.Assert(keys.Length >= SortUtils.INTROSORT_SIZE_THRESHOLD);
		Debug.Assert(comparer != null);

		var hi = keys.Length - 1;

		// Compute median-of-three.  But also partition them, since we've done the comparison.
		var middle = hi >> 1;

		// Sort lo, mid and hi appropriately, then pick mid as the pivot.
		SwapIfGreater(keys, comparer!, 0, middle);  // swap the low with the mid point
		SwapIfGreater(keys, comparer!, 0, hi);   // swap the low with the high
		SwapIfGreater(keys, comparer!, middle, hi); // swap the middle with the high

		var pivot = keys[middle];
		Swap(keys, middle, hi - 1);
		int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

		while (left < right)
		{
			while (comparer!(keys[++left], pivot) < 0)
				;
			while (comparer(pivot, keys[--right]) < 0)
				;

			if (left >= right)
				break;

			Swap(keys, left, right);
		}

		// Put pivot in the right location.
		if (left != hi - 1)
		{
			Swap(keys, left, hi - 1);
		}
		return left;
	}

	private static void HeapSort(Span<T> keys, Comparison<T> comparer)
	{
		Debug.Assert(comparer != null);
		Debug.Assert(!keys.IsEmpty);

		var n = keys.Length;
		for (var i = n >> 1; i >= 1; i--)
		{
			DownHeap(keys, i, n, comparer!);
		}

		for (var i = n; i > 1; i--)
		{
			Swap(keys, 0, i - 1);
			DownHeap(keys, 1, i - 1, comparer!);
		}
	}

	private static void DownHeap(Span<T> keys, int i, int n, Comparison<T> comparer)
	{
		Debug.Assert(comparer != null);

		var d = keys[i - 1];
		while (i <= n >> 1)
		{
			var child = 2 * i;
			if (child < n && comparer!(keys[child - 1], keys[child]) < 0)
			{
				child++;
			}

			if (!(comparer!(d, keys[child - 1]) < 0))
				break;

			keys[i - 1] = keys[child - 1];
			i = child;
		}

		keys[i - 1] = d;
	}

	private static void InsertionSort(Span<T> keys, Comparison<T> comparer)
	{
		for (var i = 0; i < keys.Length - 1; i++)
		{
			var t = keys[i + 1];

			var j = i;
			while (j >= 0 && comparer(t, keys[j]) < 0)
			{
				keys[j + 1] = keys[j];
				j--;
			}

			keys[j + 1] = t;
		}
	}
}

internal sealed class GenericArraySortHelper<T> : IArraySortHelper<T>
	where T : IComparable<T>
{
	// Do not add a constructor to this class because ArraySortHelper<T>.CreateSortHelper will not execute it

	#region IArraySortHelper<T> Members

	public void Sort(Span<T> keys, IComparer<T>? comparer)
	{
		try
		{
			if (comparer == null || comparer == Comparer<T>.Default)
			{
				if (keys.Length > 1)
				{
					// For floating-point, do a pre-pass to move all NaNs to the beginning
					// so that we can do an optimized comparison as part of the actual sort
					// on the remainder of the values.
					if (typeof(T) == typeof(double) ||
						typeof(T) == typeof(float))
					{
						var nanLeft = SortUtils.MoveNansToFront(keys, default(Span<byte>));
						if (nanLeft == keys.Length)
						{
							return;
						}
						keys = keys[nanLeft..];
					}

					IntroSort(keys, 2 * (BitOperations.Log2((uint)keys.Length) + 1));
				}
			}
			else
			{
				ArraySortHelper<T>.IntrospectiveSort(keys, comparer.Compare);
			}
		}
		catch (IndexOutOfRangeException)
		{
			SortUtils.ThrowArgumentException_BadComparer(comparer);
		}
		catch (Exception e)
		{
			ThrowHelper.ThrowInvalidOperationException(SortUtils.InvalidOperation_IComparerFailed, e);
		}
	}

	#endregion

	/// <summary>Swaps the values in the two references if the first is greater than the second.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SwapIfGreater(ref T i, ref T j)
	{
		if (i != null && GreaterThan(ref i, ref j))
		{
			Swap(ref i, ref j);
		}
	}

	/// <summary>Swaps the values in the two references, regardless of whether the two references are the same.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap(ref T i, ref T j)
	{
		Debug.Assert(!Unsafe.AreSame(ref i, ref j));

		(j, i) = (i, j);
	}

	private static void IntroSort(Span<T> keys, int depthLimit)
	{
		Debug.Assert(!keys.IsEmpty);
		Debug.Assert(depthLimit >= 0);

		var partitionSize = keys.Length;
		while (partitionSize > 1)
		{
			if (partitionSize <= SortUtils.INTROSORT_SIZE_THRESHOLD)
			{
				if (partitionSize == 2)
				{
					SwapIfGreater(ref keys[0], ref keys[1]);
					return;
				}

				if (partitionSize == 3)
				{
					ref var hiRef = ref keys[2];
					ref var him1Ref = ref keys[1];
					ref var loRef = ref keys[0];

					SwapIfGreater(ref loRef, ref him1Ref);
					SwapIfGreater(ref loRef, ref hiRef);
					SwapIfGreater(ref him1Ref, ref hiRef);
					return;
				}

				InsertionSort(keys[..partitionSize]);
				return;
			}

			if (depthLimit == 0)
			{
				HeapSort(keys[..partitionSize]);
				return;
			}
			depthLimit--;

			var p = PickPivotAndPartition(keys[..partitionSize]);

			// Note we've already partitioned around the pivot and do not have to move the pivot again.
			IntroSort(keys[(p + 1)..partitionSize], depthLimit);
			partitionSize = p;
		}
	}

	private static int PickPivotAndPartition(Span<T> keys)
	{
		Debug.Assert(keys.Length >= SortUtils.INTROSORT_SIZE_THRESHOLD);

		// Use median-of-three to select a pivot. Grab a reference to the 0th, Length-1th, and Length/2th elements, and sort them.
		ref var zeroRef = ref MemoryMarshal.GetReference(keys);
		ref var lastRef = ref Unsafe.Add(ref zeroRef, keys.Length - 1);
		ref var middleRef = ref Unsafe.Add(ref zeroRef, (keys.Length - 1) >> 1);
		SwapIfGreater(ref zeroRef, ref middleRef);
		SwapIfGreater(ref zeroRef, ref lastRef);
		SwapIfGreater(ref middleRef, ref lastRef);

		// Select the middle value as the pivot, and move it to be just before the last element.
		ref var nextToLastRef = ref Unsafe.Add(ref zeroRef, keys.Length - 2);
		var pivot = middleRef;
		Swap(ref middleRef, ref nextToLastRef);

		// Walk the left and right pointers, swapping elements as necessary, until they cross.
		ref T leftRef = ref zeroRef!, rightRef = ref nextToLastRef!;
		while (Unsafe.IsAddressLessThan(ref leftRef, ref rightRef))
		{
			if (pivot == null)
			{
				while (Unsafe.IsAddressLessThan(ref leftRef!, ref nextToLastRef) && (leftRef = ref Unsafe.Add(ref leftRef, 1)) == null)
					;
				while (Unsafe.IsAddressGreaterThan(ref rightRef, ref zeroRef) && (rightRef = ref Unsafe.Add(ref rightRef, -1)) != null)
					;
			}
			else
			{
				while (Unsafe.IsAddressLessThan(ref leftRef, ref nextToLastRef) && GreaterThan(ref pivot, ref leftRef = ref Unsafe.Add(ref leftRef, 1)))
					;
				while (Unsafe.IsAddressGreaterThan(ref rightRef, ref zeroRef) && LessThan(ref pivot, ref rightRef = ref Unsafe.Add(ref rightRef, -1)))
					;
			}

			if (!Unsafe.IsAddressLessThan(ref leftRef, ref rightRef!))
			{
				break;
			}

			Swap(ref leftRef, ref rightRef);
		}

		// Put the pivot in the correct location.
		if (!Unsafe.AreSame(ref leftRef, ref nextToLastRef))
		{
			Swap(ref leftRef, ref nextToLastRef);
		}
		return (int)((nint)Unsafe.ByteOffset(ref zeroRef, ref leftRef) / Unsafe.SizeOf<T>());
	}

	private static void HeapSort(Span<T> keys)
	{
		Debug.Assert(!keys.IsEmpty);

		var n = keys.Length;
		for (var i = n >> 1; i >= 1; i--)
		{
			DownHeap(keys, i, n);
		}

		for (var i = n; i > 1; i--)
		{
			Swap(ref keys[0], ref keys[i - 1]);
			DownHeap(keys, 1, i - 1);
		}
	}

	private static void DownHeap(Span<T> keys, int i, int n)
	{
		var d = keys[i - 1];
		while (i <= n >> 1)
		{
			var child = 2 * i;
			if (child < n && (keys[child - 1] == null || LessThan(ref keys[child - 1], ref keys[child])))
			{
				child++;
			}

			if (keys[child - 1] == null || !LessThan(ref d, ref keys[child - 1]))
				break;

			keys[i - 1] = keys[child - 1];
			i = child;
		}

		keys[i - 1] = d;
	}

	private static void InsertionSort(Span<T> keys)
	{
		for (var i = 0; i < keys.Length - 1; i++)
		{
			var t = Unsafe.Add(ref MemoryMarshal.GetReference(keys), i + 1);

			var j = i;
			while (j >= 0 && (t == null || LessThan(ref t, ref Unsafe.Add(ref MemoryMarshal.GetReference(keys), j))))
			{
				Unsafe.Add(ref MemoryMarshal.GetReference(keys), j + 1) = Unsafe.Add(ref MemoryMarshal.GetReference(keys), j);
				j--;
			}

			Unsafe.Add(ref MemoryMarshal.GetReference(keys), j + 1) = t!;
		}
	}

	// - These methods exist for use in sorting, where the additional operations present in
	//   the CompareTo methods that would otherwise be used on these primitives add non-trivial overhead,
	//   in particular for floating point where the CompareTo methods need to factor in NaNs.
	// - The floating-point comparisons here assume no NaNs, which is valid only because the sorting routines
	//   themselves special-case NaN with a pre-pass that ensures none are present in the values being sorted
	//   by moving them all to the front first and then sorting the rest.
	// - These are duplicated here rather than being on a helper type due to current limitations around generic inlining.

	[MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
	private static bool LessThan(ref T left, ref T right)
		=> typeof(T) == typeof(byte)
		? (byte)(object)left < (byte)(object)right
		: typeof(T) == typeof(sbyte)
		? (sbyte)(object)left < (sbyte)(object)right
		: typeof(T) == typeof(ushort)
		? (ushort)(object)left < (ushort)(object)right
		: typeof(T) == typeof(short)
		? (short)(object)left < (short)(object)right
		: typeof(T) == typeof(uint)
		? (uint)(object)left < (uint)(object)right
		: typeof(T) == typeof(int)
		? (int)(object)left < (int)(object)right
		: typeof(T) == typeof(ulong)
		? (ulong)(object)left < (ulong)(object)right
		: typeof(T) == typeof(long)
		? (long)(object)left < (long)(object)right
		: typeof(T) == typeof(nuint)
		? (nuint)(object)left < (nuint)(object)right
		: typeof(T) == typeof(nint)
		? (nint)(object)left < (nint)(object)right
		: typeof(T) == typeof(float)
		? (float)(object)left < (float)(object)right
		: typeof(T) == typeof(double)
		? (double)(object)left < (double)(object)right
		: left.CompareTo(right) < 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
	private static bool GreaterThan(ref T left, ref T right)
		=> typeof(T) == typeof(byte)
		? (byte)(object)left > (byte)(object)right
		: typeof(T) == typeof(sbyte)
		? (sbyte)(object)left > (sbyte)(object)right
		: typeof(T) == typeof(ushort)
		? (ushort)(object)left > (ushort)(object)right
		: typeof(T) == typeof(short)
		? (short)(object)left > (short)(object)right
		: typeof(T) == typeof(uint)
		? (uint)(object)left > (uint)(object)right
		: typeof(T) == typeof(int)
		? (int)(object)left > (int)(object)right
		: typeof(T) == typeof(ulong)
		? (ulong)(object)left > (ulong)(object)right
		: typeof(T) == typeof(long)
		? (long)(object)left > (long)(object)right
		: typeof(T) == typeof(nuint)
		? (nuint)(object)left > (nuint)(object)right
		: typeof(T) == typeof(nint)
		? (nint)(object)left > (nint)(object)right
		: typeof(T) == typeof(float)
		? (float)(object)left > (float)(object)right
		: typeof(T) == typeof(double)
		? (double)(object)left > (double)(object)right
		: left.CompareTo(right) > 0;
}

#endregion

#region ArraySortHelper for paired key and value arrays

internal sealed class ArraySortHelper<TKey, TValue> : IArraySortHelper<TKey, TValue>
{
	public static IArraySortHelper<TKey, TValue> Default { get; } = CreateArraySortHelper();

	private static IArraySortHelper<TKey, TValue> CreateArraySortHelper()
		=> typeof(IComparable<TKey>).IsAssignableFrom(typeof(TKey))
		? (IArraySortHelper<TKey, TValue>)Activator.CreateInstance(typeof(GenericArraySortHelper<,>).MakeGenericType(typeof(TKey), typeof(TValue)))
		: new ArraySortHelper<TKey, TValue>();

	public void Sort(Span<TKey> keys, Span<TValue> values, IComparer<TKey>? comparer)
	{
		// Add a try block here to detect IComparers (or their
		// underlying IComparables, etc) that are bogus.
		try
		{
			IntrospectiveSort(keys, values, comparer ?? Comparer<TKey>.Default);
		}
		catch (IndexOutOfRangeException)
		{
			SortUtils.ThrowArgumentException_BadComparer(comparer);
		}
		catch (Exception e)
		{
			ThrowHelper.ThrowInvalidOperationException(SortUtils.InvalidOperation_IComparerFailed, e);
		}
	}

	private static void SwapIfGreaterWithValues(Span<TKey> keys, Span<TValue> values, IComparer<TKey> comparer, int i, int j)
	{
		Debug.Assert(comparer != null);
		Debug.Assert(0 <= i && i < keys.Length && i < values.Length);
		Debug.Assert(0 <= j && j < keys.Length && j < values.Length);
		Debug.Assert(i != j);

		if (comparer!.Compare(keys[i], keys[j]) > 0)
		{
			(keys[j], keys[i]) = (keys[i], keys[j]);

			(values[j], values[i]) = (values[i], values[j]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap(Span<TKey> keys, Span<TValue> values, int i, int j)
	{
		Debug.Assert(i != j);

		(keys[j], keys[i]) = (keys[i], keys[j]);

		(values[j], values[i]) = (values[i], values[j]);
	}

	public static void IntrospectiveSort(Span<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
	{
		Debug.Assert(comparer != null);
		Debug.Assert(keys.Length == values.Length);

		if (keys.Length > 1)
		{
			IntroSort(keys, values, 2 * (BitOperations.Log2((uint)keys.Length) + 1), comparer!);
		}
	}

	public static void IntroSort(Span<TKey> keys, Span<TValue> values, int depthLimit, IComparer<TKey> comparer)
	{
		Debug.Assert(!keys.IsEmpty);
		Debug.Assert(values.Length == keys.Length);
		Debug.Assert(depthLimit >= 0);
		Debug.Assert(comparer != null);

		var partitionSize = keys.Length;
		while (partitionSize > 1)
		{
			if (partitionSize <= SortUtils.INTROSORT_SIZE_THRESHOLD)
			{

				if (partitionSize == 2)
				{
					SwapIfGreaterWithValues(keys, values, comparer!, 0, 1);
					return;
				}

				if (partitionSize == 3)
				{
					SwapIfGreaterWithValues(keys, values, comparer!, 0, 1);
					SwapIfGreaterWithValues(keys, values, comparer!, 0, 2);
					SwapIfGreaterWithValues(keys, values, comparer!, 1, 2);
					return;
				}

				InsertionSort(keys[..partitionSize], values[..partitionSize], comparer!);
				return;
			}

			if (depthLimit == 0)
			{
				HeapSort(keys[..partitionSize], values[..partitionSize], comparer!);
				return;
			}
			depthLimit--;

			var p = PickPivotAndPartition(keys[..partitionSize], values[..partitionSize], comparer!);

			// Note we've already partitioned around the pivot and do not have to move the pivot again.
			IntroSort(keys[(p + 1)..partitionSize], values[(p + 1)..partitionSize], depthLimit, comparer!);
			partitionSize = p;
		}
	}

	private static int PickPivotAndPartition(Span<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
	{
		Debug.Assert(keys.Length >= SortUtils.INTROSORT_SIZE_THRESHOLD);
		Debug.Assert(comparer != null);

		var hi = keys.Length - 1;

		// Compute median-of-three.  But also partition them, since we've done the comparison.
		var middle = hi >> 1;

		// Sort lo, mid and hi appropriately, then pick mid as the pivot.
		SwapIfGreaterWithValues(keys, values, comparer!, 0, middle);  // swap the low with the mid point
		SwapIfGreaterWithValues(keys, values, comparer!, 0, hi);   // swap the low with the high
		SwapIfGreaterWithValues(keys, values, comparer!, middle, hi); // swap the middle with the high

		var pivot = keys[middle];
		Swap(keys, values, middle, hi - 1);
		int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

		while (left < right)
		{
			while (comparer!.Compare(keys[++left], pivot) < 0)
				;
			while (comparer.Compare(pivot, keys[--right]) < 0)
				;

			if (left >= right)
				break;

			Swap(keys, values, left, right);
		}

		// Put pivot in the right location.
		if (left != hi - 1)
		{
			Swap(keys, values, left, hi - 1);
		}
		return left;
	}

	private static void HeapSort(Span<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
	{
		Debug.Assert(comparer != null);
		Debug.Assert(!keys.IsEmpty);

		var n = keys.Length;
		for (var i = n >> 1; i >= 1; i--)
		{
			DownHeap(keys, values, i, n, comparer!);
		}

		for (var i = n; i > 1; i--)
		{
			Swap(keys, values, 0, i - 1);
			DownHeap(keys, values, 1, i - 1, comparer!);
		}
	}

	private static void DownHeap(Span<TKey> keys, Span<TValue> values, int i, int n, IComparer<TKey> comparer)
	{
		Debug.Assert(comparer != null);

		var d = keys[i - 1];
		var dValue = values[i - 1];

		while (i <= n >> 1)
		{
			var child = 2 * i;
			if (child < n && comparer!.Compare(keys[child - 1], keys[child]) < 0)
			{
				child++;
			}

			if (!(comparer!.Compare(d, keys[child - 1]) < 0))
				break;

			keys[i - 1] = keys[child - 1];
			values[i - 1] = values[child - 1];
			i = child;
		}

		keys[i - 1] = d;
		values[i - 1] = dValue;
	}

	private static void InsertionSort(Span<TKey> keys, Span<TValue> values, IComparer<TKey> comparer)
	{
		Debug.Assert(comparer != null);

		for (var i = 0; i < keys.Length - 1; i++)
		{
			var t = keys[i + 1];
			var tValue = values[i + 1];

			var j = i;
			while (j >= 0 && comparer!.Compare(t, keys[j]) < 0)
			{
				keys[j + 1] = keys[j];
				values[j + 1] = values[j];
				j--;
			}

			keys[j + 1] = t;
			values[j + 1] = tValue;
		}
	}
}

internal sealed class GenericArraySortHelper<TKey, TValue> : IArraySortHelper<TKey, TValue>
	where TKey : IComparable<TKey>
{
	public void Sort(Span<TKey> keys, Span<TValue> values, IComparer<TKey>? comparer)
	{
		// Add a try block here to detect IComparers (or their
		// underlying IComparables, etc) that are bogus.
		try
		{
			if (comparer == null || comparer == Comparer<TKey>.Default)
			{
				if (keys.Length > 1)
				{
					// For floating-point, do a pre-pass to move all NaNs to the beginning
					// so that we can do an optimized comparison as part of the actual sort
					// on the remainder of the values.
					if (typeof(TKey) == typeof(double) ||
						typeof(TKey) == typeof(float))
					{
						var nanLeft = SortUtils.MoveNansToFront(keys, values);
						if (nanLeft == keys.Length)
						{
							return;
						}
						keys = keys[nanLeft..];
						values = values[nanLeft..];
					}

					IntroSort(keys, values, 2 * (BitOperations.Log2((uint)keys.Length) + 1));
				}
			}
			else
			{
				ArraySortHelper<TKey, TValue>.IntrospectiveSort(keys, values, comparer);
			}
		}
		catch (IndexOutOfRangeException)
		{
			SortUtils.ThrowArgumentException_BadComparer(comparer);
		}
		catch (Exception e)
		{
			ThrowHelper.ThrowInvalidOperationException(SortUtils.InvalidOperation_IComparerFailed, e);
		}
	}

	private static void SwapIfGreaterWithValues(Span<TKey> keys, Span<TValue> values, int i, int j)
	{
		Debug.Assert(i != j);

		ref var keyRef = ref keys[i];
		if (keyRef != null && GreaterThan(ref keyRef, ref keys[j]))
		{
			var key = keyRef;
			keys[i] = keys[j];
			keys[j] = key;

			(values[j], values[i]) = (values[i], values[j]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap(Span<TKey> keys, Span<TValue> values, int i, int j)
	{
		Debug.Assert(i != j);

		(keys[j], keys[i]) = (keys[i], keys[j]);

		(values[j], values[i]) = (values[i], values[j]);
	}

	private static void IntroSort(Span<TKey> keys, Span<TValue> values, int depthLimit)
	{
		Debug.Assert(!keys.IsEmpty);
		Debug.Assert(values.Length == keys.Length);
		Debug.Assert(depthLimit >= 0);

		var partitionSize = keys.Length;
		while (partitionSize > 1)
		{
			if (partitionSize <= SortUtils.INTROSORT_SIZE_THRESHOLD)
			{

				if (partitionSize == 2)
				{
					SwapIfGreaterWithValues(keys, values, 0, 1);
					return;
				}

				if (partitionSize == 3)
				{
					SwapIfGreaterWithValues(keys, values, 0, 1);
					SwapIfGreaterWithValues(keys, values, 0, 2);
					SwapIfGreaterWithValues(keys, values, 1, 2);
					return;
				}

				InsertionSort(keys[..partitionSize], values[..partitionSize]);
				return;
			}

			if (depthLimit == 0)
			{
				HeapSort(keys[..partitionSize], values[..partitionSize]);
				return;
			}
			depthLimit--;

			var p = PickPivotAndPartition(keys[..partitionSize], values[..partitionSize]);

			// Note we've already partitioned around the pivot and do not have to move the pivot again.
			IntroSort(keys[(p + 1)..partitionSize], values[(p + 1)..partitionSize], depthLimit);
			partitionSize = p;
		}
	}

	private static int PickPivotAndPartition(Span<TKey> keys, Span<TValue> values)
	{
		Debug.Assert(keys.Length >= SortUtils.INTROSORT_SIZE_THRESHOLD);

		var hi = keys.Length - 1;

		// Compute median-of-three.  But also partition them, since we've done the comparison.
		var middle = hi >> 1;

		// Sort lo, mid and hi appropriately, then pick mid as the pivot.
		SwapIfGreaterWithValues(keys, values, 0, middle);  // swap the low with the mid point
		SwapIfGreaterWithValues(keys, values, 0, hi);   // swap the low with the high
		SwapIfGreaterWithValues(keys, values, middle, hi); // swap the middle with the high

		var pivot = keys[middle];
		Swap(keys, values, middle, hi - 1);
		int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

		while (left < right)
		{
			if (pivot == null)
			{
				while (left < (hi - 1) && keys[++left] == null)
					;
				while (right > 0 && keys[--right] != null)
					;
			}
			else
			{
				while (GreaterThan(ref pivot, ref keys[++left]))
					;
				while (LessThan(ref pivot, ref keys[--right]))
					;
			}

			if (left >= right)
				break;

			Swap(keys, values, left, right);
		}

		// Put pivot in the right location.
		if (left != hi - 1)
		{
			Swap(keys, values, left, hi - 1);
		}
		return left;
	}

	private static void HeapSort(Span<TKey> keys, Span<TValue> values)
	{
		Debug.Assert(!keys.IsEmpty);

		var n = keys.Length;
		for (var i = n >> 1; i >= 1; i--)
		{
			DownHeap(keys, values, i, n);
		}

		for (var i = n; i > 1; i--)
		{
			Swap(keys, values, 0, i - 1);
			DownHeap(keys, values, 1, i - 1);
		}
	}

	private static void DownHeap(Span<TKey> keys, Span<TValue> values, int i, int n)
	{
		var d = keys[i - 1];
		var dValue = values[i - 1];

		while (i <= n >> 1)
		{
			var child = 2 * i;
			if (child < n && (keys[child - 1] == null || LessThan(ref keys[child - 1], ref keys[child])))
			{
				child++;
			}

			if (keys[child - 1] == null || !LessThan(ref d, ref keys[child - 1]))
				break;

			keys[i - 1] = keys[child - 1];
			values[i - 1] = values[child - 1];
			i = child;
		}

		keys[i - 1] = d;
		values[i - 1] = dValue;
	}

	private static void InsertionSort(Span<TKey> keys, Span<TValue> values)
	{
		for (var i = 0; i < keys.Length - 1; i++)
		{
			var t = keys[i + 1];
			var tValue = values[i + 1];

			var j = i;
			while (j >= 0 && (t == null || LessThan(ref t, ref keys[j])))
			{
				keys[j + 1] = keys[j];
				values[j + 1] = values[j];
				j--;
			}

			keys[j + 1] = t!;
			values[j + 1] = tValue;
		}
	}

	// - These methods exist for use in sorting, where the additional operations present in
	//   the CompareTo methods that would otherwise be used on these primitives add non-trivial overhead,
	//   in particular for floating point where the CompareTo methods need to factor in NaNs.
	// - The floating-point comparisons here assume no NaNs, which is valid only because the sorting routines
	//   themselves special-case NaN with a pre-pass that ensures none are present in the values being sorted
	//   by moving them all to the front first and then sorting the rest.
	// - These are duplicated here rather than being on a helper type due to current limitations around generic inlining.

	[MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
	private static bool LessThan(ref TKey left, ref TKey right)
		=> typeof(TKey) == typeof(byte)
		? (byte)(object)left < (byte)(object)right
		: typeof(TKey) == typeof(sbyte)
		? (sbyte)(object)left < (sbyte)(object)right
		: typeof(TKey) == typeof(ushort)
		? (ushort)(object)left < (ushort)(object)right
		: typeof(TKey) == typeof(short)
		? (short)(object)left < (short)(object)right
		: typeof(TKey) == typeof(uint)
		? (uint)(object)left < (uint)(object)right
		: typeof(TKey) == typeof(int)
		? (int)(object)left < (int)(object)right
		: typeof(TKey) == typeof(ulong)
		? (ulong)(object)left < (ulong)(object)right
		: typeof(TKey) == typeof(long)
		? (long)(object)left < (long)(object)right
		: typeof(TKey) == typeof(nuint)
		? (nuint)(object)left < (nuint)(object)right
		: typeof(TKey) == typeof(nint)
		? (nint)(object)left < (nint)(object)right
		: typeof(TKey) == typeof(float)
		? (float)(object)left < (float)(object)right
		: typeof(TKey) == typeof(double)
		? (double)(object)left < (double)(object)right
		: left.CompareTo(right) < 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] // compiles to a single comparison or method call
	private static bool GreaterThan(ref TKey left, ref TKey right)
		=> typeof(TKey) == typeof(byte)
		? (byte)(object)left > (byte)(object)right
		: typeof(TKey) == typeof(sbyte)
		? (sbyte)(object)left > (sbyte)(object)right
		: typeof(TKey) == typeof(ushort)
		? (ushort)(object)left > (ushort)(object)right
		: typeof(TKey) == typeof(short)
		? (short)(object)left > (short)(object)right
		: typeof(TKey) == typeof(uint)
		? (uint)(object)left > (uint)(object)right
		: typeof(TKey) == typeof(int)
		? (int)(object)left > (int)(object)right
		: typeof(TKey) == typeof(ulong)
		? (ulong)(object)left > (ulong)(object)right
		: typeof(TKey) == typeof(long)
		? (long)(object)left > (long)(object)right
		: typeof(TKey) == typeof(nuint)
		? (nuint)(object)left > (nuint)(object)right
		: typeof(TKey) == typeof(nint)
		? (nint)(object)left > (nint)(object)right
		: typeof(TKey) == typeof(float)
		? (float)(object)left > (float)(object)right
		: typeof(TKey) == typeof(double)
		? (double)(object)left > (double)(object)right
		: left.CompareTo(right) > 0;
}

#endregion

/// <summary>Helper methods for use in array/span sorting routines.</summary>
internal static class SortUtils
{
	public static int MoveNansToFront<TKey, TValue>(Span<TKey> keys, Span<TValue> values) where TKey : notnull
	{
		Debug.Assert(typeof(TKey) == typeof(double) || typeof(TKey) == typeof(float));

		var left = 0;

		for (var i = 0; i < keys.Length; i++)
		{
			if ((typeof(TKey) == typeof(double) && double.IsNaN((double)(object)keys[i])) ||
				(typeof(TKey) == typeof(float) && float.IsNaN((float)(object)keys[i])))
			{
				(keys[i], keys[left]) = (keys[left], keys[i]);

				if ((uint)i < (uint)values.Length) // check to see if we have values
				{
					(values[i], values[left]) = (values[left], values[i]);
				}

				left++;
			}
		}

		return left;
	}

	// This is the threshold where Introspective sort switches to Insertion sort.
	// Empirically, 16 seems to speed up most cases without slowing down others, at least for integers.
	// Large value types may benefit from a smaller number.
	internal const int INTROSORT_SIZE_THRESHOLD = 16;

	[DoesNotReturn]
	internal static void ThrowArgumentException_BadComparer(object? comparer)
		=> throw new ArgumentException(string.Format(Arg_BogusIComparer, comparer));

	internal static string InvalidOperation_IComparerFailed => "Failed to compare two elements in the array.";
	internal static string Arg_BogusIComparer
		=> "Unable to sort because the IComparer.Compare() method returns inconsistent results. Either a value does not compare equal to itself, or one value repeatedly compared to another value yields different results. IComparer: '{0}'.";
}

internal interface IArraySortHelper<TKey, TValue>
{
	void Sort(Span<TKey> keys, Span<TValue> values, IComparer<TKey>? comparer);
}

internal interface IArraySortHelper<TKey>
{
	void Sort(Span<TKey> keys, IComparer<TKey>? comparer);
}

internal static class BitOperations
{
	private static ReadOnlySpan<byte> Log2DeBruijn
		=> new byte[32]
		{
			00, 09, 01, 10, 13, 21, 02, 29,
			11, 14, 16, 18, 22, 25, 03, 30,
			08, 12, 20, 28, 15, 17, 24, 07,
			19, 27, 23, 06, 26, 05, 04, 31
		};

	/// <summary>
	/// Returns the integer (floor) log of the specified value, base 2.
	/// Note that by convention, input value 0 returns 0 since log(0) is undefined.
	/// </summary>
	/// <param name="value">The value.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	//[CLSCompliant(false)]
	public static int Log2(uint value)
	{
		// The 0->0 contract is fulfilled by setting the LSB to 1.
		// Log(1) is 0, and setting the LSB for values > 1 does not change the log2 result.
		value |= 1;

#if probablyNotSupportedOnUnity
		// value    lzcnt   actual  expected
		// ..0001   31      31-31    0
		// ..0010   30      31-30    1
		// 0010..    2      31-2    29
		// 0100..    1      31-1    30
		// 1000..    0      31-0    31
		if (Lzcnt.IsSupported)
		{
			return 31 ^ (int)Lzcnt.LeadingZeroCount(value);
		}

		if (ArmBase.IsSupported)
		{
			return 31 ^ ArmBase.LeadingZeroCount(value);
		}

		// BSR returns the log2 result directly. However BSR is slower than LZCNT
		// on AMD processors, so we leave it as a fallback only.
		if (X86Base.IsSupported)
		{
			return (int)X86Base.BitScanReverse(value);
		}
#endif

		// Fallback contract is 0->0
		return Log2SoftwareFallback(value);
	}

	/// <summary>
	/// Returns the integer (floor) log of the specified value, base 2.
	/// Note that by convention, input value 0 returns 0 since Log(0) is undefined.
	/// Does not directly use any hardware intrinsics, nor does it incur branching.
	/// </summary>
	/// <param name="value">The value.</param>
	private static int Log2SoftwareFallback(uint value)
	{
		// No AggressiveInlining due to large method size
		// Has conventional contract 0->0 (Log(0) is undefined)

		// Fill trailing zeros with ones, eg 00010010 becomes 00011111
		value |= value >> 01;
		value |= value >> 02;
		value |= value >> 04;
		value |= value >> 08;
		value |= value >> 16;

		// uint.MaxValue >> 27 is always in range [0 - 31] so we use Unsafe.AddByteOffset to avoid bounds check
		return Unsafe.AddByteOffset(
			// Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
			ref MemoryMarshal.GetReference(Log2DeBruijn),
			// uint|long -> IntPtr cast on 32-bit platforms does expensive overflow checks not needed here
			(IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
	}
}