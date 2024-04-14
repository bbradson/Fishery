// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace FisheryLib.Collections.Internal;

public static class KeyUtility<T>
{
	public static readonly T Default = GetDefaultValue();

	private static T GetDefaultValue()
	{
		var type = typeof(T);
		
		var invalidKey = KeyUtility.GetDefaultValue(type);
		if (!type.IsValueType)
			return Unsafe.As<object?, T>(ref invalidKey);
		
		return (T)invalidKey!;
	}
}

public static class KeyUtility
{
	public static readonly object InvalidObject = new();

	public const ulong InvalidValue = 0x7F7F_7F7F_7F7F_7F7FUL;

	public static readonly string InvalidString
		= new(['\u7F7F', '\u7F7F', '\u7F7F', '\u7F7F', '\u7F7F', '\u7F7F', '\u7F7F', '\u7F7F']);
	
	private static readonly ConcurrentDictionary<FieldInfo, Action<object>> _fieldSetters = [];

	private static readonly ConcurrentDictionary<Type, object> _defaultValues = [];

	private static readonly MethodInfo _genericMaxValueMethodDefinition
		= methodof(GetMaxValue<ulong>).GetGenericMethodDefinition();

	private static readonly FieldInfo _invalidFieldInfo = typeof(KeyUtility).GetField(nameof(InvalidObject));

	public static object? GetDefaultValue(Type type)
	{
		if (type == typeof(string))
			return InvalidString;

		if (_defaultValues.TryGetValue(type, out var result))
			return result;
		
		if (!type.IsValueType)
		{
			return !type.IsAssignableTo(typeof(IEquatable<>), type)
				&& type.GetMethod(nameof(object.Equals),
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(object)],
					Array.Empty<ParameterModifier>())?.DeclaringType
				== typeof(object)
					? InvalidObject
					: null;
		}

		result = FormatterServices.GetUninitializedObject(type);

		var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		for (var i = 0; i < fields.Length; i++)
		{
			var field = fields[i];
			var fieldType = field.FieldType;
			if (fieldType.IsValueType)
			{
				if (AccessTools.IsValue(fieldType))
					field.SetValue(result, GetMaxValue(fieldType));
				else
					GetFieldInvalidator(field)(result);
			}
			else
			{
				if (fieldType != typeof(string))
				{
					if (!fieldType.IsAssignableTo(typeof(IEquatable<>), fieldType))
						GetFieldInvalidator(field)(result);
					else
						field.SetValue(result, null);
				}
				else
				{
					field.SetValue(result, InvalidString);
				}
			}
			
			if (Unsafe.As<object, ulong>(ref result) > 0xFFFF_FFFF_FFFF)
				Log.Error($"Detected invalid pointer with a value > 48 bit\n{new StackTrace(true)}");
		}
		
		_defaultValues.TryAdd(type, result);

		return result;
	}

	private static object GetMaxValue(Type type)
	{
		if (!AccessTools.IsValue(type))
		{
			return ThrowHelper.ThrowArgumentException<object>($"Invalid type passed to KeyUtility.GetMaxValue: '{
				type.FullDescription()}'. Only primitive structs are supported.");
		}

		var result = _genericMaxValueMethodDefinition.MakeGenericMethod(type).Invoke(null, null);
		_defaultValues.TryAdd(type, result);
		return result;
	}

	private static object GetMaxValue<T>() where T : struct
	{
		var maxValue = InvalidValue;
		return Unsafe.As<ulong, T>(ref maxValue)!;
	}
	
	private static Action<object> GetFieldInvalidator(FieldInfo fieldInfo)
	{
		if (_fieldSetters.TryGetValue(fieldInfo, out var result))
			return result;
		
		var method = new DynamicMethod($"FisheryLib><::>UnsafeSetter<::><{fieldInfo.FullDescription()}", typeof(void),
			[typeof(object)], typeof(KeyUtility), true);
		var ilGenerator = method.GetILGenerator();
		var fieldType = fieldInfo.FieldType;
		var declaringType = fieldInfo.DeclaringType;
		
		ilGenerator.Emit(FishTranspiler.Argument(0));
		if (declaringType?.IsValueType ?? false)
			ilGenerator.Emit(FishTranspiler.UnboxAddress(declaringType));
		
		if (!fieldType.IsValueType)
		{
			ilGenerator.Emit(FishTranspiler.Field(_invalidFieldInfo));
			ilGenerator.Emit(FishTranspiler.Call(methodof(Unsafe.As<object>).GetGenericMethodDefinition()
				.MakeGenericMethod(fieldType)));
		}
		else
		{
			ilGenerator.Emit(FishTranspiler.Token(fieldType));
			ilGenerator.Emit(FishTranspiler.Call(Type.GetTypeFromHandle));

			ilGenerator.Emit(FishTranspiler.Call(AccessTools.IsValue(fieldType)
				// ReSharper disable once RedundantCast
				? (Func<Type, object?>)GetMaxValue
				: GetDefaultValue));
			
			ilGenerator.Emit(FishTranspiler.UnboxValue(fieldType));
		}
		ilGenerator.Emit(FishTranspiler.StoreField(fieldInfo));
		ilGenerator.Emit(FishTranspiler.Return);
		
		result = (Action<object>)method.CreateDelegate(typeof(Action<object>));
		_fieldSetters.TryAdd(fieldInfo, result);
		return result;
	}
}