// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace FisheryLib;

[PublicAPI]
public static class StopwatchExtensions
{
	public static decimal ElapsedMilliSecondsAccurate(this Stopwatch stopwatch)
		=> stopwatch.ElapsedTicks / (_frequency / 1000m);

	public static decimal ElapsedSecondsAccurate(this Stopwatch stopwatch)
		=> stopwatch.ElapsedTicks / _frequency;

	public static decimal ElapsedMicroSeconds(this Stopwatch stopwatch)
		=> stopwatch.ElapsedTicks / (_frequency / 1e6m);

	public static decimal ElapsedNanoSeconds(this Stopwatch stopwatch)
		=> stopwatch.ElapsedTicks / (_frequency / 1e9m);

	private static decimal _frequency = Stopwatch.Frequency;
}