// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
#if V1_2
using JetBrains.Annotations;
#endif

namespace FisheryLib.Utility.Diagnostics;

/// <summary>
/// Helper methods to verify conditions when running code.
/// </summary>
[DebuggerStepThrough]
[PublicAPI]
public static partial class Guard
{
	/// <summary>
	/// Asserts that the input value is <see langword="null"/>.
	/// </summary>
	/// <typeparam name="T">The type of reference value type being tested.</typeparam>
	/// <param name="value">The input value to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is not <see langword="null"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNull<T>([NoEnumeration] T? value, [CallerArgumentExpression("value")] string name = "")
	{
		if (value is null)
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNull(value, name);
	}

	/// <summary>
	/// Asserts that the input value is <see langword="null"/>.
	/// </summary>
	/// <typeparam name="T">The type of nullable value type being tested.</typeparam>
	/// <param name="value">The input value to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is not <see langword="null"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNull<T>([NoEnumeration] T? value, [CallerArgumentExpression("value")] string name = "")
		where T : struct
	{
		if (value is null)
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNull(value, name);
	}

	/// <summary>
	/// Asserts that the input value is not <see langword="null"/>.
	/// </summary>
	/// <typeparam name="T">The type of reference value type being tested.</typeparam>
	/// <param name="value">The input value to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is <see langword="null"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNotNull<T>([NoEnumeration] [NotNull] T? value,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (value is not null)
			return;

		ThrowHelper.ThrowArgumentNullExceptionForIsNotNull<T>(name);
	}

	/// <summary>
	/// Asserts that the input value is not <see langword="null"/>.
	/// </summary>
	/// <typeparam name="T">The type of nullable value type being tested.</typeparam>
	/// <param name="value">The input value to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is <see langword="null"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNotNull<T>([NoEnumeration] [NotNull] T? value,
		[CallerArgumentExpression("value")] string name = "")
		where T : struct
	{
		if (value is not null)
			return;

		ThrowHelper.ThrowArgumentNullExceptionForIsNotNull<T?>(name);
	}

	/// <summary>
	/// Asserts that the input value is of a specific type.
	/// </summary>
	/// <typeparam name="T">The type of the input value.</typeparam>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is not of type <typeparamref name="T"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsOfType<T>([NoEnumeration] object value, [CallerArgumentExpression("value")] string name = "")
	{
		if (value.GetType() == typeof(T))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsOfType<T>(value, name);
	}

	/// <summary>
	/// Asserts that the input value is not of a specific type.
	/// </summary>
	/// <typeparam name="T">The type of the input value.</typeparam>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is of type <typeparamref name="T"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNotOfType<T>([NoEnumeration] object value,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (value.GetType() != typeof(T))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNotOfType<T>(value, name);
	}

	/// <summary>
	/// Asserts that the input value is of a specific type.
	/// </summary>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="type">The type to look for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if the type of <paramref name="value"/> is not the same as <paramref name="type"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsOfType([NoEnumeration] object value, Type type,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (value.GetType() == type)
			return;

		ThrowHelper.ThrowArgumentExceptionForIsOfType(value, type, name);
	}

	/// <summary>
	/// Asserts that the input value is not of a specific type.
	/// </summary>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="type">The type to look for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if the type of <paramref name="value"/> is the same as <paramref name="type"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNotOfType([NoEnumeration] object value, Type type,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (value.GetType() != type)
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNotOfType(value, type, name);
	}

	/// <summary>
	/// Asserts that the input value can be assigned to a specified type.
	/// </summary>
	/// <typeparam name="T">The type to check the input value against.</typeparam>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> can't be assigned to type <typeparamref name="T"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsAssignableToType<T>([NoEnumeration] object value,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (value is T)
			return;

		ThrowHelper.ThrowArgumentExceptionForIsAssignableToType<T>(value, name);
	}

	/// <summary>
	/// Asserts that the input value can't be assigned to a specified type.
	/// </summary>
	/// <typeparam name="T">The type to check the input value against.</typeparam>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> can be assigned to type <typeparamref name="T"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNotAssignableToType<T>([NoEnumeration] object value,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (value is not T)
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNotAssignableToType<T>(value, name);
	}

	/// <summary>
	/// Asserts that the input value can be assigned to a specified type.
	/// </summary>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="type">The type to look for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> can't be assigned to <paramref name="type"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsAssignableToType([NoEnumeration] object value, Type type,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (type.IsInstanceOfType(value))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsAssignableToType(value, type, name);
	}

	/// <summary>
	/// Asserts that the input value can't be assigned to a specified type.
	/// </summary>
	/// <param name="value">The input <see cref="object"/> to test.</param>
	/// <param name="type">The type to look for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> can be assigned to <paramref name="type"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsNotAssignableToType([NoEnumeration] object value, Type type,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (!type.IsInstanceOfType(value))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNotAssignableToType(value, type, name);
	}
	
	/// <summary>
	/// Asserts that the input type can be assigned to a specified type.
	/// </summary>
	/// <param name="value">The input <see cref="Type"/> to test.</param>
	/// <param name="type">The type to look for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> can't be assigned to <paramref name="type"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsTypeAssignableToType([NoEnumeration] Type value, Type type,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (type.IsAssignableFrom(value))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsAssignableToType(value, type, name);
	}

	/// <summary>
	/// Asserts that the input type can't be assigned to a specified type.
	/// </summary>
	/// <param name="value">The input <see cref="Type"/> to test.</param>
	/// <param name="type">The type to look for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> can be assigned to <paramref name="type"/>.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsTypeNotAssignableToType([NoEnumeration] Type value, Type type,
		[CallerArgumentExpression("value")] string name = "")
	{
		if (!type.IsAssignableFrom(value))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsNotAssignableToType(value, type, name);
	}

	/// <summary>
	/// Asserts that the input value must be the same instance as the target value.
	/// </summary>
	/// <typeparam name="T">The type of input values to compare.</typeparam>
	/// <param name="value">The input <typeparamref name="T"/> value to test.</param>
	/// <param name="target">The target <typeparamref name="T"/> value to test for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is not the same instance as <paramref name="target"/>.</exception>
	/// <remarks>The method is generic to prevent using it with value types.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsReferenceEqualTo<T>([NoEnumeration] T value, T target,
		[CallerArgumentExpression("value")] string name = "")
		where T : class
	{
		if (ReferenceEquals(value, target))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsReferenceEqualTo<T>(name);
	}

	/// <summary>
	/// Asserts that the input value must not be the same instance as the target value.
	/// </summary>
	/// <typeparam name="T">The type of input values to compare.</typeparam>
	/// <param name="value">The input <typeparamref name="T"/> value to test.</param>
	/// <param name="target">The target <typeparamref name="T"/> value to test for.</param>
	/// <param name="name">The name of the input parameter being tested.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is the same instance as <paramref name="target"/>.</exception>
	/// <remarks>The method is generic to prevent using it with value types.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IsReferenceNotEqualTo<T>([NoEnumeration] T value, T target,
		[CallerArgumentExpression("value")] string name = "")
		where T : class
	{
		if (!ReferenceEquals(value, target))
			return;

		ThrowHelper.ThrowArgumentExceptionForIsReferenceNotEqualTo<T>(name);
	}
}