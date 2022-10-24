// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

namespace FisheryLib;
public abstract class SingletonFactory<T_base> where T_base : notnull
{
	/// <summary>
	/// Get a Singleton instance of a type derived from T_base
	/// </summary>
	/// <typeparam name="T">The type of the instance</typeparam>
	/// <returns>The Singleton instance</returns>
	public static T Get<T>() where T : T_base
		=> (T)GetInternal(typeof(T));

	/// <summary>
	/// Get a Singleton instance of a type derived from T_base
	/// </summary>
	/// <param name="type">The type of the instance</param>
	/// <returns>The Singleton instance</returns>
	public static T_base Get(Type type)
	{
		if (!typeof(T_base).IsAssignableFrom(type))
			ThrowHelper.ThrowArgumentException($"Parameters for {typeof(T_base).Name}.Get must be assignable from Type {typeof(T_base).Name}. Got {type.FullDescription()} instead.");

		return GetInternal(type);
	}

	/// <summary>
	/// protected to allow declaring of default values on derived types. Do not directly invoke this constructor or expose it as public.
	/// </summary>
	protected SingletonFactory()
	{
		if (_instances.ContainsKey(GetType()))
			ThrowHelper.ThrowInvalidOperationException($"Tried creating a second instance of {GetType().Name}. Only the {typeof(T_base).Name}.Get method should be used for instantiation.");
	}

	private static T_base GetInternal(Type type) => _instances.GetOrAdd(type, type => (T_base)Activator.CreateInstance(type, true));

	private static readonly ConcurrentDictionary<Type, T_base> _instances = new();
}