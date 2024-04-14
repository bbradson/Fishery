// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// https://github.com/CommunityToolkit/dotnet/blob/main/CommunityToolkit.HighPerformance/Enumerables/ReadOnlySpanEnumerable%7BT%7D.cs

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FisheryLib;

/// <summary>
/// A <see langword="ref"/> <see langword="struct"/> that enumerates the items in a given <see cref="ReadOnlySpan{T}"/> instance.
/// </summary>
/// <typeparam name="T">The type of items to enumerate.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
[PublicAPI]
public ref struct ReadOnlySpanEnumerable<T>
{
	/// <summary>
	/// The source <see cref="ReadOnlySpan{T}"/> instance.
	/// </summary>
	private readonly ReadOnlySpan<T> _span;

	/// <summary>
	/// The current index within <see cref="_span"/>.
	/// </summary>
	private int _index;

	/// <summary>
	/// Initializes a new instance of the <see cref="ReadOnlySpanEnumerable{T}"/> struct.
	/// </summary>
	/// <param name="span">The source <see cref="ReadOnlySpan{T}"/> instance.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpanEnumerable(ReadOnlySpan<T> span)
	{
		_span = span;
		_index = -1;
	}

	/// <summary>
	/// Implements the duck-typed <see cref="IEnumerable{T}.GetEnumerator"/> method.
	/// </summary>
	/// <returns>An <see cref="ReadOnlySpanEnumerable{T}"/> instance targeting the current <see cref="ReadOnlySpan{T}"/> value.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly ReadOnlySpanEnumerable<T> GetEnumerator() => this;

	/// <summary>
	/// Implements the duck-typed <see cref="System.Collections.IEnumerator.MoveNext"/> method.
	/// </summary>
	/// <returns><see langword="true"/> whether a new element is available, <see langword="false"/> otherwise</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool MoveNext() => ++_index < _span.Length;

	/// <summary>
	/// Gets the duck-typed <see cref="IEnumerator{T}.Current"/> property.
	/// </summary>
	public readonly Item Current
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		// See comment in SpanEnumerable<T> about this
			=> new(ref Unsafe.Add(ref _span.DangerousGetPinnableReference(), (nint)(uint)_index), _index);
	}

	/// <summary>
	/// An item from a source <see cref="Span{T}"/> instance.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public readonly ref struct Item
	{
		/// <summary>
		/// The source <see cref="ReadOnlySpan{T}"/> instance.
		/// </summary>
		private readonly ReadOnlySpan<T> _span;

		/// <summary>
		/// Initializes a new instance of the <see cref="Item"/> struct.
		/// </summary>
		/// <param name="value">A reference to the target value.</param>
		/// <param name="index">The index of the target value.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Item(ref T value, int index) => _span = MemoryMarshal.CreateReadOnlySpan(ref value, index);

		/// <summary>
		/// Gets the reference to the current value.
		/// </summary>
		public ref readonly T Value
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => ref _span.DangerousGetPinnableReference();
		}

		/// <summary>
		/// Gets the current index.
		/// </summary>
		public int Index
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _span.Length;
		}
	}
}