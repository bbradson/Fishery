// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FisheryLib;

internal static class UtilityF
{
	internal static void ThrowIfNullOrEmpty<T>(IEnumerable<T> sequence, [CallerArgumentExpression(nameof(sequence))] string argumentName = "")
	{
		Guard.IsNotNull(sequence, argumentName);

		if (!sequence.Any())
			ThrowHelper.ThrowArgumentException("Sequence contains no elements", argumentName);
	}

	[DoesNotReturn]
	internal static void Throw(string message) => throw new(message);

	[DoesNotReturn]
	internal static void Throw<T>(string message) where T : Exception => throw (T)Activator.CreateInstance(typeof(T), message);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Assembly GetCallingAssembly()
	{
		Assembly? fishAssembly = null;
		var stacktrace = new StackTrace();

		for (var i = 0; i < stacktrace.FrameCount; i++)
		{
			var assembly = stacktrace.GetFrame(i).GetMethod().ReflectedType.Assembly;
			fishAssembly ??= assembly;

			if (assembly != fishAssembly)
				return assembly;
		}

		return Assembly.GetCallingAssembly();
	}

	/*internal static T New<T>() => Create<T>.@new();

	internal static T New<T, V>(V argument) => Create<T, V>.@new(argument);

	internal static T New<T, V, W>(V firstArgument, W secondArgument) => Create<T, V, W>.@new(firstArgument, secondArgument);

	private static class Create<T>
	{
		internal static Func<T> @new = AccessTools.Constructor(typeof(T)).CreateDelegate<Func<T>>();
	}

	private static class Create<T, V>
	{
		internal static Func<V, T> @new = AccessTools.Constructor(typeof(T), new[] { typeof(V) }).CreateDelegate<Func<V, T>>();
	}

	private static class Create<T, V, W>
	{
		internal static Func<V, W, T> @new = AccessTools.Constructor(typeof(T), new[] { typeof(V), typeof(W) }).CreateDelegate<Func<V, W, T>>();
	}*/
}