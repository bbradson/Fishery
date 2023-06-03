// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

global using static FisheryLib.Aliases;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace FisheryLib;

[PublicAPI]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "alias")]
public static class Aliases
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	// ReSharper disable once InconsistentNaming
	public static MethodInfo methodof(Delegate method) => method.Method;
}