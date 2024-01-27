// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace FisheryLib;

internal static class UtilityF
{
	internal static void ThrowIfNullOrEmpty<T>([NoEnumeration] IEnumerable<T> sequence,
		[CallerArgumentExpression(nameof(sequence))] string argumentName = "")
	{
		Guard.IsNotNull(sequence, argumentName);

		if (!sequence.Any())
			ThrowHelper.ThrowArgumentException("Sequence contains no elements", argumentName);
	}

	[DoesNotReturn]
	[SuppressMessage("Usage", "CA2201")]
	internal static void Throw(string message) => throw new(message);

	[DoesNotReturn]
	internal static void Throw<T>(string message) where T : Exception
		=> throw (T)Activator.CreateInstance(typeof(T), message);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Assembly GetCallingAssembly()
	{
		Assembly? fishAssembly = null;
		var stacktrace = new StackTrace();

		for (var i = 0; i < stacktrace.FrameCount; i++)
		{
			var assembly = stacktrace.GetFrame(i).GetMethod().ReflectedType?.Assembly;
			fishAssembly ??= assembly;

			if (assembly != fishAssembly && assembly != null)
				return assembly;
		}

		return Assembly.GetCallingAssembly();
	}
}