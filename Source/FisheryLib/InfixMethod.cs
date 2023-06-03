// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

/*using System.Linq;

namespace FisheryLib;
public class InfixMethod : HarmonyMethod
{
	public InfixMethod(MethodInfo method, InfixTarget target)
	{
		Guard.IsNotNull(method, nameof(method));

		ImportMethod(method, target);
	}

	private void ImportMethod(MethodInfo theMethod, InfixTarget target)
	{
		method = MakeInfixMethod(theMethod, target).Method;
		if (method is not null
			&& HarmonyMethodExtensions.GetFromMethod(method) is { } fromMethod)
		{
			Merge(fromMethod).CopyTo(this);
		}
	}
	private static InfixDelegate MakeInfixMethod(MethodInfo theMethod, InfixTarget target)
		=> (codes, original)
			=> target.Mode switch
			{
				InfixTargetMode.After => codes.InsertAfter(target.Predicate, GetInfixInstructions(original, theMethod)),
				InfixTargetMode.Replace => codes.Replace(target.Predicate, code => FishTranspiler.Call(theMethod)), // <-- this one is different
				InfixTargetMode.Before => codes.InsertBefore(target.Predicate, GetInfixInstructions(original, theMethod)),
				_ => throw new($"Invalid InfixTargetMode for {theMethod.FullDescription()}")
			};

	private static IEnumerable<CodeInstruction> GetInfixInstructions(MethodBase original, MethodInfo infixMethod)
	{
		var originalParameters = original.GetParameters();
		foreach (var argument in infixMethod.GetParameters())
		{
			var argumentName = argument.Name;
			var argumentType = argument.ParameterType;

			if (argumentName == "__instance")
			{
				yield return FishTranspiler.This;
			}
			else if (argumentName == "__args")
			{
				foreach (var code in GetArgsArray(originalParameters))
					yield return code;
			}
			else if (argumentName == "__originalMethod")
			{
				foreach (var code in GetOriginalMethod(original))
					yield return code;
			}
			else if (argumentName is "__result")
			{
				throw new NotSupportedException("Infixes do not support __result.");
			}
			else if (argumentName is "__state")
			{
				throw new NotSupportedException("Infixes do not support __state.");
			}
			//argument
			else if (original.GetParameters().Any(parm => parm.Name == argumentName))
			{
				yield return FishTranspiler.Argument(original, argumentName);
			}
			//___field
			else if (argumentName.StartsWith("___"))
			{
				yield return FishTranspiler.Field(argumentType, argumentName.Remove(0, 3));
			}
			//vars
			else if (argumentName.StartsWith("__"))
			{
				yield return GetVarInstruction(original, argumentName, argumentType);
			}
		}

		yield return FishTranspiler.Call(infixMethod);
	}

	public static IEnumerable<CodeInstruction> GetOriginalMethod(MethodBase original)
	{
		Guard.IsNotNull(original, nameof(original));

		if (original is MethodInfo or ConstructorInfo)
		{
			yield return FishTranspiler.Token(original);

			if (original.ReflectedType is { IsGenericType: true } type)
			{
				yield return FishTranspiler.Token(type);
				yield return FishTranspiler.Call<Func<RuntimeMethodHandle, RuntimeTypeHandle, MethodBase>>(MethodBase.GetMethodFromHandle);
			}
			else
			{
				yield return FishTranspiler.Call<Func<RuntimeMethodHandle, MethodBase>>(MethodBase.GetMethodFromHandle);
			}
		}
		else
		{
			yield return FishTranspiler.Null;
		}
	}

	public static IEnumerable<CodeInstruction> GetArgsArray(ParameterInfo[] originalParameters)
	{
		Guard.IsNotNull(originalParameters, nameof(originalParameters));

		yield return FishTranspiler.Constant(originalParameters.Length);
		yield return FishTranspiler.New<object[]>();

		for (var i = 0; i < originalParameters.Length; i++)
		{
			var parameter = originalParameters[i];

			yield return FishTranspiler.Duplicate;
			yield return FishTranspiler.Constant(i);
			yield return FishTranspiler.Argument(parameter);

			var parameterType = parameter.ParameterType;
			if (parameterType.IsByRef)
			{
				parameterType = parameterType.GetElementType();
				yield return FishTranspiler.LoadByRef(parameterType);
			}

			if (parameterType.IsValueType)
				yield return FishTranspiler.Box(parameterType);

			yield return FishTranspiler.StoreElement<object>();
		}
	}

	public static CodeInstruction GetVarInstruction(MethodBase original, string argumentName, Type argumentType)
	{
		Guard.IsNotNull(original, nameof(original));
		Guard.IsNotNullOrEmpty(argumentName, nameof(argumentName));
		Guard.IsNotNull(argumentType, nameof(argumentType));

		//__var
		return argumentName == "__var"
		? FishTranspiler.FirstLocalVariable(original, argumentType)

		//__var#
		: argumentName.Contains("__var") && int.TryParse(argumentName.Trim('_', 'v', 'a', 'r'), out var index)
		? FishTranspiler.LocalVariable(index)

		//__*#
		: int.TryParse(new(argumentName.Where(char.IsDigit).ToArray()), out index)
		? FishTranspiler.MatchingLocalVariables(original, loc => loc.LocalType == argumentType).ElementAt(index)

		//__*
		: FishTranspiler.FirstLocalVariable(original, argumentType);
	}

	//public static HarmonyMethod Merge(List<HarmonyMethod> attributes)
	//{
	//	var harmonyMethod = new HarmonyMethod();

	//	if (attributes == null)
	//		return harmonyMethod;

	//	var resultTrv = Traverse.Create(harmonyMethod);
	//	attributes.ForEach((HarmonyMethod attribute) =>
	//	{
	//		var trv = Traverse.Create(attribute);
	//		HarmonyFields().ForEach(delegate (string f)
	//		{
	//			var value = trv.Field(f).GetValue();
	//			if (value != null && (f != "priority" || (int)value != -1))
	//			{
	//				HarmonyMethodExtensions.SetValue(resultTrv, f, value);
	//			}
	//		});
	//	});
	//	return harmonyMethod;
	//}

	/// <summary>
	/// hide overridden HarmonyMethod ctors as they're insufficient for infixes
	/// </summary>
#pragma warning disable IDE0051, IDE0060
	private InfixMethod(MethodInfo method) { }
	private InfixMethod(MethodInfo method, int priority, string[] before, string[] after, bool? debug) { }
	private InfixMethod(Type methodType, string methodName, Type[] argumentTypes) { }
#pragma warning restore IDE0051, IDE0060
}

public delegate IEnumerable<CodeInstruction> InfixDelegate(IEnumerable<CodeInstruction> codes, MethodBase original);

public struct InfixTarget
{
	public Predicate<CodeInstruction> Predicate { get; }
	public InfixTargetMode Mode { get; }

	public InfixTarget(MethodInfo calledMethod, InfixTargetMode targetMode)
	{
		Guard.IsNotNull(calledMethod, nameof(calledMethod));

		Predicate = code => code.Calls(calledMethod);
		Mode = targetMode;
	}

	public InfixTarget(FieldInfo loadedField, InfixTargetMode targetMode)
	{
		Guard.IsNotNull(loadedField, nameof(loadedField));

		Predicate = code => code.LoadsField(loadedField);
		Mode = targetMode;
	}

	public InfixTarget(long constantValue, InfixTargetMode targetMode)
	{
		Predicate = code => code.LoadsConstant(constantValue);
		Mode = targetMode;
	}

	public InfixTarget(double constantValue, InfixTargetMode targetMode)
	{
		Predicate = code => code.LoadsConstant(constantValue);
		Mode = targetMode;
	}

	public InfixTarget(Enum constantValue, InfixTargetMode targetMode)
	{
		Predicate = code => code.LoadsConstant(constantValue);
		Mode = targetMode;
	}

	public InfixTarget(Predicate<CodeInstruction> predicate, InfixTargetMode targetMode)
	{
		Guard.IsNotNull(predicate, nameof(predicate));

		Predicate = predicate;
		Mode = targetMode;
	}
}

public enum InfixTargetMode
{
	After,
	Replace,
	Before
}

public abstract class InfixAttribute : Attribute
{
	public InfixTarget InfixTarget { get; }

	public InfixAttribute(InfixTarget target) => InfixTarget = target;
}

public class InfixBeforeAttribute : InfixAttribute
{
	public InfixBeforeAttribute(MethodInfo methodCall)
		: base(new(NotNull(methodCall), InfixTargetMode.Before))
	{ }

	public InfixBeforeAttribute(FieldInfo fieldToLoad)
		: base(new(NotNull(fieldToLoad), InfixTargetMode.Before))
	{ }

	public InfixBeforeAttribute(long constantValue)
		: base(new(constantValue, InfixTargetMode.Before))
	{ }

	public InfixBeforeAttribute(double constantValue)
		: base(new(constantValue, InfixTargetMode.Before))
	{ }

	public InfixBeforeAttribute(Enum constantValue)
		: base(new(constantValue, InfixTargetMode.Before))
	{ }

	public InfixBeforeAttribute(Predicate<CodeInstruction> predicate)
		: base(new(NotNull(predicate), InfixTargetMode.Before))
	{ }
}

public class InfixAfterAttribute : InfixAttribute
{
	public InfixAfterAttribute(MethodInfo methodCall)
		: base(new(NotNull(methodCall), InfixTargetMode.After))
	{ }

	public InfixAfterAttribute(FieldInfo fieldToLoad)
		: base(new(NotNull(fieldToLoad), InfixTargetMode.After))
	{ }

	public InfixAfterAttribute(long constantValue)
		: base(new(constantValue, InfixTargetMode.After))
	{ }

	public InfixAfterAttribute(double constantValue)
		: base(new(constantValue, InfixTargetMode.After))
	{ }

	public InfixAfterAttribute(Enum constantValue)
		: base(new(constantValue, InfixTargetMode.After))
	{ }

	public InfixAfterAttribute(Predicate<CodeInstruction> predicate)
		: base(new(NotNull(predicate), InfixTargetMode.After))
	{ }
}

public class InfixReplaceAttribute : InfixAttribute
{
	public InfixReplaceAttribute(MethodInfo methodCall)
		: base(new(NotNull(methodCall), InfixTargetMode.Replace))
	{ }

	public InfixReplaceAttribute(FieldInfo fieldToLoad)
		: base(new(NotNull(fieldToLoad), InfixTargetMode.Replace))
	{ }

	public InfixReplaceAttribute(long constantValue)
		: base(new(constantValue, InfixTargetMode.Replace))
	{ }

	public InfixReplaceAttribute(double constantValue)
		: base(new(constantValue, InfixTargetMode.Replace))
	{ }

	public InfixReplaceAttribute(Enum constantValue)
		: base(new(constantValue, InfixTargetMode.Replace))
	{ }

	public InfixReplaceAttribute(Predicate<CodeInstruction> predicate)
		: base(new(NotNull(predicate), InfixTargetMode.Replace))
	{ }
}*/

