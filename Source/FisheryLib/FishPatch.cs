// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

/*using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using Verse;

namespace FisheryLib;

internal class FishPatchManager
{
	internal const string FISHERY = "fishery";

	internal FishPatchManager(Assembly assembly)
	{
		Assembly = assembly;
		ModContentPack = LoadedModManager.RunningModsListForReading.Find(pack => pack.assemblies.loadedAssemblies.Contains(assembly));
		ModName = ModContentPack?.Name ?? assembly.GetName().Name;
		ModPackageID = ModContentPack?.PackageIdPlayerFacing;
		Harmony = new($"{FISHERY}.{ModPackageID ?? ModName.ToLowerInvariant()}");

	}

	public Assembly Assembly { get; }
	public ModContentPack? ModContentPack { get; }
	public string? ModPackageID { get; }
	public string ModName { get; }
	public Harmony Harmony { get; }
}

public abstract class FishPatch : IExposable, IHasDescription
{
	public string ModName = "Fishery";
	public Harmony Harmony { get; } = new("bs.fishery");
	public void PatchAll()
	{
		foreach (var patch in AllPatchClasses)
		{
			try
			{
				if (!patch.RequiresLoadedGameForPatching)
					patch.Patches.PatchAll();
			}
			catch (Exception ex)
			{
				Log.Error($"{ModName} encountered an exception while patching:\n{ex}");
			}
		}
	}
	public bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			if (value)
			{
				if (ShouldBePatched)
					TryPatch();
			}
			else
			{
				var shouldBePatched = ShouldBePatched;
				TryUnpatch();
				ShouldBePatched = shouldBePatched;
			}
		}
	}
	private bool _enabled = true;
	public virtual bool DefaultState => true;
	public virtual int PrefixMethodPriority => TryGetPriority(PrefixMethodInfo);
	public virtual int PostfixMethodPriority => TryGetPriority(PostfixMethodInfo);
	public virtual int TranspilerMethodPriority => TryGetPriority(TranspilerMethodInfo);
	public virtual int FinalizerMethodPriority => TryGetPriority(FinalizerMethodInfo);
	public MethodInfo? HarmonyMethodInfo { get; private set; }
	private bool Patched { get; set; }
	private bool ShouldBePatched { get; set; }

	public virtual string Name => GetType().Name;
	public virtual string? Description => null;
	public virtual bool ShowSettings => true;

	public virtual Delegate? TargetMethodGroup => null;
	public virtual IEnumerable<Delegate>? TargetMethodGroups => null;
	public virtual Expression<Action>? TargetMethod => null;
	public virtual IEnumerable<Expression<Action>>? TargetMethods => null;
	public virtual MethodBase TargetMethodInfo => TargetMethod != null ? SymbolExtensions.GetMethodInfo(TargetMethod)
		: TargetMethodGroup?.Method ?? throw new MissingMethodException(GetType().ToString());
	public virtual IEnumerable<MethodBase> TargetMethodInfos => TargetMethods?.Select(m => SymbolExtensions.GetMethodInfo(m))
		?? TargetMethodGroups?.Select(m => m.Method) ?? (TargetMethodInfo != null ? new MethodBase[] { TargetMethodInfo }.AsEnumerable() : throw new MissingMethodException(GetType().ToString()));
	public virtual MethodInfo ReversePatchMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "REVERSEPATCH" || (m.HasAttribute<HarmonyReversePatch>() && !m.HasAttribute<HarmonyTranspiler>()));
	public virtual MethodInfo ReversePatchTranspilerMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "REVERSEPATCHTRANSPILER" || (m.HasAttribute<HarmonyTranspiler>() && m.HasAttribute<HarmonyReversePatch>()));
	public virtual MethodInfo PrefixMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "PREFIX" || m.HasAttribute<HarmonyPrefix>());
	public virtual MethodInfo PostfixMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "POSTFIX" || m.HasAttribute<HarmonyPostfix>());
	public virtual MethodInfo TranspilerMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "TRANSPILER" || (m.HasAttribute<HarmonyTranspiler>() && !m.HasAttribute<HarmonyReversePatch>()));
	public virtual MethodInfo FinalizerMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "FINALIZER" || m.HasAttribute<HarmonyFinalizer>());
	public virtual MethodInfo PrepareMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "PREPARE" || m.HasAttribute<HarmonyPrepare>());
	public virtual MethodInfo CleanupMethodInfo => GetType().GetMethods(AccessTools.allDeclared)
		.FirstOrDefault(m => m.Name.ToUpperInvariant() == "CLEANUP" || m.HasAttribute<HarmonyCleanup>());

	public FishPatch() => _enabled = DefaultState;

	public virtual MethodInfo? TryPatch()
	{
		if (TargetMethodInfos is null || !TargetMethodInfos.Any())
		{
			Log.Error($"Tried to apply patches for {GetType().Name}, but it lacks a target method for patching.");
			return null;
		}
		if (PrefixMethodInfo is null && PostfixMethodInfo is null && TranspilerMethodInfo is null && FinalizerMethodInfo is null && ReversePatchMethodInfo is null)
		{
			Log.Error($"Tried to apply patches for {GetType().Name}, but there are none. This is likely not intended.");
			return null;
		}

		ShouldBePatched = true;
		if (Enabled && !Patched)
		{
			Patched = true;

			PrepareMethodInfo?.Invoke(null, null);
			foreach (var method in TargetMethodInfos)
			{
				try
				{
					if (ReversePatchMethodInfo != null)
						HarmonyMethodInfo = Harmony.ReversePatch(method, new(ReversePatchMethodInfo), ReversePatchTranspilerMethodInfo);

					if (PrefixMethodInfo != null || PostfixMethodInfo != null || TranspilerMethodInfo != null || FinalizerMethodInfo != null)
					{
						HarmonyMethodInfo = Harmony.Patch(method,
							prefix: PrefixMethodInfo != null ? new(PrefixMethodInfo, PrefixMethodPriority) : null,
							postfix: PostfixMethodInfo != null ? new(PostfixMethodInfo, PostfixMethodPriority) : null,
							transpiler: TranspilerMethodInfo != null ? new(TranspilerMethodInfo, TranspilerMethodPriority) : null,
							finalizer: FinalizerMethodInfo != null ? new(FinalizerMethodInfo, FinalizerMethodPriority) : null);
					}
				}
				catch (Exception e)
				{
					Log.Error($"{ModName} encountered an exception while trying to patch {method.FullDescription()} with {PrefixMethodInfo.FullDescription()}" +
						$", {PostfixMethodInfo.FullDescription()}, {TranspilerMethodInfo.FullDescription()}, {FinalizerMethodInfo.FullDescription()}:\n{e}");
				}

#if DEBUG
				Log.Message($"{ModName} applied {GetType().Name} on {method.FullDescription()}");
#endif
			}
		}
		return HarmonyMethodInfo;
	}

	public virtual void TryUnpatch()
	{
		ShouldBePatched = false;
		if (Patched)
		{
			Patched = false;
			foreach (var method in TargetMethodInfos)
			{
				if (PrefixMethodInfo != null)
				{
					try
					{
						Harmony.Unpatch(method, PrefixMethodInfo);
					}
					catch (Exception e)
					{
						Log.Error($"{ModName} encountered an exception when unpatching {PrefixMethodInfo.FullDescription()} from {method.FullDescription()}:\n{e}");
					}
				}
				if (PostfixMethodInfo != null)
				{
					try
					{
						Harmony.Unpatch(method, PostfixMethodInfo);
					}
					catch (Exception e)
					{
						Log.Error($"{ModName} encountered an exception when unpatching {PostfixMethodInfo.FullDescription()} from {method.FullDescription()}:\n{e}");
					}
				}
				if (TranspilerMethodInfo != null)
				{
					try
					{
						Harmony.Unpatch(method, TranspilerMethodInfo);
					}
					catch (Exception e)
					{
						Log.Error($"{ModName} encountered an exception when unpatching {TranspilerMethodInfo.FullDescription()} from {method.FullDescription()}:\n{e}");
					}
				}
				if (FinalizerMethodInfo != null)
				{
					try
					{
						Harmony.Unpatch(method, FinalizerMethodInfo);
					}
					catch (Exception e)
					{
						Log.Error($"{ModName} encountered an exception when unpatching {FinalizerMethodInfo.FullDescription()} from {method.FullDescription()}:\n{e}");
					}
				}
			}
			CleanupMethodInfo?.Invoke(null, null);
		}
	}

	private static int TryGetPriority(MethodInfo info) => info?.TryGetAttribute<HarmonyPriority>()?.info.priority ?? Priority.Normal;

	public static T Get<T>() => TryGet<T>() ?? throw new KeyNotFoundException($"Couldn't find FishPatch or IHasFishPatch class {typeof(T).Name}");
	public static T? TryGet<T>()
	{
		if (typeof(IHasFishPatch).IsAssignableFrom(typeof(T)))
		{
			foreach (var patch in AllPatchClasses)
			{
				if (patch is T patchOfT)
					return patchOfT;
			}
		}
		else if (typeof(FishPatch).IsAssignableFrom(typeof(T)))
		{
			foreach (var patchClass in AllPatchClasses)
			{
				if (patchClass.Patches.TryGet(typeof(T)) is T patch)
					return patch;
			}
		}

		return default;
	}
	public static IHasFishPatch[] AllPatchClasses { get; } = Assembly.GetExecutingAssembly().GetTypes()
		.Where(t => typeof(IHasFishPatch).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
		.Select(t => (IHasFishPatch)Activator.CreateInstance(t)).ToArray();
	public void ExposeData() => Scribe_Values.Look(ref _enabled, GetType().Name, DefaultState);
}

public abstract class FirstPriorityFishPatch : FishPatch
{
	public override int PrefixMethodPriority => Priority.First;
	public override int PostfixMethodPriority => Priority.First;
	public override int TranspilerMethodPriority => Priority.First;
	public override int FinalizerMethodPriority => Priority.First;
}

public static class FishPatchExtensions
{
	public static void PatchAll(this IEnumerable<FishPatch> patches)
	{
		foreach (var patch in patches)
			patch.TryPatch();
	}
	public static void UnpatchAll(this IEnumerable<FishPatch> patches)
	{
		foreach (var patch in patches)
			patch.TryUnpatch();
	}
}

public class FishPatchHolder : IExposable, IEnumerable<FishPatch>
{
	public Dictionary<Type, FishPatch> All => _all;
	public T Get<T>() where T : FishPatch => (T)All[typeof(T)];
	public FishPatch Get(Type type) => All[type];
	public T? TryGet<T>() where T : FishPatch => All!.TryGetValue(typeof(T)) as T;
	public FishPatch? TryGet(Type type) => All!.TryGetValue(type);
	public void Add(FishPatch patch) => All[patch.GetType()] = patch;
	public void PatchAll()
	{
		AddPatchesRecursively(_type);
		All.Values.PatchAll();
	}
	public void UnpatchAll() => All.Values.UnpatchAll();
	public FishPatch this[Type type] => All[type];

	public FishPatchHolder(Type type) => _type = type;

	public void ExposeData() => Scribe_Collections.Look(ref _all, "patches", valueLookMode: LookMode.Deep);

	private void AddPatchesRecursively(Type type)
	{
		if (typeof(FishPatch).IsAssignableFrom(type) && !All.ContainsKey(type))
		{
			var dupeClasses = FishPatch.AllPatchClasses.Where(patchClass => patchClass.GetType() != _type && patchClass.Patches.All.ContainsKey(type));
			if (dupeClasses.Any())
			{
				foreach (var patchClass in dupeClasses)
				{
					patchClass.Patches[type].Enabled = false;
					patchClass.Patches.All.Remove(type);
					Log.Warning($"{FishPatchManager.FISHERY} removed a duplicate patch from {patchClass.GetType().FullName}. This is likely caused by no longer valid mod configs");
				}
			}

			All.Add(type, (FishPatch)Activator.CreateInstance(type));
		}

		foreach (var nestedType in type.GetNestedTypes(AccessTools.all))
			AddPatchesRecursively(nestedType);
	}

	public IEnumerator<FishPatch> GetEnumerator() => All.Values.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => All.Values.GetEnumerator();

	private Dictionary<Type, FishPatch> _all = new();
	private Type _type;
}

public interface IHasFishPatch
{
	public FishPatchHolder Patches { get; }
	public FishPatchHolder? PatchHolder { get; set; }
	public bool RequiresLoadedGameForPatching { get; }
}

public interface IHasDescription
{
	public string? Description { get; }
}

public abstract class ClassWithFishPatches : IHasFishPatch
{
	public virtual FishPatchHolder Patches => _patchHolder ??= new(GetType());
	public virtual FishPatchHolder? PatchHolder { get => _patchHolder; set => _patchHolder = value; }
	public virtual bool RequiresLoadedGameForPatching => false;
	private FishPatchHolder? _patchHolder;
}*/