// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the MIT license.
// If a copy of the license was not distributed with this file,
// You can obtain one at https://opensource.org/licenses/MIT/.

using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace FisheryLib;

[PublicAPI]
public static class GUIScope
{
	public readonly record struct WidgetGroup : IDisposable
	{
		public WidgetGroup(in Rect rect)
#if !V1_2
			=> Widgets.BeginGroup(rect);
#else
			=> GUI.BeginGroup(rect);
#endif

		public void Dispose()
#if !V1_2
			=> Widgets.EndGroup();
#else
			=> GUI.EndGroup();
#endif
	}
	
	public readonly record struct ScrollView : IDisposable
	{
		private const float SCROLL_BAR_WIDTH = 16f;
		
		private readonly ScrollViewStatus _scrollViewStatus;

		private readonly float _outRectHeight;

		public readonly Vector2 ViewSize;
		
		public ref float Height
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => ref _scrollViewStatus.Height;
		}

		public Vector2 Position
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _scrollViewStatus.Position;
		}

		public ScrollView(in Rect outRect, ScrollViewStatus scrollViewStatus, bool showScrollbars = true)
		{
			_scrollViewStatus = scrollViewStatus;
			_outRectHeight = outRect.height;
			var viewRect = new Rect(0f, 0f, outRect.width, Math.Max(Height, _outRectHeight));
			if (Height - 0.1f >= outRect.height)
				viewRect.width -= SCROLL_BAR_WIDTH;

			ViewSize = viewRect.size;
			Height = 0f;
			Widgets.BeginScrollView(outRect, ref _scrollViewStatus.Position, viewRect, showScrollbars);
		}

		public void Dispose() => Widgets.EndScrollView();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool CanCull(float entryHeight, float entryY)
			=> entryY + entryHeight < Position.y
				|| entryY > Position.y + _outRectHeight;
	}

	public class ScrollViewStatus
	{
		public Vector2 Position;
		public float Height;
	}
	
	public readonly record struct ListingStandard : IDisposable
	{
		public Listing_Standard Listing
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get;
		}

		public ListingStandard(in Rect rect, Listing_Standard? listing = null)
			=> (Listing = listing ?? new()).Begin(rect);

		public void Dispose() => Listing.End();
	}
	
	public readonly record struct ScrollableListingStandard : IDisposable
	{
		private readonly ListingStandard _listingStandard;

		private readonly ScrollView _scrollView;

		public Listing_Standard Listing
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _listingStandard.Listing;
		}

		public Vector2 ScrollViewSize
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _scrollView.ViewSize;
		}

		public Vector2 ScrollViewPosition
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _scrollView.Position;
		}

		public ScrollableListingStandard(Rect outRect, ScrollViewStatus scrollViewStatus,
			Listing_Standard? listing = null, bool showScrollbars = true)
		{
			_scrollView = new(outRect, scrollViewStatus, showScrollbars);
			_listingStandard = new(new(0f, 0f, ScrollViewSize.x, float.PositiveInfinity), listing);
		}

		public void Dispose()
		{
			_scrollView.Height = Listing.CurHeight + 12f;
			_listingStandard.Dispose();
			_scrollView.Dispose();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryCull(float entryHeight)
		{
			var canCull = _scrollView.CanCull(entryHeight, Listing.curY);
			if (canCull)
				Listing.curY += entryHeight;

			return canCull;
		}
	}
	
	public readonly record struct TextAnchor : IDisposable
	{
		private readonly UnityEngine.TextAnchor _default;
	
		public TextAnchor(UnityEngine.TextAnchor anchor)
		{
			_default = Text.Anchor;
			Text.Anchor = anchor;
		}
	
		public void Dispose() => Text.Anchor = _default;
	}

	public readonly record struct WordWrap : IDisposable
	{
		private readonly bool _default;
	
		public WordWrap(bool wordWrap)
		{
			_default = Text.WordWrap;
			Text.WordWrap = wordWrap;
		}
	
		public void Dispose() => Text.WordWrap = _default;
	}

	public readonly record struct Color : IDisposable
	{
		private readonly UnityEngine.Color _default;
	
		public Color(UnityEngine.Color color)
		{
			_default = GUI.color;
			GUI.color = color;
		}
	
		public void Dispose() => GUI.color = _default;
	}
	
	public readonly record struct Font : IDisposable
	{
		private readonly GameFont _default;
	
		public Font(GameFont font)
		{
			_default = Text.Font;
			Text.Font = font;
		}
	
		public void Dispose() => Text.Font = _default;
	}
	
	public readonly record struct FontSize : IDisposable
	{
		private readonly int _default;
	
		public FontSize(int size)
		{
			var curFontStyle = Text.CurFontStyle;
			
			_default = curFontStyle.fontSize;
			curFontStyle.fontSize = size;
		}
	
		public void Dispose() => Text.CurFontStyle.fontSize = _default;
	}
}