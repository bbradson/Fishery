// Copyright (c) 2023 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace FisheryLib.Cecil;

[PublicAPI]
public static class ReflectionExtensions
{
	public static Type[] GetTypes(this ParameterInfo[] parameterInfos)
		=> Array.ConvertAll(parameterInfos, static p => p.ParameterType);

	public static ParameterInfo? TryGetNamed(this ParameterInfo[] parameterInfos, string name)
		=> TryGetNamed(parameterInfos, name, static p => p.Name);

	public static T? TryGetNamed<T>(this T[] memberInfos, string name) where T : MemberInfo
		=> TryGetNamed(memberInfos, name, static m => m.Name);

	public static ParameterDefinition? TryGetNamed(this Collection<ParameterDefinition> parameterDefinitions,
		string name)
		=> TryGetNamed(parameterDefinitions, name, static p => p.Name);

	public static T? TryGetNamed<T>(this Collection<T> memberReferences, string name) where T : MemberReference
		=> TryGetNamed(memberReferences, name, static m => m.Name);
	
	public static ParameterInfo GetNamed(this ParameterInfo[] parameterInfos, string name)
		=> TryGetNamed(parameterInfos, name, static p => p.Name)
			?? ThrowForMissingParameter<ParameterInfo>(name);

	public static T GetNamed<T>(this T[] memberInfos, string name) where T : MemberInfo
		=> TryGetNamed(memberInfos, name, static m => m.Name)
			?? ThrowForMissingMember<T>(name);

	public static ParameterDefinition GetNamed(this Collection<ParameterDefinition> parameterDefinitions,
		string name)
		=> TryGetNamed(parameterDefinitions, name, static p => p.Name)
			?? ThrowForMissingParameter<ParameterDefinition>(name);

	public static T GetNamed<T>(this Collection<T> memberReferences, string name) where T : MemberReference
		=> TryGetNamed(memberReferences, name, static m => m.Name)
			?? ThrowForMissingMember<T>(name);

	[DoesNotReturn]
	private static T ThrowForMissingMember<T>(string name)
		=> ThrowHelper.ThrowInvalidOperationException<T>($"Sequence contains no member with name: {name}");

	[DoesNotReturn]
	private static T ThrowForMissingParameter<T>(string name)
		=> ThrowHelper.ThrowInvalidOperationException<T>(
			$"Sequence contains no parameter with name: {name}");

	private static T? TryGetNamed<T>(this IList<T> memberInfos, string name, Func<T, string> nameGetter)
		where T : class
	{
		for (var i = 0; i < memberInfos.Count; i++)
		{
			if (nameGetter(memberInfos[i]) == name)
				return memberInfos[i];
		}

		return null;
	}

	public static TypeReference[] GetTypeReferences(this Type[] types, ModuleDefinition module)
	{
		var result = new TypeReference[types.Length];
		for (var i = types.Length; i-- > 0;)
			result[i] = module.ImportReference(types[i]);

		return result;
	}
	
	public static bool IsGeneric(this TypeReference type)
		=> type.ContainsGenericParameter || type.HasGenericParameters || type.IsGenericInstance;

	public static bool IsGeneric(this Type type)
		=> type.ContainsGenericParameters || type.IsGenericType || type.IsConstructedGenericType;
}