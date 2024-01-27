// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v.2.0.If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FisheryLib.Collections;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace FisheryLib.Cecil;

public static class InstructionExtensions
{
	public static bool LoadsArgument(this Instruction instruction, int index)
		=> instruction.LoadsArgument() && GetLoadArgumentIndex(instruction) == index;

	public static bool LoadsArgument(this Instruction instruction) => LoadArgumentOpcodes.Contains(instruction.OpCode);

	private static int GetLoadArgumentIndex(Instruction instruction)
	{
		var opCode = instruction.OpCode;
		return opCode == OpCodes.Ldarg_0 ? 0
			: opCode == OpCodes.Ldarg_1 ? 1
			: opCode == OpCodes.Ldarg_2 ? 2
			: opCode == OpCodes.Ldarg_3 ? 3
			: instruction.Operand is ParameterDefinition parameterDefinition ? parameterDefinition.Index
			: Convert.Type<object, int>(instruction.Operand);
	}

	public static readonly FishSet<OpCode> LoadArgumentOpcodes =
	[
		OpCodes.Ldarg, OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3, OpCodes.Ldarg_S,
		OpCodes.Ldarga, OpCodes.Ldarga_S
	];
}