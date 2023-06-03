// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Mono.Cecil;

namespace FisheryLib.Cecil;

public static class CecilReflectionExtensions
{
	public static MethodReference ImportMethodWithGenericArguments(this ModuleDefinition module, MethodInfo method,
		params Type[] genericArguments)
		=> module.ImportReference(method.IsGenericMethodDefinition ? method : method.GetGenericMethodDefinition())
			.MakeGenericMethod(genericArguments.GetTypeReferences(module));

	public static MethodReference ImportMethodWithGenericArguments(this ModuleDefinition module, Delegate method,
		params Type[] genericArguments)
		=> module.ImportMethodWithGenericArguments(method.Method, genericArguments);
	
	public static TypeReference ImportTypeWithGenericArguments(this ModuleDefinition module, Type type,
		params Type[] genericArguments)
		=> module.ImportReference(type.IsGenericTypeDefinition ? type : type.GetElementType())
			.MakeGenericType(genericArguments.GetTypeReferences(module));
}