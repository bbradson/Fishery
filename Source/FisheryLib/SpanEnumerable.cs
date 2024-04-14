// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// https://github.com/CommunityToolkit/dotnet/blob/main/CommunityToolkit.HighPerformance/Enumerables/SpanEnumerable%7BT%7D.cs

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FisheryLib;

/// <summary>
/// A <see langword="ref"/> <see langword="struct"/> that enumerates the items in a given <see cref="Span{T}"/> instance.
/// </summary>
/// <typeparam name="T">The type of items to enumerate.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
[PublicAPI]
public ref struct SpanEnumerable<T>
{
	/// <summary>
	/// The source <see cref="Span{T}"/> instance.
	/// </summary>
	private readonly Span<T> _span;

	/// <summary>
	/// The current index within <see cref="_span"/>.
	/// </summary>
	private int _index;

	/// <summary>
	/// Initializes a new instance of the <see cref="SpanEnumerable{T}"/> struct.
	/// </summary>
	/// <param name="span">The source <see cref="Span{T}"/> instance.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public SpanEnumerable(Span<T> span)
	{
		_span = span;
		_index = -1;
	}

	/// <summary>
	/// Implements the duck-typed <see cref="IEnumerable{T}.GetEnumerator"/> method.
	/// </summary>
	/// <returns>An <see cref="SpanEnumerable{T}"/> instance targeting the current <see cref="Span{T}"/> value.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly SpanEnumerable<T> GetEnumerator() => this;

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
		// On .NET Standard 2.1 and .NET Core (or on any target that offers runtime
		// support for the Span<T> types), we can save 4 bytes by piggybacking the
		// current index in the length of the wrapped span. We're going to use the
		// first item as the target reference, and the length as a host for the
		// current original offset. This is not possible on eg. .NET Standard 2.0,
		// as we lack the API to create Span<T>-s from arbitrary references.
			=> new(ref Unsafe.Add(ref _span.DangerousGetPinnableReference(), (nint)(uint)_index), _index);
	}

	/// <summary>
	/// An item from a source <see cref="Span{T}"/> instance.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public readonly ref struct Item
	{
		/// <summary>
		/// The source <see cref="Span{T}"/> instance.
		/// </summary>
		private readonly Span<T> _span;

		/// <summary>
		/// Initializes a new instance of the <see cref="Item"/> struct.
		/// </summary>
		/// <param name="value">A reference to the target value.</param>
		/// <param name="index">The index of the target value.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Item(ref T value, int index) => _span = MemoryMarshal.CreateSpan(ref value, index);

		/// <summary>
		/// Gets the reference to the current value.
		/// </summary>
		public ref T Value
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => ref MemoryMarshal.GetReference(_span);
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