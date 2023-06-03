// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Verse;
using CallSite = Mono.Cecil.CallSite;

namespace FisheryLib.Cecil;

public static class CecilExtensions
{
	public static MethodReference ImportReference(this ModuleDefinition module, Delegate method)
		=> module.ImportReference(method.Method);
	
	public static MethodReference MakeGenericMethod<T>(this MethodReference genericMethodDefinition,
		ICollection<T> genericArguments) where T : TypeReference
	{
		if (genericArguments.Count != genericMethodDefinition.GenericParameters.Count)
			ThrowForInvalidGenericArgumentCount(genericMethodDefinition, genericArguments);
		
		var genericInstanceMethod = new GenericInstanceMethod(genericMethodDefinition);
		foreach (var genericArgument in genericArguments)
			genericInstanceMethod.GenericArguments.Add(genericArgument);

		return genericInstanceMethod;
	}

	private static void ThrowForInvalidGenericArgumentCount<T>(IGenericParameterProvider genericParameterProvider,
		ICollection<T> genericArguments) where T : TypeReference
		=> throw new ArgumentException(
			$"Invalid generic argument count for {genericParameterProvider}. Received {
				genericArguments.Count}, must be {genericParameterProvider.GenericParameters.Count}");

	public static MethodReference MakeGenericMethod<T>(this MethodReference genericMethodDefinition,
		params T[] genericArguments) where T : TypeReference
		=> MakeGenericMethod(genericMethodDefinition, (ICollection<T>)genericArguments);

	public static MethodReference ImportMethodWithGenericArguments<T>(this ModuleDefinition module, MethodInfo method,
		ICollection<T> genericArguments) where T : TypeReference
		=> module.ImportReference(method.IsGenericMethodDefinition ? method : method.GetGenericMethodDefinition())
			.MakeGenericMethod(genericArguments);

	public static MethodReference ImportMethodWithGenericArguments<T>(this ModuleDefinition module, MethodInfo method,
		params T[] genericArguments) where T : TypeReference
		=> module.ImportMethodWithGenericArguments(method, (ICollection<T>)genericArguments);

	public static MethodReference ImportMethodWithGenericArguments<T>(this ModuleDefinition module, Delegate method,
		ICollection<T> genericArguments) where T : TypeReference
		=> module.ImportMethodWithGenericArguments(method.Method, genericArguments);

	public static MethodReference ImportMethodWithGenericArguments<T>(this ModuleDefinition module, Delegate method,
		params T[] genericArguments) where T : TypeReference
		=> module.ImportMethodWithGenericArguments(method.Method, (ICollection<T>)genericArguments);
	
	public static TypeReference MakeGenericType<T>(this TypeReference genericTypeDefinition,
		ICollection<T> genericArguments) where T : TypeReference
	{
		if (genericArguments.Count != genericTypeDefinition.GenericParameters.Count)
			ThrowForInvalidGenericArgumentCount(genericTypeDefinition, genericArguments);
		
		var genericInstanceType = new GenericInstanceType(genericTypeDefinition);
		foreach (var genericArgument in genericArguments)
			genericInstanceType.GenericArguments.Add(genericArgument);

		return genericInstanceType;
	}

	public static TypeReference MakeGenericType<T>(this TypeReference genericTypeDefinition,
		params T[] genericArguments) where T : TypeReference
			=> MakeGenericType(genericTypeDefinition, (ICollection<T>)genericArguments);

	public static TypeReference ImportTypeWithGenericArguments<T>(this ModuleDefinition module, Type type,
		ICollection<T> genericArguments) where T : TypeReference
		=> module.ImportReference(type.IsGenericTypeDefinition ? type : type.GetElementType())
			.MakeGenericType(genericArguments);

	public static TypeReference ImportTypeWithGenericArguments<T>(this ModuleDefinition module, Type type,
		params T[] genericArguments) where T : TypeReference
			=> module.ImportTypeWithGenericArguments(type, (ICollection<T>)genericArguments);

	public static VariableDefinition DeclareLocal(this ILProcessor ilProcessor, Type localType)
	{
		var variableDefinition = new VariableDefinition(ilProcessor.GetModule().ImportReference(localType));
		ilProcessor.Body.Variables.Add(variableDefinition);
		return variableDefinition;
	}

	public static VariableDefinition DeclareLocal(this ILProcessor ilProcessor, TypeReference localType)
	{
		var variableDefinition = new VariableDefinition(localType);
		ilProcessor.Body.Variables.Add(variableDefinition);
		return variableDefinition;
	}

	public static void InsertAt(this ILProcessor ilProcessor, int index, Instruction instruction)
		=> ilProcessor.instructions.Insert(index, instruction);

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, TypeReference type)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, type));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, Type type)
		=> ilProcessor.InsertAt(index, opCode, ilProcessor.GetModule().ImportReference(type));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, CallSite site)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, site));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, MethodReference method)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, method));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, MethodBase method,
		params Type[] genericArguments)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode,
			ImportReferenceWithOptionalGenericArguments(method, ilProcessor.GetModule(), genericArguments)));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, Delegate method,
		params Type[] genericArguments)
		=> ilProcessor.InsertAt(index, opCode, method.Method, genericArguments);

	private static MethodReference ImportReferenceWithOptionalGenericArguments(MethodBase method,
		ModuleDefinition module, params Type[] genericArguments)
		=> genericArguments.Length > 0
			? module.ImportMethodWithGenericArguments((MethodInfo)method, genericArguments)
			: module.ImportReference(method);

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, FieldReference field)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, field));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, FieldInfo field)
		=> ilProcessor.InsertAt(index, opCode, ilProcessor.GetModule().ImportReference(field));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, string value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, sbyte value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, byte value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, int value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, long value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, nint value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, float value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, double value)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, value));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, Instruction target)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, target));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, Instruction[] targets)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, targets));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, VariableDefinition variable)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, variable));

	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, ParameterDefinition parameter)
		=> ilProcessor.InsertAt(index, Instruction.Create(opCode, parameter));
	
	public static void InsertAt(this ILProcessor ilProcessor, int index, OpCode opCode, object operand)
		=> ilProcessor.InsertAt(index, CreateInstruction(opCode, operand));

	public static void InsertAt(this ILProcessor ilProcessor, int index, object instruction)
	{
		var module = ilProcessor.GetModule();
		ilProcessor.InsertAt(index, instruction switch
		{
			Instruction codeInstruction => codeInstruction,
			OpCode opCode => Instruction.Create(opCode),
			ITuple tuple => CreateInstruction(tuple is [OpCode opCode, _, ..]
					? opCode
					: ThrowArgumentExceptionForInvalidType<OpCode>(instruction),
				tuple[1] switch
				{
					MethodBase => ImportMethodFromTuple(tuple, module),
					FieldInfo fieldInfo => module.ImportReference(fieldInfo),
					Type => ImportTypeFromTuple(tuple, module),
					ParameterInfo parameterInfo => GetParameterDefinition(ilProcessor, parameterInfo),
					Delegate @delegate => module.ImportReference(@delegate.Method),
					_ => tuple[1]
				}),
			_ => ThrowArgumentExceptionForInvalidType<Instruction>(instruction)
		});
	}

	private static ParameterDefinition GetParameterDefinition(ILProcessor ilProcessor, ParameterInfo parameterInfo)
	{
		var parameters = ilProcessor.Body.Method.Parameters;
		foreach (var parameter in parameters)
		{
			if (parameter.Name == parameterInfo.Name)
				return parameter;
		}

		return ThrowHelper.ThrowArgumentException<ParameterDefinition>($"Could not find parameter named {
			parameterInfo.Name} in method {ilProcessor.Body.Method.FullName}");
	}

	private static MethodReference ImportMethodFromTuple(ITuple tuple, ModuleDefinition module)
	{
		var tupleLength = tuple.Length;
		Guard.IsGreaterThanOrEqualTo(tupleLength, 2);

		if (tupleLength == 2)
			return module.ImportReference((MethodBase)tuple[1]);

		var methodInfo = (MethodInfo)tuple[1];
		switch (tuple[2])
		{
			case Type[] typeArray:
			{
				return module.ImportMethodWithGenericArguments(methodInfo, typeArray);
			}
			case Type:
			{
				var genericArguments = new Type[tupleLength - 2];
				for (var i = 2; i < tupleLength; i++)
					genericArguments[i - 2] = (Type)tuple[i];

				return module.ImportMethodWithGenericArguments(methodInfo, genericArguments);
			}
			case Collection<GenericParameter> genericParameterCollection:
			{
				return module.ImportMethodWithGenericArguments(methodInfo, genericParameterCollection);
			}
			case Collection<TypeReference> typeReferenceCollection:
			{
				return module.ImportMethodWithGenericArguments(methodInfo, typeReferenceCollection);
			}
			case TypeReference:
			{
				var genericArguments = new TypeReference[tupleLength - 2];
				for (var i = 2; i < tupleLength; i++)
					genericArguments[i - 2] = (TypeReference)tuple[i];

				return module.ImportMethodWithGenericArguments(methodInfo, genericArguments);
			}
			default:
			{
				return ThrowHelper.ThrowArgumentException<MethodReference>(
					$"Tuple contains invalid type: {tuple[2].GetType().FullName}");
			}
		}
	}

	private static TypeReference ImportTypeFromTuple(ITuple tuple, ModuleDefinition module)
	{
		var tupleLength = tuple.Length;
		Guard.IsGreaterThanOrEqualTo(tupleLength, 2);

		if (tupleLength == 2)
			return module.ImportReference((Type)tuple[1]);

		var type = (Type)tuple[1];
		switch (tuple[2])
		{
			case Type[] typeArray:
			{
				return module.ImportTypeWithGenericArguments(type, typeArray);
			}
			case Type:
			{
				var genericArguments = new Type[tupleLength - 2];
				for (var i = 2; i < tupleLength; i++)
					genericArguments[i - 2] = (Type)tuple[i];

				return module.ImportTypeWithGenericArguments(type, genericArguments);
			}
			case Collection<GenericParameter> genericParameterCollection:
			{
				return module.ImportTypeWithGenericArguments(type, genericParameterCollection);
			}
			case Collection<TypeReference> typeReferenceCollection:
			{
				return module.ImportTypeWithGenericArguments(type, typeReferenceCollection);
			}
			case TypeReference:
			{
				var genericArguments = new TypeReference[tupleLength - 2];
				for (var i = 2; i < tupleLength; i++)
					genericArguments[i - 2] = (TypeReference)tuple[i];

				return module.ImportTypeWithGenericArguments(type, genericArguments);
			}
			default:
			{
				return ThrowHelper.ThrowArgumentException<TypeReference>(
					$"Tuple contains invalid type: {tuple[2].GetType().FullName}");
			}
		}
	}

	public static void InsertRange(this ILProcessor ilProcessor, int index, IEnumerable<Instruction> instructions)
	{
		foreach (var instruction in instructions)
			ilProcessor.InsertAt(index++, instruction);
	}

	public static void InsertRange(this ILProcessor ilProcessor, int index, params Instruction[] instructions)
	{
		for (var i = 0; i < instructions.Length; i++)
			ilProcessor.InsertAt(index++, instructions[i]);
	}

	public static void InsertRange(this ILProcessor ilProcessor, int index, IEnumerable<object> instructions)
	{
		foreach (var instruction in instructions)
			ilProcessor.InsertAt(index++, instruction);
	}

	public static void InsertRange(this ILProcessor ilProcessor, int index, params object[] instructions)
	{
		for (var i = 0; i < instructions.Length; i++)
			ilProcessor.InsertAt(index++, instructions[i]);
	}

	public static void ReplaceBodyWith(this ILProcessor ilProcessor, Delegate replacement)
		=> ilProcessor.ReplaceBodyWith(replacement.Method);
	
	public static void ReplaceBodyWith(this ILProcessor ilProcessor, MethodInfo replacement)
	{
		var oldBody = ilProcessor.Body;
		var module = oldBody.Method.Module;

		if (replacement.IsGenericMethod)
			replacement = replacement.GetGenericMethodDefinition();

		if (replacement.DeclaringType!.IsGenericType)
		{
			replacement = AccessTools.DeclaredMethod(replacement.DeclaringType.GetGenericTypeDefinition(),
				replacement.Name, replacement.GetParameters().GetTypes());
		}

		var newMethodReference = module.ImportReference(replacement);
		var newBody = newMethodReference.Resolve().Body;

		oldBody.Instructions.Clear();
		foreach (var instruction in newBody.instructions)
		{
			oldBody.Instructions.Add(new(instruction.OpCode, instruction.Operand switch
			{
				FieldReference field => module.ImportReference(field, newMethodReference),
				MethodReference method => module.ImportReference(method, newMethodReference),
				TypeReference type => module.ImportReference(type, newMethodReference),
				_ => instruction.Operand
			}));
		}

		oldBody.Variables.Clear();
		foreach (var variable in newBody.Variables)
			oldBody.Variables.Add(new(module.ImportReference(variable.VariableType, newMethodReference)));

		oldBody.MaxStackSize = newBody.MaxStackSize;

		oldBody.ExceptionHandlers.Clear();
		foreach (var exceptionHandler in newBody.ExceptionHandlers)
		{
			var handler = new ExceptionHandler(exceptionHandler.HandlerType)
			{
				TryStart = exceptionHandler.TryStart,
				TryEnd = exceptionHandler.TryEnd,
				FilterStart = exceptionHandler.FilterStart,
				HandlerStart = exceptionHandler.HandlerStart,
				HandlerEnd = exceptionHandler.HandlerEnd
			};

			if (exceptionHandler.CatchType is { } catchType)
				handler.CatchType = module.ImportReference(catchType, newMethodReference);
			
			oldBody.ExceptionHandlers.Add(handler);
		}

		oldBody.Method.ImplAttributes = newBody.Method.ImplAttributes;
	}

	public static MethodDefinition GetMethod(this ILProcessor ilProcessor) => ilProcessor.Body.Method;

	public static MethodDefinition? GetMethod(this TypeDefinition type, string name, Type?[]? parameters = null)
	{
		var methods = type.Methods;
		MethodDefinition? foundMethod = null;
		
		for (var i = 0; i < methods.Count; i++)
		{
			var method = methods[i];
			if (method.Name != name
				|| !TestForMatchingTypes(parameters, method.Parameters, static p => p.ParameterType.Name))
			{
				continue;
			}

			if (foundMethod != null)
				ThrowForAmbiguousMatch(type, name, parameters);
			else
				foundMethod = method;
		}
		
		return foundMethod;
	}

	public static TypeDefinition? GetTypeDefinition(this ModuleDefinition module, Type type)
		=> type.DeclaringType is { } declaringType
			? module.GetTypeDefinition(declaringType)?.NestedTypes.GetNamed(type.Name)
			: module.GetType(type.Namespace, type.Name);

	private static void ThrowForAmbiguousMatch(TypeDefinition type, string name, Type?[]? parameters)
		=> throw new AmbiguousMatchException($"Ambiguous match found for type: {type.FullName}, name: {
			name}, parameters: {parameters.ToStringSafeEnumerable()}");

	private static bool TestForMatchingTypes<T>(Type?[]? types, Collection<T>? testedTypes,
		Func<T, string> typeNameGetter)
	{
		if (types is null)
			return true;

		if (testedTypes is null || testedTypes.Count != types.Length)
			return false;

		var typesNotMatching = false;
		for (var i = 0; i < types.Length; i++)
		{
			var type = types[i];
			if (type is null || type.IsGenericParameter)
				continue;
			
			if (typeNameGetter(testedTypes[i]) != type.Name)
				typesNotMatching = true;
		}

		return !typesNotMatching;
	}

	public static ModuleDefinition GetModule(this ILProcessor ilProcessor) => ilProcessor.GetMethod().Module;

	public static Collection<TypeReference>? TryGetGenericArguments(this MethodReference method)
		=> method is GenericInstanceMethod instanceMethod
			? instanceMethod.GenericArguments
			: method.DeclaringType.TryGetGenericArguments();

	public static Collection<TypeReference>? TryGetGenericArguments(this TypeReference type)
	{
		while (type != null)
		{
			if (type is GenericInstanceType instanceType)
				return instanceType.GenericArguments;

			type = type.DeclaringType;
		}

		return null;
	}

	public static Collection<TypeReference>? TryGetGenericArguments(this TypeReference type,
		IGenericParameterProvider provider)
	{
		while (type != null)
		{
			if (type is GenericInstanceType instanceType)
				return instanceType.GenericArguments.ConvertAll(g => g.TryGetGenericParameterType(provider) ?? g);

			type = type.DeclaringType;
		}

		return null;
	}

	public static Collection<TypeReference>? TryGetGenericArguments(this IGenericParameterProvider provider)
		=> provider switch
		{
			null => ThrowHelper.ThrowArgumentNullException<Collection<TypeReference>>(nameof(provider)),
			MethodReference method => method.TryGetGenericArguments(),
			TypeReference type => type.TryGetGenericArguments(),
			_ => ThrowHelper.ThrowArgumentException<Collection<TypeReference>>(
				$"Generic parameter provider has unsupported type: {provider.GetType()}")
		};

	public static TypeReference? TryGetGenericParameterType(this TypeReference typeReference,
		IGenericParameterProvider provider)
		=> typeReference is GenericParameter genericParameter
			? TryGetUnderlyingType(genericParameter, provider)
			: null;

	public static TypeReference? TryGetUnderlyingType(GenericParameter genericParameter,
		IGenericParameterProvider provider)
		=> (genericParameter.Owner is TypeReference ownerType && provider is MemberReference providerMember
			? providerMember.TryGetParentOfName(ownerType.Name)
			: provider)?.TryGetGenericArguments()?[genericParameter.Position];
	

	public static Collection<TOutput> ConvertAll<TInput, TOutput>(this Collection<TInput> collection,
		Converter<TInput, TOutput> converter)
	{
		var result = new Collection<TOutput>(collection.Count);
		
		for (var i = 0; i < collection.Count; i++)
			result.Add(converter(collection[i])); // somehow throws OutOfRangeExceptions when using set_Item here
		// Storing collection.Count in a var makes no difference

		return result;
	}

	private static TypeReference? TryGetParentOfName(this MemberReference providerMember, string name)
	{
		var type = providerMember as TypeReference ?? providerMember.DeclaringType;
		while (type != null)
		{
			if (type.Name == name)
				return type;

			type = type.DeclaringType;
		}

		return null;
	}

	private static T ThrowArgumentExceptionForInvalidType<T>(object instruction)
		=> ThrowHelper.ThrowArgumentException<T>(nameof(instruction),
			// ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
			$"Argument is of invalid type: {instruction?.GetType().FullName ?? "NULL"}");

	private static Instruction CreateInstruction(OpCode opCode, object operand)
		=> operand switch
		{
			TypeReference type => Instruction.Create(opCode, type),
			CallSite site => Instruction.Create(opCode, site),
			MethodReference method => Instruction.Create(opCode, method),
			FieldReference field => Instruction.Create(opCode, field),
			string value => Instruction.Create(opCode, value),
			sbyte value => Instruction.Create(opCode, value),
			byte value => Instruction.Create(opCode, value),
			int value => Instruction.Create(opCode, value),
			long value => Instruction.Create(opCode, value),
			float value => Instruction.Create(opCode, value),
			double value => Instruction.Create(opCode, value),
			nint value => Instruction.Create(opCode, value),
			Instruction target => Instruction.Create(opCode, target),
			Instruction[] targets => Instruction.Create(opCode, targets),
			VariableDefinition variable => Instruction.Create(opCode, variable),
			ParameterDefinition parameter => Instruction.Create(opCode, parameter),
			_ => ThrowHelper.ThrowArgumentException<Instruction>(nameof(operand),
				// ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
				$"Operand for OpCode {opCode} is of invalid type: {operand?.GetType().FullName ?? "NULL"}")
		};
}