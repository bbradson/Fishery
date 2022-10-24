// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

/*namespace FisheryLib;
public static class HarmonyPatchExtensions
{
	/// <summary>Creates patches by manually specifying the methods</summary>
	/// <param name="original">The original method/constructor</param>
	/// <param name="prefix">An optional prefix method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <param name="postfix">An optional postfix method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <param name="infix">An optional infix method wrapped in a <see cref="InfixMethod"/> object</param>
	/// <param name="transpiler">An optional transpiler method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <param name="finalizer">An optional finalizer method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <returns>The replacement method that was created to patch the original method</returns>
	public static MethodInfo Patch(this Harmony harmony, MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, InfixMethod? infix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
	{
		Guard.IsNotNull(harmony, nameof(harmony));
		Guard.IsNotNull(original, nameof(original));

		var patchProcessor = harmony.CreateProcessor(original);
		patchProcessor.AddPrefix(prefix);
		patchProcessor.AddPostfix(postfix);
		patchProcessor.AddTranspiler(infix);
		patchProcessor.AddTranspiler(transpiler);
		patchProcessor.AddFinalizer(finalizer);
		return patchProcessor.Patch();
	}
}*/