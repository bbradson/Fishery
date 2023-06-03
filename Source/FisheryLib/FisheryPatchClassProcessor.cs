// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

/*using System.Linq;

namespace FisheryLib;
/// <summary>A PatchClassProcessor used to turn <see cref="HarmonyAttribute"/> and Fishery attributes on a class/type into patches</summary>
public class FisheryPatchClassProcessor : PatchClassProcessor
{
	new readonly HarmonyMethod? containerAttributes;
	new readonly Dictionary<Type, MethodInfo>? auxilaryMethods;

	new readonly List<AttributePatch>? patchMethods;

	new static readonly List<Type> auxilaryTypes = new()
	{
		typeof(HarmonyPrepare),
		typeof(HarmonyCleanup),
		typeof(HarmonyTargetMethod),
		typeof(HarmonyTargetMethods)
	};

	/// <summary>Creates a patch class processor by pointing out a class. Similar to PatchAll() but without searching through all classes. This version supports attributes added by Fishery.</summary>
	/// <param name="instance">The Harmony instance</param>
	/// <param name="type">The class to process (need to have at least a [HarmonyPatch] attribute)</param>
	public FisheryPatchClassProcessor(Harmony instance, Type type) : base(instance, type)
	{
		Guard.IsNotNull(instance, nameof(instance));
		Guard.IsNotNull(type, nameof(type));
		//this.instance = instance;
		//containerType = type;

		if (HarmonyMethodExtensions.GetFromType(type) is not { Count: not 0 } harmonyAttributes)
			return;

		containerAttributes = HarmonyMethod.Merge(harmonyAttributes);
		containerAttributes.methodType ??= MethodType.Normal; // MethodType default is Normal

		auxilaryMethods = new();
		foreach (var auxType in auxilaryTypes)
		{
			var method = PatchTools.GetPatchMethod(containerType, auxType.FullName);
			if (method is object)
				auxilaryMethods[auxType] = method;
		}

		patchMethods = PatchTools.GetPatchMethods(containerType);
		foreach (var patchMethod in patchMethods)
		{
			var method = patchMethod.info.method;
			patchMethod.info = containerAttributes.Merge(patchMethod.info);
			patchMethod.info.method = method;
		}


		foreach (var attribute in type.GetCustomAttributes())
		{
			if (attribute is InfixAttribute infixAttribute)
			{
				var infixMethod = new InfixMethod(null!, infixAttribute.InfixTarget);
			}
		}
	}

	public static List<AttributePatch> GetPatchMethods(Type type)
	{
		var attributePatches = new List<AttributePatch>();
		var methodInfos = AccessTools.GetDeclaredMethods(type);
		for (var i = 0; i < methodInfos.Count; i++)
		{
			if (AttributePatch.Create(methodInfos[i]) is { } attributePatch)
				attributePatches.Add(attributePatch);
		}

		return attributePatches;
	}

	public static AttributePatch? CreateInfixAttributePatch(MethodInfo patch)
	{
		Guard.IsNotNull(patch, nameof(patch));

		var customAttributes = patch.GetCustomAttributes(inherit: true);
		var patchType = GetInfixPatchType(patch.Name, customAttributes);

		if (patchType is null)
			return null;

		if (patchType != FisheryPatchType.ReversePatch && !patch.IsStatic)
			throw new ArgumentException($"Patch method {patch.FullDescription()} must be static");

		var harmonyAttributes = new List<HarmonyMethod>();
		foreach (var attr in customAttributes)
		{
			if (attr.GetType().BaseType.FullName == AttributePatch.harmonyAttributeName)
			{
				harmonyAttributes.Add(
					AccessTools.MakeDeepCopy<HarmonyMethod>(
						 AccessTools.Field(attr.GetType(), nameof(HarmonyAttribute.info)).GetValue(attr)));
			}
		}

		var harmonyMethod = HarmonyMethod.Merge(harmonyAttributes);
		harmonyMethod.method = patch;
		return new AttributePatch
		{
			info = harmonyMethod,
			type = (HarmonyPatchType)patchType //<--------------------------------------------------------------------------------------------------------------------------------------------
		};
	}

	public static IEnumerable<string> HarmonyFields { get; } = typeof(HarmonyMethod).GetFields(AccessTools.allDeclared).Select(f => f.Name).Where(s => s != "method");

	public static HarmonyMethod Merge(List<HarmonyMethod> attributes)
	{
		var harmonyMethod = new HarmonyMethod();
		if (attributes == null)
		{
			return harmonyMethod;
		}

		var resultTrv = Traverse.Create(harmonyMethod);
		attributes.ForEach(delegate (HarmonyMethod attribute)
		{
			var trv = Traverse.Create(attribute);
			foreach (var f in HarmonyFields)
			{
				var value = trv.Field(f).GetValue();
				if (value != null && (f != "priority" || (int)value != -1))
				{
					SetValue(resultTrv, f, value);
				}
			};
		});
		return harmonyMethod;
	}

	public static void SetValue(Traverse trv, string name, object val)
	{
		if (val != null)
		{
			var traverse = trv.Field(name);
			if (name is "methodType" or "reversePatchType")
			{
				val = Enum.ToObject(Nullable.GetUnderlyingType(traverse.GetValueType()), (int)val);
			}

			traverse.SetValue(val);
		}
	}

	public static FisheryPatchType? GetInfixPatchType(string methodName, object[] allAttributes)
	{
		var hashSet = new HashSet<string>();
		for (var i = 0; i < allAttributes.Length; i++)
		{
			var name = allAttributes[i].GetType().FullName;
			if (StartsWithAny(name, AttributePrefixes))
				hashSet.Add(name.ToUpperInvariant());
		}

		FisheryPatchType? result = null;
		var array = AttributePatch.allPatchTypes;
		for (var i = 0; i < array.Length; i++)
		{
			var value = array[i];
			var text = value.ToString().ToUpperInvariant();

			if (text == methodName.ToUpperInvariant()
				|| hashSet.Overlaps(CombineWithAttributePrefixes(text)))
			{
				result = (FisheryPatchType)value; //<------------------------------------------------------------------------------------------------------------------------------------------
				break;
			}
		}

		return result;
	}

	private static (string Namespace, string Prefix)[] AttributePrefixInfoTuples { get; } = new[] { (nameof(HarmonyLib), "Harmony"), (nameof(FisheryLib), "Fishery"), (nameof(FisheryLib), "Infix") };
	private static string[] AttributePrefixes { get; } = Array.ConvertAll(AttributePrefixInfoTuples, t => t.Prefix);
	private static string[] AttributePrefixesWithNamespace { get; } = Array.ConvertAll(AttributePrefixInfoTuples, t => $"{t.Namespace}.{t.Prefix}");
	private static IEnumerable<string> CombineWithAttributePrefixes(string text)
	{
		foreach (var prefix in AttributePrefixesWithNamespace)
			yield return prefix + text;
	}

	private static bool StartsWithAny(string text, string[] values)
	{
		for (var i = 0; i < values.Length; i++)
		{
			if (text.StartsWith(values[i]))
				return true;
		}
		return false;
	}
}

public enum FisheryPatchType
{
	All,
	Prefix,
	Postfix,
	Infix,
	Transpiler,
	Finalizer,
	ReversePatch
}*/

