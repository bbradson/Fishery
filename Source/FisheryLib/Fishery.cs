// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//#define DEBUG

global using System;
global using System.Collections.Generic;
global using System.Reflection;
global using HarmonyLib;
global using FisheryLib.Utility.Diagnostics;
global using CodeInstructions = System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>;
using System.Security;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: AllowPartiallyTrustedCallers]
[assembly: SecurityRules(SecurityRuleSet.Level2, SkipVerificationInFullTrust = true)]
[assembly: Debuggable(false, false)]

[module: SkipLocalsInit]

namespace FisheryLib;

public static class FisheryLib
{
	public const decimal VERSION = 0.380085M;

	public static decimal CurrentlyLoadedVersion { get; } = VERSION;
}

/*namespace FisheryLib;
public class Fishery : Harmony
{
	/// <summary>Creates a new Fishery instance</summary>
	/// <param name="id">A unique identifier (you choose your own)</param>
	/// <returns>A Fishery instance</returns>
	public Fishery(string id) : base(id)
	{
	}

	/// <summary>Creates a patch class processor from an annotated class</summary>
	/// <param name="type">The class/type</param>
	/// <returns>A new <see cref="PatchClassProcessor"/> instance</returns>
	public new FisheryPatchClassProcessor CreateClassProcessor(Type type) => new(this, type);

	/// <summary>Creates patches by manually specifying the methods</summary>
	/// <param name="original">The original method/constructor</param>
	/// <param name="prefix">An optional prefix method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <param name="postfix">An optional postfix method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <param name="infix">An optional infix method wrapped in a <see cref="InfixMethod"/> object</param>
	/// <param name="transpiler">An optional transpiler method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <param name="finalizer">An optional finalizer method wrapped in a <see cref="HarmonyMethod"/> object</param>
	/// <returns>The replacement method that was created to patch the original method</returns>
	public MethodInfo Patch(MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, InfixMethod? infix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
	{
		Guard.IsNotNull(original, nameof(original));

		return HarmonyPatchExtensions.Patch(this, original, prefix, postfix, infix, transpiler, finalizer);
	}

	/// <summary>Searches the current assembly for Harmony and Fishery annotations, using them to create patches</summary>
	/// <remarks>This method can fail to use the correct assembly when being inlined. It calls StackTrace.GetFrame(1) which can point to the wrong method/assembly. If you are unsure or run into problems, use <code>PatchAll(Assembly.GetExecutingAssembly())</code> instead.</remarks>
	public new void PatchAll() => PatchAll(UtilityF.GetCallingAssembly());

	/// <summary>Searches an assembly for Harmony annotations and uses them to create patches</summary>
	/// <param name="assembly">The assembly</param>
	public new void PatchAll(Assembly assembly)
	{
		Guard.IsNotNull(assembly, nameof(assembly));

		foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
			CreateClassProcessor(type).Patch();
	}
}*/