// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Security;
using JetBrains.Annotations;
using Verse;
// ReSharper disable PossibleMultipleEnumeration

namespace FisheryLib;

[PublicAPI]
public static class FishTranspiler
{
	[PublicAPI]
	public readonly struct Container : IEquatable<Container>, IEquatable<CodeInstruction>
	{
		public Container(OpCode opcode, object? operand = null)
		{
			OpCode = opcode;
			Operand = operand;
		}

		public Container(CodeInstruction instruction)
		{
			Guard.IsNotNull(instruction, nameof(instruction));

			OpCode = instruction.opcode;
			Operand = instruction.operand;
		}

		public Container(Container container)
		{
			OpCode = container.OpCode;
			Operand = container.Operand;
		}

		public override string ToString()
			=> $"{OpCode} {Operand switch
			{
				MemberInfo memberInfo => memberInfo.FullDescription(),
				null => "NULL",
				_ => Operand.ToString()
			}}";

		[SecuritySafeCritical]
		public CodeInstruction ToInstruction() => new(OpCode, Operand);

		public Container Store()
			=> OpCode.LoadsArgument()
				? ToStoreArgument()
				: new(OpCode.LoadsField() || OpCode.StoresField() ? OpCode.ToStoreField()
					: OpCode.LoadsLocalVariable() || OpCode.StoresLocalVariable() ? OpCode.ToStoreLocalVariable()
					: OpCode.StoresArgument() ? OpCode
					: ThrowHelper.ThrowInvalidOperationException<OpCode>(
						$"Cannot determine Store instruction for {OpCode}"),
					Operand);

		public Container Load()
			=> OpCode.StoresArgument()
				? ToLoadArgument()
				: new(OpCode.LoadsField() || OpCode.StoresField() ? OpCode.ToLoadField()
					: OpCode.LoadsLocalVariable() || OpCode.StoresLocalVariable() ? OpCode.ToLoadLocalVariable()
					: OpCode.LoadsArgument() ? OpCode
					: ThrowHelper.ThrowInvalidOperationException<OpCode>(
						$"Cannot determine Load instruction for {OpCode}"),
					Operand);

		private Container ToStoreArgument() => new(OpCode.ToStoreArgument(), GetIndex());

		private Container ToLoadArgument()
		{
			var index = GetIndex();
			return new(GetLoadArgumentOpCode(index), index > 3 ? index : null);
		}

		public Container Address() => new(OpCode.ToAddress(), GetIndex());

		public CodeInstruction WithLabels(params Label[] labels) => WithLabels((IEnumerable<Label>)labels);

		[SecuritySafeCritical]
		public CodeInstruction WithLabels(IEnumerable<Label> labels)
		{
			Guard.IsNotNull(labels);

			return ToInstruction().WithLabels(labels);
		}

		public CodeInstruction WithBlocks(params ExceptionBlock[] blocks)
			=> WithBlocks((IEnumerable<ExceptionBlock>)blocks);

		[SecuritySafeCritical]
		public CodeInstruction WithBlocks(IEnumerable<ExceptionBlock> blocks)
		{
			Guard.IsNotNull(blocks);

			return ToInstruction().WithBlocks(blocks);
		}

		[SecuritySafeCritical]
		public CodeInstruction WithLabelsAndBlocks(CodeInstruction instruction)
			=> WithLabels(instruction.labels).WithBlocks(instruction.blocks);

		public int GetIndex()
			=> OpCode.TryGetIndex()
				?? TryGetIndexFromOperand(Operand)
				?? ThrowHelper.ThrowArgumentException<int>(
					$"{OpCode} has operand {Operand}. This is not a supported index.");

		public bool LoadsLocalVariable(MethodBase method, Type localType)
			=> OpCode.LoadsLocalVariable()
				&& GetTypeIn(method) == localType;

		public bool LoadsLocalVariable(int index)
			=> OpCode.LoadsLocalVariable()
				&& GetIndex() == index;

		public bool StoresLocalVariable(MethodBase method, Type localType)
			=> OpCode.StoresLocalVariable()
				&& GetTypeIn(method) == localType;

		public bool StoresLocalVariable(int index)
			=> OpCode.StoresLocalVariable()
				&& GetIndex() == index;

		public bool LoadsArgument(MethodBase method, string argumentName)
			=> OpCode.LoadsArgument()
				&& GetNameIn(method) == argumentName;

		public bool LoadsArgument(MethodBase method, Type argumentType)
			=> OpCode.LoadsArgument()
				&& GetTypeIn(method) == argumentType;

		public bool LoadsArgument(int index)
			=> OpCode.LoadsArgument()
				&& GetIndex() == index;

		public bool LoadsField(FieldInfo fieldInfo)
			=> OpCode.LoadsField()
				&& Operand is FieldInfo operandInfo
				&& operandInfo == fieldInfo;

		public bool StoresField(FieldInfo fieldInfo)
			=> OpCode.StoresField()
				&& Operand is FieldInfo operandInfo
				&& operandInfo == fieldInfo;

		public bool Calls(MethodInfo methodInfo)
			=> (OpCode == OpCodes.Call || OpCode == OpCodes.Callvirt)
				&& Operand is MethodInfo operandInfo
				&& operandInfo == methodInfo;

		public string? GetNameIn(MethodBase method)
			=> OpCode.LoadsArgument() ? method.GetParameters()[GetIndex()].Name
				: OpCode.LoadsField() || OpCode.StoresField() ? (Operand as FieldInfo)?.Name
				: OpCode == OpCodes.Call || OpCode == OpCodes.Callvirt ? (Operand as MethodInfo)?.Name
				: null;

		public Type? GetTypeIn(MethodBase method)
			=> OpCode.LoadsArgument() ? method.GetParameters()[GetIndex()].ParameterType
				: OpCode.LoadsLocalVariable() || OpCode.StoresLocalVariable()
					? method.GetLocalVariables()[GetIndex()].LocalType
				: OpCode.LoadsField() || OpCode.StoresField() ? (Operand as FieldInfo)?.FieldType
				: OpCode == OpCodes.Call || OpCode == OpCodes.Callvirt ? (Operand as MethodInfo)?.ReturnType
				: OpCode.LoadsConstant() ? Operand?.GetType()
				: null;

		public override bool Equals(object? obj)
			=> obj is CodeInstruction code
				? this == code
				: obj is Container helper && this == helper;

		public bool Equals(Container container) => this == container;

		public bool Equals(CodeInstruction instruction) => this == instruction;

		public override int GetHashCode()
			=> Operand is null
				? OpCode.GetHashCode()
				: unchecked((((1009 * 9176) + OpCode.GetHashCode()) * 9176) + Operand.GetHashCode());

		public static bool operator ==(Container lhs, Container rhs)
			=> lhs.OpCode == rhs.OpCode
				&& CompareOperands(lhs.Operand, rhs.Operand);

		public static bool operator !=(Container lhs, Container rhs) => !(lhs == rhs);

		public static bool operator ==(Container helper, CodeInstruction code)
			=> helper.OpCode == code.opcode
				&& CompareOperands(helper.Operand, code.operand);

		public static bool operator !=(Container helper, CodeInstruction code) => !(helper == code);

		public static bool operator ==(CodeInstruction code, Container helper) => helper == code;

		public static bool operator !=(CodeInstruction code, Container helper) => !(helper == code);

		[SuppressMessage("Usage", "CA2225")]
		public static implicit operator CodeInstruction(Container helper) => helper.ToInstruction();

		//public static implicit operator Container(CodeInstruction instruction)
		//	=> new(instruction);

		//public static implicit operator Container(OpCode opCode)
		//	=> new(opCode);

		public OpCode OpCode { get; }

		public object? Operand { get; }

		/// <summary>
		/// Gets the number of values popped from the stack by this instruction
		/// </summary>
		public int Pops
			=> PopsNone() ? 0
				: PopsOne() ? 1
				: PopsTwo() ? 2
				: PopsThree() ? 3
				: OpCode.Calls() ? GetCallPops()
				: OpCode.Branches() ? GetBranchPops()
				: ThrowHelper.ThrowNotSupportedException<int>($"OpCode {
					OpCode} not supported by FishTranspiler.Pops");

		/// <summary>
		/// Gets the number of values pushed onto the stack by this instruction
		/// </summary>
		public int Pushes
			=> PushesNone() ? 0
				: PushesOne() ? 1
				: PushesTwo() ? 2
				: OpCode.Calls() ? GetCallPushes()
				: ThrowHelper.ThrowNotSupportedException<int>($"OpCode {
					OpCode} not supported by FishTranspiler.Pushes");

		private bool PopsNone()
			=> OpCode == OpCodes.Nop
				|| OpCode == OpCodes.Jmp
				|| OpCode == OpCodes.Ldftn
				|| OpCode.LoadsArgument()
				|| OpCode.LoadsConstant()
				|| OpCode.LoadsStaticField()
				|| OpCode.LoadsLocalVariable();

		private bool PopsOne()
			=> OpCode == OpCodes.Pop
				|| OpCode == OpCodes.Initobj
				|| OpCode == OpCodes.Dup
				|| OpCode == OpCodes.Isinst
				|| OpCode == OpCodes.Not
				|| OpCode == OpCodes.Neg
				|| OpCode == OpCodes.Ldlen
				|| OpCode == OpCodes.Newarr
				|| OpCode == OpCodes.Ldvirtftn
				|| OpCode.StoresArgument()
				|| OpCode.StoresStaticField()
				|| OpCode.LoadsInstanceField()
				|| OpCode.StoresLocalVariable()
				|| OpCode.LoadsByRef()
				|| OpCode.Converts();

		private bool PopsTwo()
			=> OpCode == OpCodes.Cpobj
				|| OpCode.StoresInstanceField()
				|| OpCode.LoadsElement()
				|| OpCode.StoresByRef()
				|| OpCode.Compares()
				|| OpCode.Computes();

		private bool PopsThree()
			=> OpCode == OpCodes.Cpblk
				|| OpCode == OpCodes.Initblk
				|| OpCode.StoresElement();

		private int GetCallPops()
			=> OpCode == OpCodes.Calli
				|| Operand is not MethodInfo methodInfo
					? ThrowHelper.ThrowNotSupportedException<int>()
					: methodInfo.GetParameters().Length + (methodInfo.IsStatic ? 0 : 1);

		private int GetBranchPops()
			=> OpCode == OpCodes.Brtrue
				|| OpCode == OpCodes.Brtrue_S
				|| OpCode == OpCodes.Brfalse
				|| OpCode == OpCodes.Brfalse_S
				|| OpCode == OpCodes.Switch ? 1
				: OpCode == OpCodes.Br
				|| OpCode == OpCodes.Br_S ? 0
				: 2; // greater, less, equal, etc

		private bool PushesNone()
			=> OpCode == OpCodes.Nop
				|| OpCode == OpCodes.Jmp
				|| OpCode == OpCodes.Pop
				|| OpCode == OpCodes.Cpblk
				|| OpCode == OpCodes.Initblk
				|| OpCode == OpCodes.Cpobj
				|| OpCode == OpCodes.Initobj
				|| OpCode.StoresArgument()
				|| OpCode.StoresField()
				|| OpCode.StoresLocalVariable()
				|| OpCode.StoresElement()
				|| OpCode.StoresByRef()
				|| OpCode.Branches();

		private bool PushesOne()
			=> OpCode == OpCodes.Isinst
				|| OpCode == OpCodes.Ldlen
				|| OpCode == OpCodes.Newarr
				|| OpCode == OpCodes.Newobj
				|| OpCode == OpCodes.Ldftn
				|| OpCode == OpCodes.Ldvirtftn
				|| OpCode.LoadsArgument()
				|| OpCode.LoadsConstant()
				|| OpCode.LoadsField()
				|| OpCode.LoadsLocalVariable()
				|| OpCode.LoadsElement()
				|| OpCode.LoadsByRef()
				|| OpCode.Compares()
				|| OpCode.Computes()
				|| OpCode.Converts();

		private bool PushesTwo() => OpCode == OpCodes.Dup;

		private int GetCallPushes()
			=> OpCode == OpCodes.Calli
				|| Operand is not MethodInfo methodInfo ? ThrowHelper.ThrowNotSupportedException<int>()
				: methodInfo.ReturnType == typeof(void) ? 0
				: 1;
	}

	/// <summary>
	/// Create a FishTranspiler copy of a Harmony CodeInstruction.
	/// </summary>
	/// <param name="instruction">The Harmony CodeInstruction to copy</param>
	public static Container Copy(CodeInstruction instruction)
	{
		Guard.IsNotNull(instruction);

		return new(instruction);
	}

	/// <summary>
	/// Loads the first argument matching a specified type onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="argumentType">The type to search for</param>
	public static Container FirstArgument(MethodBase method, Type argumentType)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(argumentType);

		return Argument(FirstArgumentIndex(method, argumentType));
	}

	/// <summary>
	/// Loads the first argument matching a supplied predicate onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="predicate">The predicate to match</param>
	public static Container FirstArgument(MethodBase method, Func<ParameterInfo, bool> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		return Argument(FirstArgumentIndex(method, predicate));
	}

	/// <summary>
	/// Loads the argument referenced by a specified name onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="name">The name of the argument</param>
	public static Container Argument(MethodBase method, string name)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNullOrEmpty(name);

		return Argument(FirstArgumentIndex(method, p => p.Name == name));
	}

	/// <summary>
	/// Loads the argument referenced by a supplied ParameterInfo onto the evaluation stack.
	/// </summary>
	/// <param name="parameter">The ParameterInfo of the argument</param>
	public static Container Argument(ParameterInfo parameter)
	{
		Guard.IsNotNull(parameter);

		return Argument(ArgumentIndex(parameter));
	}

	/// <summary>
	/// Loads the argument referenced by a specified index onto the evaluation stack.
	/// </summary>
	/// <param name="index">The index of the argument in its method's parameter list</param>
	public static Container Argument(int index) => new(GetLoadArgumentOpCode(index), GetOperandFromIndex(index));

	/// <summary>
	/// Loads the address of the argument referenced by a specified name onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="name">The name of the argument</param>
	public static Container ArgumentAddress(MethodBase method, string name)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNullOrEmpty(name);

		return ArgumentAddress(FirstArgumentIndex(method, p => p.Name == name));
	}

	/// <summary>
	/// Loads the address of the argument referenced by a specified index onto the evaluation stack.
	/// </summary>
	/// <param name="index">The index of the argument in its method's parameter list</param>
	public static Container ArgumentAddress(int index) => new(GetLoadArgumentAddressOpCode(index), index);

	/// <summary>
	/// Loads the first argument matching a specified type onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="argumentType">The type to search for</param>
	public static Container StoreFirstArgument(MethodBase method, Type argumentType)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(argumentType);

		return StoreArgument(FirstArgumentIndex(method, argumentType));
	}

	/// <summary>
	/// Loads the first argument matching a supplied predicate onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="predicate">The predicate to match</param>
	public static Container StoreFirstArgument(MethodBase method, Func<ParameterInfo, bool> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		return StoreArgument(FirstArgumentIndex(method, predicate));
	}

	/// <summary>
	/// Loads the argument referenced by a specified name onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the argument's method</param>
	/// <param name="name">The name of the argument</param>
	public static Container StoreArgument(MethodBase method, string name)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNullOrEmpty(name);

		return StoreArgument(FirstArgumentIndex(method, p => p.Name == name));
	}

	/// <summary>
	/// Loads the argument referenced by a supplied ParameterInfo onto the evaluation stack.
	/// </summary>
	/// <param name="parameter">The ParameterInfo of the argument</param>
	public static Container StoreArgument(ParameterInfo parameter)
	{
		Guard.IsNotNull(parameter);

		return StoreArgument(ArgumentIndex(parameter));
	}

	/// <summary>
	/// Loads the argument referenced by a specified index onto the evaluation stack.
	/// </summary>
	/// <param name="index">The index of the argument in its method's parameter list</param>
	public static Container StoreArgument(int index) => new(GetStoreArgumentOpCode(index), GetOperandFromIndex(index));

	/// <summary>
	/// Loads the local variable at the first index matching the specified type
	/// onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the method to search for local variables in</param>
	/// <param name="localType">The type of the local variable to search for</param>
	public static Container FirstLocalVariable(MethodBase method, Type localType)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(localType);

		return FirstLocalVariable(method, l => l.LocalType == localType);
	}

	/// <summary>
	/// Loads the local variable at the first index matching the supplied predicate
	/// onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the method to search for local variables in</param>
	/// <param name="predicate">The predicate used to identify the local variable</param>
	public static Container FirstLocalVariable(MethodBase method, Predicate<LocalVariableInfo> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		var locals = MatchingLocalVariables(method, predicate);
		return locals.Any()
			? locals.First()
			: ThrowHelper.ThrowInvalidOperationException<Container>(
				$"No local variable found for predicate {predicate.Method.FullDescription()} and method {
				method.FullDescription()}");
	}

	/// <summary>
	/// Loads all local variables matching the supplied predicate onto the evaluation stack.
	/// </summary>
	/// <param name="method">The MethodBase of the method to search for local variables in</param>
	/// <param name="predicate">The predicate used to identify the local variables</param>
	public static IEnumerable<Container> MatchingLocalVariables(MethodBase method,
		Predicate<LocalVariableInfo> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		foreach (var index in GetLocalIndices(method, predicate))
			yield return new(GetLoadLocalOpCode(index), GetOperandFromIndex(index));
	}

	/// <summary>
	/// Loads the local variable at the first index matching the specified type
	/// onto the evaluation stack.
	/// </summary>
	/// <param name="codes">The IEnumerable of CodeInstructions to search in</param>
	/// <param name="localType">The type of the local variable to search for</param>
	public static Container FirstLocalVariable(CodeInstructions codes, Type localType)
	{
		Guard.IsNotNull(codes);
		Guard.IsNotNull(localType);

		var operands = GetLocalOperandsOrIndices(codes, c => c.Returns(localType));
		return operands.Any()
			? LocalVariable(operands.First())
			: ThrowHelper.ThrowInvalidOperationException<Container>(
				$"No local variable found with localType {localType.FullDescription()}");
	}

	/// <summary>
	/// Loads the local variable at a specified index onto the evaluation stack.
	/// </summary>
	/// <param name="operand">The index or LocalBuilder of the local variable</param>
	public static Container LocalVariable(object operand)
	{
		Guard.IsNotNull(operand);

		return operand is LocalBuilder builder ? LocalVariable(builder) : LocalVariable((int)operand);
	}

	/// <summary>
	/// Loads the local variable at the index specified by a supplied LocalBuilder
	/// onto the evaluation stack.
	/// </summary>
	/// <param name="builder">The LocalBuilder of the local variable</param>
	public static Container LocalVariable(LocalBuilder builder)
	{
		Guard.IsNotNull(builder);

		return new(GetLoadLocalOpCode(builder.LocalIndex), GetOperandFromBuilder(builder));
	}

	/// <summary>
	/// Loads the local variable at a specified index onto the evaluation stack.
	/// </summary>
	/// <param name="index">The index of the local variable</param>
	public static Container LocalVariable(int index) => new(GetLoadLocalOpCode(index), GetOperandFromIndex(index));

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at the first index matching the specified type.
	/// </summary>
	/// <param name="method">The MethodBase of the method to search for local variables in</param>
	/// <param name="localType">The type of the local variable to search for</param>
	public static Container StoreFirstLocalVariable(MethodBase method, Type localType)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(localType);

		return StoreFirstLocalVariable(method, l => l.LocalType == localType);
	}

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at the first index matching the supplied predicate.
	/// </summary>
	/// <param name="method">The MethodBase of the method to search for local variables in</param>
	/// <param name="predicate">The predicate used to identify the local variable</param>
	public static Container StoreFirstLocalVariable(MethodBase method, Predicate<LocalVariableInfo> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		var locals = StoreMatchingLocalVariables(method, predicate);
		return locals.Any()
			? locals.First()
			: ThrowHelper.ThrowInvalidOperationException<Container>(
				$"No local variable found for predicate {predicate.Method.FullDescription()} and method {method.FullDescription()}");
	}

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at all indices matching the supplied predicate.
	/// </summary>
	/// <param name="method">The MethodBase of the method to search for local variables in</param>
	/// <param name="predicate">The predicate used to identify the local variables</param>
	public static IEnumerable<Container> StoreMatchingLocalVariables(MethodBase method,
		Predicate<LocalVariableInfo> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		foreach (var index in GetLocalIndices(method, predicate))
			yield return new(GetStoreLocalOpCode(index), GetOperandFromIndex(index));
	}

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at the first index matching with the specified type.
	/// </summary>
	/// <param name="codes">The IEnumerable of CodeInstructions to search in</param>
	/// <param name="localType">The type of the local variable to search for</param>
	public static Container StoreFirstLocalVariable(CodeInstructions codes, Type localType)
	{
		Guard.IsNotNull(codes);
		Guard.IsNotNull(localType);

		var operands = GetLocalOperandsOrIndices(codes, c => c.Returns(localType));
		return operands.Any()
			? StoreLocalVariable(operands.First())
			: ThrowHelper.ThrowInvalidOperationException<Container>(
				$"No local variable found with localType {localType.FullDescription()}");
	}

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at index.
	/// </summary>
	/// <param name="operand">The index or LocalBuilder of the local variable</param>
	public static Container StoreLocalVariable(object operand)
	{
		Guard.IsNotNull(operand);

		return operand is LocalBuilder builder ? StoreLocalVariable(builder) : StoreLocalVariable((int)operand);
	}

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at the index specified by the supplied LocalBuilder.
	/// </summary>
	/// <param name="builder">The LocalBuilder of the local variable</param>
	public static Container StoreLocalVariable(LocalBuilder builder)
	{
		Guard.IsNotNull(builder);

		return new(GetStoreLocalOpCode(builder.LocalIndex), GetOperandFromBuilder(builder));
	}

	/// <summary>
	/// Pops the current value from the top of the evaluation stack and stores it in
	/// the local variable list at index.
	/// </summary>
	/// <param name="index">The index of the local variable</param>
	public static Container StoreLocalVariable(int index)
		=> new(GetStoreLocalOpCode(index), GetOperandFromIndex(index));

	/// <summary>
	/// Declares a local variable of the specified type. Then returns an instruction matching that of StoreLocalVariable.
	/// </summary>
	/// <param name="localType">A System.Type object that represents the type of the local variable</param>
	/// <param name="generator">The ILGenerator used for the instruction stream</param>
	public static Container NewLocalVariable(Type localType, ILGenerator generator)
	{
		Guard.IsNotNull(localType);
		Guard.IsNotNull(generator);

		return NewLocalVariable(localType, generator, false);
	}

	/// <summary>
	/// Declares a local variable of the specified type, pinning the object referred to by the variable,
	/// for use with unsafe pointers. Then returns an instruction matching that of StoreLocalVariable.
	/// </summary>
	/// <param name="localType">A System.Type object that represents the type of the local variable</param>
	/// <param name="generator">The ILGenerator used for the instruction stream</param>
	public static Container NewFixedLocalVariable(Type localType, ILGenerator generator)
	{
		Guard.IsNotNull(localType);
		Guard.IsNotNull(generator);

		return NewLocalVariable(localType, generator, true);
	}

	private static Container NewLocalVariable(Type localType, ILGenerator generator, bool pinned)
		=> StoreLocalVariable(generator.DeclareLocal(localType, pinned));

	/// <summary>
	/// Pushes a supplied value of type int32 onto the evaluation stack as an int32.
	/// </summary>
	/// <param name="integer">The value to push onto the evaluation stack</param>
	public static Container Constant([SuppressMessage("Naming", "CA1720")] int integer)
		=> new(GetLoadConstantOpCode(integer), GetOperandOfConstant(integer));

	/// <summary>
	/// Pushes a supplied value of type int64 onto the evaluation stack as an int64.
	/// </summary>
	/// <param name="integer">The value to push onto the evaluation stack</param>
	public static Container Constant([SuppressMessage("Naming", "CA1720")] long integer)
		=> new(OpCodes.Ldc_I8, integer);

	/// <summary>
	/// Pushes a supplied value of type float32 onto the evaluation stack as type F (float).
	/// </summary>
	/// <param name="number">The value to push onto the evaluation stack</param>
	public static Container Constant(float number) => new(OpCodes.Ldc_R4, number);

	/// <summary>
	/// Pushes a supplied value of type float64 onto the evaluation stack as type F (float).
	/// </summary>
	/// <param name="number">The value to push onto the evaluation stack</param>
	public static Container Constant(double number) => new(OpCodes.Ldc_R8, number);

	/// <summary>
	/// Pushes a supplied Enum value onto the evaluation stack as an int32, int64 or type F (float).
	/// </summary>
	/// <param name="e">The value to push onto the evaluation stack</param>
	public static Container Constant(Enum e)
		=> e.GetTypeCode() switch
		{
			TypeCode.SByte
				or TypeCode.Byte
				or TypeCode.Int16
				or TypeCode.UInt16
				or TypeCode.Int32
				or TypeCode.UInt32 => Constant(System.Convert.ToInt32(e)),
			TypeCode.Int64
				or TypeCode.UInt64 => Constant(System.Convert.ToInt64(e)),
			var typeCode => InvalidEnumForConstant(e, typeCode)
		};

	/// <summary>
	/// Pushes a new object reference to a string literal stored in the metadata.
	/// </summary>
	/// <param name="text">The string literal</param>
	public static Container Constant(string text) => String(text);

	/// <summary>
	/// Converts a metadata token to its runtime representation, pushing it onto the
	/// evaluation stack.
	/// </summary>
	/// <param name="memberInfo">The MemberInfo to push onto the stack</param>
	/// <exception cref="ArgumentNullException">memberInfo is null</exception>
	public static Container Constant(MemberInfo memberInfo) => Token(memberInfo);

	/// <summary>
	/// Pushes a supplied value onto the evaluation stack as an int32, int64, float, double, null reference,
	/// RuntimeHandle or a reference to a string literal stored in the metadata.
	/// </summary>
	/// <param name="value">The value to push onto the evaluation stack</param>
	public static Container Constant(object value)
		=> value switch
		{
			int intValue => Constant(intValue),
			long or ulong => Constant(System.Convert.ToInt64(value)),
			float floatValue => Constant(floatValue),
			double doubleValue => Constant(doubleValue),
			Enum enumValue => Constant(enumValue),
			string stringValue => String(stringValue),
			MemberInfo memberInfo => Token(memberInfo),
			null => Null,
			IConvertible convertible => Constant(convertible.ToInt32(CultureInfo.InvariantCulture)),
			_ => ThrowHelper.ThrowArgumentException<Container>(nameof(value),
				$"Type {value.GetType()} cannot be used for FishTranspiler.Constant")
		};

	private static Container InvalidEnumForConstant(Enum e, TypeCode typeCode)
		=> ThrowHelper.ThrowArgumentException<Container>(
			$"Tried using Enum {e.GetType().FullDescription()} with underlying Type {
				Enum.GetUnderlyingType(e.GetType()).FullDescription()} (TypeCode: {
				typeCode}) for FishTranspiler.Constant.");

	/// <summary>
	/// Calls the method indicated by the passed method group.
	/// </summary>
	/// <typeparam name="T">The compatible delegate type</typeparam>
	/// <param name="method">The method group</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	public static Container Call<T>(T method, bool forceNoCallvirt = false) where T : Delegate
	{
		Guard.IsNotNull(method);

		return Call(method.Method, forceNoCallvirt);
	}

	/// <summary>
	/// Calls the method indicated by the passed expression.
	/// </summary>
	/// <param name="expression">An expression with a single method call</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	[SecuritySafeCritical]
	public static Container Call(Expression<Action> expression, bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(expression);

		return Call(SymbolExtensions.GetMethodInfo(expression), forceNoCallvirt);
	}

	/// <summary>
	/// Calls the method indicated by the passed arguments.
	/// </summary>
	/// <param name="assembly">The name of the assembly the method is declared in</param>
	/// <param name="type">The name of the type the method is declared in</param>
	/// <param name="name">The name of the method</param>
	/// <param name="parameters">An optional array of parameter types to specify the desired method overload</param>
	/// <param name="generics">
	/// An optional array of generic parameter types to specify the method's generic parameters
	/// </param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	public static Container Call(string assembly, string type, string name, Type[]? parameters = null,
		Type[]? generics = null, bool forceNoCallvirt = false)
	{
		Guard.IsNotNullOrEmpty(assembly);
		Guard.IsNotNullOrEmpty(type);
		Guard.IsNotNullOrEmpty(name);

		return Call(
			Type.GetType($"{type}, {assembly}")
			?? ThrowHelper.ThrowArgumentException<Type>($"No type named {type} found in assembly {assembly}"), name,
			parameters, generics, forceNoCallvirt);
	}

	/// <summary>
	/// Calls the method indicated by the passed arguments.
	/// </summary>
	/// <param name="type">The type the method is declared in</param>
	/// <param name="name">The name of the method</param>
	/// <param name="parameters">An optional array of parameter types to specify the desired method overload</param>
	/// <param name="generics">
	/// An optional array of generic parameter types to specify the method's generic parameters
	/// </param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	[SecuritySafeCritical]
	public static Container Call(Type type, string name, Type[]? parameters = null, Type[]? generics = null,
		bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(type);
		Guard.IsNotNullOrEmpty(name);

		return Call(
			AccessTools.Method(type, name, parameters, generics)
			?? ThrowHelper.ThrowArgumentException<MethodInfo>($"No method found with type {type}, name {
				name}{(parameters != null ? $", parameters: {parameters.ToStringSafeEnumerable()}" : "")}{
				(generics != null ? $", generics: {generics.ToStringSafeEnumerable()}" : "")}"),
			forceNoCallvirt);
	}

	/// <summary>
	/// Calls the method indicated by the passed method descriptor.
	/// </summary>
	/// <param name="method">The MethodBase used as descriptor</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	/// <exception cref="ArgumentNullException">MethodBase is null</exception>
	public static Container Call(MethodBase method, bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(method);

		return new(
			method.IsStatic || (method.DeclaringType?.IsValueType ?? false) || forceNoCallvirt
				? OpCodes.Call
				: OpCodes.Callvirt, method);
	}

	/// <summary>
	/// Pushes the value of a static field onto the evaluation stack or finds
	/// the value of a field in the object whose reference is currently on the
	/// evaluation stack.
	/// </summary>
	/// <param name="type">The type the field is declared in</param>
	/// <param name="name">The name of the field</param>
	/// <exception cref="ArgumentException">No field found for the specified type and name</exception>
	[SecuritySafeCritical]
	public static Container Field(Type type, string name)
	{
		Guard.IsNotNull(type);
		Guard.IsNotNullOrEmpty(name);

		var field = AccessTools.Field(type, name);
		return field != null
			? new(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field)
			: ThrowHelper.ThrowMissingFieldException<Container>(
				$"FishTranspiler.LoadField failed to find a field at {type.FullDescription()}:{name}");
	}

	/// <summary>
	/// Pushes the value of a static field onto the evaluation stack or finds
	/// the value of a field in the object whose reference is currently on the
	/// evaluation stack.
	/// </summary>
	/// <param name="fieldInfo">The FieldInfo</param>
	public static Container Field(FieldInfo fieldInfo)
	{
		Guard.IsNotNull(fieldInfo);

		return new(fieldInfo.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldInfo);
	}

	/// <summary>
	/// Pushes the address of a static field onto the evaluation stack or finds
	/// the address of a field in the object whose reference is currently on the
	/// evaluation stack.
	/// </summary>
	/// <param name="type">The type the field is declared in</param>
	/// <param name="name">The name of the field</param>
	/// <exception cref="ArgumentException">No field found for the specified type and name</exception>
	[SecuritySafeCritical]
	public static Container FieldAddress(Type type, string name)
	{
		Guard.IsNotNull(type);
		Guard.IsNotNullOrEmpty(name);

		var field = AccessTools.Field(type, name);
		return field != null
			? new(field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, field)
			: ThrowHelper.ThrowMissingFieldException<Container>(
				$"FishTranspiler.LoadFieldAddress failed to find a field at {type.FullDescription()}:{name}");
	}

	/// <summary>
	/// Pushes the address of a static field onto the evaluation stack or finds
	/// the address of a field in the object whose reference is currently on the
	/// evaluation stack.
	/// </summary>
	/// <param name="fieldInfo">The FieldInfo</param>
	public static Container FieldAddress(FieldInfo fieldInfo)
	{
		Guard.IsNotNull(fieldInfo);

		return new(fieldInfo.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, fieldInfo);
	}

	/// <summary>
	/// Replaces the value of a static field with a value from the evaluation stack
	/// or replaces the value stored in the field of an object reference or pointer
	/// with a new value.
	/// </summary>
	/// <param name="type">The type the field is declared in</param>
	/// <param name="name">The name of the field</param>
	/// <exception cref="ArgumentException">No field found for the specified type and name</exception>
	[SecuritySafeCritical]
	public static Container StoreField(Type type, string name)
	{
		Guard.IsNotNull(type);
		Guard.IsNotNullOrEmpty(name);

		var field = AccessTools.Field(type, name);
		return field != null
			? new(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field)
			: ThrowHelper.ThrowMissingFieldException<Container>(
				$"FishTranspiler.StoreField failed to find a field at {type.FullDescription()}:{name}");
	}

	/// <summary>
	/// Replaces the value of a static field with a value from the evaluation stack
	/// or replaces the value stored in the field of an object reference or pointer
	/// with a new value.
	/// </summary>
	/// <param name="fieldInfo">The FieldInfo</param>
	public static Container StoreField(FieldInfo fieldInfo)
	{
		Guard.IsNotNull(fieldInfo);

		return new(fieldInfo.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, fieldInfo);
	}

	[Obsolete("Use FishTranspiler.PropertyGetter instead")]
	public static Container CallPropertyGetter(Type type, string name, bool forceNoCallvirt = false)
		=> PropertyGetter(type, name, forceNoCallvirt);

	[Obsolete("Use FishTranspiler.PropertyGetter instead")]
	public static Container CallPropertyGetter(PropertyInfo propertyInfo, bool forceNoCallvirt = false)
		=> PropertyGetter(propertyInfo, forceNoCallvirt);

	[Obsolete("Use FishTranspiler.PropertySetter instead")]
	public static Container CallPropertySetter(Type type, string name, bool forceNoCallvirt = false)
		=> PropertySetter(type, name, forceNoCallvirt);

	[Obsolete("Use FishTranspiler.PropertySetter instead")]
	public static Container CallPropertySetter(PropertyInfo propertyInfo, bool forceNoCallvirt = false)
		=> PropertySetter(propertyInfo, forceNoCallvirt);

	/// <summary>
	/// Calls the property getter indicated by the passed arguments.
	/// </summary>
	/// <param name="type">The type the property is declared in</param>
	/// <param name="name">The name of the property</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	/// <exception cref="ArgumentException">No property found for the specified type and name</exception>
	[SecuritySafeCritical]
	public static Container PropertyGetter(Type type, string name, bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(type);
		Guard.IsNotNullOrEmpty(name);

		var method = AccessTools.PropertyGetter(type, name);
		return method != null
			? Call(method, forceNoCallvirt)
			: ThrowHelper.ThrowMissingMethodException<Container>(
				$"FishTranspiler.CallPropertyGetter failed to find a property getter at {
					type.FullDescription()}:{name}");
	}

	/// <summary>
	/// Calls the property getter indicated by the passed arguments.
	/// </summary>
	/// <param name="propertyInfo">The PropertyInfo</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	/// <exception cref="ArgumentException">No property getter found for the specified property info</exception>
	public static Container PropertyGetter(PropertyInfo propertyInfo, bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(propertyInfo);

		var method = propertyInfo.GetMethod;
		return method != null
			? Call(method, forceNoCallvirt)
			: ThrowHelper.ThrowMissingMethodException<Container>(
				$"FishTranspiler.CallPropertyGetter failed to find a property getter at {
					propertyInfo.DeclaringType.FullDescription()}:{propertyInfo.Name}");
	}

	/// <summary>
	/// Calls the property setter indicated by the passed arguments.
	/// </summary>
	/// <param name="type">The type the property is declared in</param>
	/// <param name="name">The name of the property</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	/// <exception cref="ArgumentException">No property found with specified type and name</exception>
	[SecuritySafeCritical]
	public static Container PropertySetter(Type type, string name, bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(type);
		Guard.IsNotNullOrEmpty(name);

		var method = AccessTools.PropertySetter(type, name);
		return method != null
			? Call(method, forceNoCallvirt)
			: ThrowHelper.ThrowMissingMethodException<Container>(
				$"FishTranspiler.CallPropertySetter failed to find a property setter at {
					type.FullDescription()}:{name}");
	}

	/// <summary>
	/// Calls the property setter indicated by the passed arguments.
	/// </summary>
	/// <param name="propertyInfo">The PropertyInfo</param>
	/// <param name="forceNoCallvirt">force a Call OpCode instead of using Callvirt where appropriate</param>
	/// <exception cref="ArgumentException">No property setter found for the specified property info</exception>
	public static Container PropertySetter(PropertyInfo propertyInfo, bool forceNoCallvirt = false)
	{
		Guard.IsNotNull(propertyInfo);

		var method = propertyInfo.SetMethod;
		return method != null
			? Call(method, forceNoCallvirt)
			: ThrowHelper.ThrowMissingMethodException<Container>(
				$"FishTranspiler.CallPropertySetter failed to find a property setter at {
					propertyInfo.DeclaringType.FullDescription()}:{propertyInfo.Name}");
	}

	/// <summary>
	/// Constrains the type on which a virtual method call is made. Normally used for generics.
	/// </summary>
	/// <typeparam name="T">The type to constrain to</typeparam>
	public static Container Constrained<T>() => Constrained(typeof(T));

	/// <summary>
	/// Constrains the type on which a virtual method call is made. Normally used for generics.
	/// </summary>
	/// <param name="type">The type to constrain to</param>
	public static Container Constrained(Type type)
	{
		Guard.IsNotNull(type);

		return new(OpCodes.Constrained, type);
	}

	/// <summary>
	/// Creates a new object or a new instance of a value type, pushing an object reference
	/// (type O) onto the evaluation stack
	/// </summary>
	/// <typeparam name="T">The type of the new object</typeparam>
	/// <param name="parameters">Optional parameter types to specify the desired constructor overload</param>
	public static Container New<T>(params Type[]? parameters) => New(typeof(T), parameters);

	/// <summary>
	/// Creates a new object or a new instance of a value type, pushing an object reference
	/// (type O) onto the evaluation stack
	/// </summary>
	/// <param name="type">The type of the new object</param>
	/// <param name="parameters">An optional array of parameter types to specify the desired constructor overload</param>
	public static Container New(Type type, Type[]? parameters = null)
	{
		Guard.IsNotNull(type);

		return typeof(Array).IsAssignableFrom(type)
			? new(OpCodes.Newarr, type.GetElementType())
			: type.IsValueType
			&& (parameters is null || parameters.Length == 0)
			&& !Array.Exists(type.GetConstructors(AccessTools.allDeclared & ~BindingFlags.Static),
				static c => c.GetParameters().Length == 0)
				? new(OpCodes.Initobj, type)
				: New(Reflection.MatchingConstructor(type, parameters, throwOnFailure: true)!);
	}

	/// <summary>
	/// Creates a new object or a new instance of a value type, pushing an object reference
	/// (type O) onto the evaluation stack
	/// </summary>
	/// <param name="constructor">The ConstructorInfo to use as descriptor for the new instance</param>
	/// <exception cref="ArgumentNullException">ConstructorInfo is null</exception>
	public static Container New(ConstructorInfo constructor)
	{
		Guard.IsNotNull(constructor);

		return new(OpCodes.Newobj, constructor);
	}

	/// <summary>
	/// Pushes a new object reference to a string literal stored in the metadata.
	/// </summary>
	/// <param name="text">The string literal</param>
	[SuppressMessage("Naming", "CA1720")]
	public static Container String(string text)
	{
		Guard.IsNotNull(text);

		return new(OpCodes.Ldstr, text);
	}

	/// <summary>
	/// Transfers control to a target instruction if value is false, a null reference,
	/// or zero. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfFalse_Short(Label label) => new(OpCodes.Brfalse_S, label);

	/// <summary>
	/// Transfers control to a target instruction if value is false, a null reference,
	/// or zero.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfFalse(Label label) => new(OpCodes.Brfalse, label);

	/// <summary>
	/// Transfers control to a target instruction if value is true, not
	/// null, or non-zero. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfTrue_Short(Label label) => new(OpCodes.Brtrue_S, label);

	/// <summary>
	/// Transfers control to a target instruction if value is true, not
	/// null, or non-zero.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfTrue(Label label) => new(OpCodes.Brtrue, label);

	/// <summary>
	/// Transfers control to a target instruction if value is false, a null reference,
	/// or zero. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfNull_Short(Label label) => new(OpCodes.Brfalse_S, label);

	/// <summary>
	/// Transfers control to a target instruction if value is false, a null reference,
	/// or zero.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfNull(Label label) => new(OpCodes.Brfalse, label);

	/// <summary>
	/// Transfers control to a target instruction if value is true, not
	/// null, or non-zero. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfNotNull_Short(Label label) => new(OpCodes.Brtrue_S, label);

	/// <summary>
	/// Transfers control to a target instruction if value is true, not
	/// null, or non-zero.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfNotNull(Label label) => new(OpCodes.Brtrue, label);

	/// <summary>
	/// Unconditionally transfers control to a target instruction.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container GoTo(Label label) => new(OpCodes.Br, label);

	/// <summary>
	/// Unconditionally transfers control to a target instruction. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container GoTo_Short(Label label) => new(OpCodes.Br_S, label);

	/// <summary>
	/// Transfers control to a target instruction if two values are equal.
	/// Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfEqual_Short(Label label) => new(OpCodes.Beq_S, label);

	/// <summary>
	/// Transfers control to a target instruction if two values are equal.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfEqual(Label label) => new(OpCodes.Beq, label);

	/// <summary>
	/// Transfers control to a target instruction if two values are not equal or unordered.
	/// Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfNotEqualOrUnordered_Short(Label label) => new(OpCodes.Bne_Un_S, label);

	/// <summary>
	/// Transfers control to a target instruction if two values are not equal or unordered.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfNotEqualOrUnordered(Label label) => new(OpCodes.Bne_Un, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than the second value. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThan_Short(Label label) => new(OpCodes.Bgt_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than the second value.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThan(Label label) => new(OpCodes.Bgt, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than the second value or if any value is unordered. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThanOrUnordered_Short(Label label) => new(OpCodes.Bgt_Un_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than the second value or if any value is unordered.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThanOrUnordered(Label label) => new(OpCodes.Bgt_Un, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than or equal to the second value. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThanOrEqual_Short(Label label) => new(OpCodes.Bge_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than or equal to the second value.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThanOrEqual(Label label) => new(OpCodes.Bge, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than or equal to the second value or if any value is unordered.
	/// Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThanOrEqualOrUnordered_Short(Label label) => new(OpCodes.Bge_Un_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// greater than or equal to the second value or if any value is unordered.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfGreaterThanOrEqualOrUnordered(Label label) => new(OpCodes.Bge_Un, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than the second value. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThan_Short(Label label) => new(OpCodes.Blt_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than the second value.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThan(Label label) => new(OpCodes.Blt, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than the second value or if any value is unordered.
	/// Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThanOrUnordered_Short(Label label) => new(OpCodes.Blt_Un_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than the second value or if any value is unordered.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThanOrUnordered(Label label) => new(OpCodes.Blt_Un, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than or equal to the second value. Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThanOrEqual_Short(Label label) => new(OpCodes.Ble_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than or equal to the second value.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThanOrEqual(Label label) => new(OpCodes.Ble, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than or equal to the second value or if any value is unordered.
	/// Short form for short jumps.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThanOrEqualOrUnordered_Short(Label label) => new(OpCodes.Ble_Un_S, label);

	/// <summary>
	/// Transfers control to a target instruction if the first value is
	/// less than or equal to the second value or if any value is unordered.
	/// </summary>
	/// <param name="label">The label attached to the target instruction</param>
	public static Container IfLessThanOrEqualOrUnordered(Label label) => new(OpCodes.Ble_Un, label);

	/// <summary>
	/// Implements a jump table.
	/// </summary>
	public static Container Switch(params Label[] labels)
	{
		Guard.IsNotNull(labels);
		Guard.HasSizeGreaterThan(labels, 0);

		return new(OpCodes.Switch, labels);
	}

	/// <summary>
	/// Loads a value of the specified type onto the evaluation stack indirectly or copies
	/// the value type object pointed to by an address to the top of the evaluation stack.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	public static Container LoadByRef<T>() => LoadByRef(typeof(T));

	/// <summary>
	/// Loads a value of the specified type onto the evaluation stack indirectly or copies
	/// the value type object pointed to by an address to the top of the evaluation stack.
	/// </summary>
	/// <param name="type">The object's type</param>
	[SecuritySafeCritical]
	public static Container LoadByRef(Type? type)
	{
		if (type is null)
			return LoadIndirectly(type);

		if (type.IsByRef)
			type = type.GetElementType();

		return AccessTools.IsStruct(type)
			? LoadObject(type!)
			: LoadIndirectly(type);
	}

	/// <summary>
	/// Stores a value of a specified reference type at a supplied address or copies
	/// a value of a specified value type from the evaluation stack into a supplied
	/// memory address.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	public static Container StoreByRef<T>() => StoreByRef(typeof(T));

	/// <summary>
	/// Stores a value of a specified reference type at a supplied address or copies
	/// a value of a specified value type from the evaluation stack into a supplied
	/// memory address.
	/// </summary>
	/// <param name="type">The object's type</param>
	[SecuritySafeCritical]
	public static Container StoreByRef(Type? type)
	{
		if (type is null)
			return StoreIndirectly(type);

		if (type.IsByRef)
			type = type.GetElementType();

		return AccessTools.IsStruct(type)
			? StoreObject(type!)
			: StoreIndirectly(type);
	}

	/// <summary>
	/// Copies the value type object pointed to by an address to the top of the evaluation
	/// stack.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	public static Container LoadObject<T>() => LoadObject(typeof(T));

	/// <summary>
	/// Copies the value type object pointed to by an address to the top of the evaluation
	/// stack.
	/// </summary>
	/// <param name="type">The object's type</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	/// <exception cref="ArgumentException">type is not a ValueType</exception>
	public static Container LoadObject(Type type)
	{
		Guard.IsNotNull(type, nameof(type));

		return type.IsValueType
			? new(OpCodes.Ldobj, type)
			: ThrowHelper.ThrowArgumentException<Container>(
				$"Type ({type}) for FishTranspiler.LoadObject must be a ValueType. Use LoadByRef or "
				+ "LoadIndirectly for primitive and reference types.",
				nameof(type));
	}

	/// <summary>
	/// Copies a value of a specified type from the evaluation stack into a supplied
	/// memory address.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	public static Container StoreObject<T>() => StoreObject(typeof(T));

	/// <summary>
	/// Copies a value of a specified type from the evaluation stack into a supplied
	/// memory address.
	/// </summary>
	/// <param name="type">The object's type</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	/// <exception cref="ArgumentException">type is not a ValueType</exception>
	public static Container StoreObject(Type type)
	{
		Guard.IsNotNull(type);

		return type.IsValueType
			? new(OpCodes.Stobj, type)
			: ThrowHelper.ThrowArgumentException<Container>(
				$"Type ({type}) for FishTranspiler.StoreObject must be a ValueType. Use StoreByRef or "
				+ "StoreIndirectly for primitive and reference types.",
				nameof(type));
	}

	/// <summary>
	/// Converts a value type to an object reference (type O).
	/// </summary>
	/// <typeparam name="T">The type of the value</typeparam>
	public static Container Box<T>() => new(OpCodes.Box, typeof(T));

	/// <summary>
	/// Converts a value type to an object reference (type O).
	/// </summary>
	/// <param name="type">The type of the value</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	/// <exception cref="ArgumentException">type is not a ValueType</exception>
	public static Container Box(Type type)
	{
		Guard.IsNotNull(type);

		return type.IsValueType
			? new(OpCodes.Box, type)
			: ThrowHelper.ThrowArgumentException<Container>(
				$"Type ({type}) for FishTranspiler.Box must be a ValueType.", nameof(type));
	}
	

	/// <summary>
	/// Converts the boxed representation of a value type to its unboxed form, pushing an address to the box object's
	/// held value to the stack. Implemented using the Unbox instruction.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	public static Container UnboxAddress<T>() => UnboxAddress(typeof(T));

	/// <summary>
	/// Converts the boxed representation of a value type to its unboxed form, pushing an address to the box object's
	/// held value to the stack. Implemented using the Unbox instruction.
	/// </summary>
	/// <param name="type">The object's type</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	/// <exception cref="ArgumentException">type is not a ValueType</exception>
	public static Container UnboxAddress(Type type)
	{
		Guard.IsNotNull(type);

		return type.IsValueType
			? new(OpCodes.Unbox, type)
			: ThrowHelper.ThrowArgumentException<Container>(
				$"Type ({type}) for FishTranspiler.UnboxAddress must be a ValueType.", nameof(type));
	}

	/// <summary>
	/// Converts the boxed representation of a value type to its unboxed form, pushing an address to the box object's
	/// held value to the stack. Implemented using the Unbox instruction.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	[Obsolete("Renamed to UnboxAddress for clarity")]
	public static Container Unbox<T>() => UnboxAddress<T>();

	/// <summary>
	/// Converts the boxed representation of a value type to its unboxed form, pushing an address to the box object's
	/// held value to the stack. Implemented using the Unbox instruction.
	/// </summary>
	/// <param name="type">The object's type</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	/// <exception cref="ArgumentException">type is not a ValueType</exception>
	[Obsolete("Renamed to UnboxAddress for clarity")]
	public static Container Unbox(Type type) => UnboxAddress(type);

	/// <summary>
	/// Converts the boxed representation of a type specified in the instruction to its unboxed form, pushing the value
	/// to the stack. Implemented using the UnboxAny instruction.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	public static Container UnboxValue<T>() => UnboxValue(typeof(T));

	/// <summary>
	/// Converts the boxed representation of a type specified in the instruction to its unboxed form, pushing the value
	/// to the stack. Implemented using the UnboxAny instruction.
	/// </summary>
	/// <param name="type">The object's type</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	public static Container UnboxValue(Type type)
	{
		Guard.IsNotNull(type);

		return new(OpCodes.Unbox_Any, type);
	}

	/// <summary>
	/// Converts the boxed representation of a type specified in the instruction to its unboxed form, pushing the value
	/// to the stack. Implemented using the UnboxAny instruction.
	/// </summary>
	/// <typeparam name="T">The object's type</typeparam>
	[Obsolete("Renamed to UnboxValue for clarity")]
	public static Container UnboxAny<T>() => UnboxValue<T>();

	/// <summary>
	/// Converts the boxed representation of a type specified in the instruction to its unboxed form, pushing the value
	/// to the stack. Implemented using the UnboxAny instruction.
	/// </summary>
	/// <param name="type">The object's type</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	[Obsolete("Renamed to UnboxValue for clarity")]
	public static Container UnboxAny(Type type) => UnboxValue(type);

	/// <summary>
	/// Loads the current instance onto the evaluation stack.
	/// </summary>
	public static Container This => new(OpCodes.Ldarg_0);

	/// <summary>
	/// Returns from the current method, pushing a return value (if present) from the
	/// callee's evaluation stack onto the caller's evaluation stack.
	/// </summary>
	public static Container Return => new(OpCodes.Ret);

	/// <summary>
	/// Copies the current topmost value on the evaluation stack, and then pushes the
	/// copy onto the evaluation stack.
	/// </summary>
	public static Container Duplicate => new(OpCodes.Dup);

	/// <summary>
	/// Removes the value currently on top of the evaluation stack.
	/// </summary>
	public static Container Pop => new(OpCodes.Pop);

	/// <summary>
	/// Pushes a null reference (type O) onto the evaluation stack.
	/// </summary>
	public static Container Null => new(OpCodes.Ldnull);

	/// <summary>
	/// Adds two values and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container Add => new(OpCodes.Add);

	/// <summary>
	/// Adds two integers, performs an overflow check, and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container AddChecked => new(OpCodes.Add_Ovf);

	/// <summary>
	/// Adds two unsigned integer values, performs an overflow check, and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container AddCheckedUnsigned => new(OpCodes.Add_Ovf_Un);

	/// <summary>
	/// Multiplies two values and pushes the result on the evaluation stack.
	/// </summary>
	public static Container Multiply => new(OpCodes.Mul);

	/// <summary>
	/// Multiplies two integer values, performs an overflow check, and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container MultiplyChecked => new(OpCodes.Mul_Ovf);

	/// <summary>
	/// Multiplies two unsigned integer values, performs an overflow check, and pushes the result onto the evaluation
	/// stack.
	/// </summary>
	public static Container MultiplyCheckedUnsigned => new(OpCodes.Mul_Ovf_Un);

	/// <summary>
	/// Divides two values and pushes the result as a floating-point (type F) or quotient (type int32) onto the
	/// evaluation stack.
	/// </summary>
	public static Container Divide => new(OpCodes.Div);

	/// <summary>
	/// Divides two unsigned integer values and pushes the result (int32) onto the evaluation stack.
	/// </summary>
	public static Container DivideUnsigned => new(OpCodes.Div_Un);

	/// <summary>
	/// Subtracts one value from another and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container Subtract => new(OpCodes.Sub);

	/// <summary>
	/// Subtracts one integer value from another, performs an overflow check, and pushes the result onto the evaluation
	/// stack.
	/// </summary>
	public static Container SubtractChecked => new(OpCodes.Sub_Ovf);

	/// <summary>
	/// Subtracts one unsigned integer value from another, performs an overflow check, and pushes the result onto the
	/// evaluation stack.
	/// </summary>
	public static Container SubtractCheckedUnsigned => new(OpCodes.Sub_Ovf_Un);

	/// <summary>
	/// Divides two values and pushes the remainder onto the evaluation stack.
	/// </summary>
	public static Container Remainder => new(OpCodes.Rem);

	/// <summary>
	/// Divides two unsigned values and pushes the remainder onto the evaluation stack.
	/// </summary>
	public static Container RemainderUnsigned => new(OpCodes.Rem_Un);

	/// <summary>
	/// Computes the bitwise AND of two values and pushes the result onto the evaluation
	/// stack.
	/// </summary>
	public static Container And => new(OpCodes.And);

	/// <summary>
	/// Compute the bitwise complement of the two integer values on top of the stack
	/// and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container Or => new(OpCodes.Or);

	/// <summary>
	/// Computes the bitwise XOR of the top two values on the evaluation stack, pushing
	/// the result onto the evaluation stack.
	/// </summary>
	public static Container Xor => new(OpCodes.Xor);

	/// <summary>
	/// Shifts an integer value to the left (in zeroes) by a specified number of bits,
	/// pushing the result onto the evaluation stack.
	/// </summary>
	public static Container LeftShift => new(OpCodes.Shl);

	/// <summary>
	/// Shifts an integer value (in sign) to the right by a specified number of bits,
	/// pushing the result onto the evaluation stack.
	/// </summary>
	public static Container RightShift => new(OpCodes.Shr);

	/// <summary>
	/// Shifts an unsigned integer value (in zeroes) to the right by a specified number
	/// of bits, pushing the result onto the evaluation stack.
	/// </summary>
	public static Container RightShiftUnsigned => new(OpCodes.Shr_Un);

	/// <summary>
	/// Negates a value and pushes the result onto the evaluation stack.
	/// </summary>
	public static Container Negate => new(OpCodes.Neg);

	/// <summary>
	/// Computes the bitwise complement of the integer value on top of the stack and
	/// pushes the result onto the evaluation stack as the same type.
	/// </summary>
	public static Container Not => new(OpCodes.Not);

	/// <summary>
	/// Copies a specified number bytes from a source address to a destination address.
	/// </summary>
	public static Container CopyBlock => new(OpCodes.Cpblk);

	/// <summary>
	/// Copies the value type located at the address of an object (type &, or native int) to the address of the
	/// destination object (type &, or native int).
	/// </summary>
	/// <typeparam name="T">The type of the value</typeparam>
	public static Container CopyObject<T>() => CopyObject(typeof(T));

	/// <summary>
	/// Copies the value type located at the address of an object (type &, or native int) to the address of the
	/// destination object (type &, or native int).
	/// </summary>
	/// <param name="type">The type of the value</param>
	public static Container CopyObject(Type type)
	{
		Guard.IsNotNull(type);

		return new(OpCodes.Cpobj, type);
	}

	/// <summary>
	/// Loads a value of the specified type onto the evaluation stack indirectly.
	/// </summary>
	/// <typeparam name="T">The type of the value</typeparam>
	public static Container LoadIndirectly<T>() => LoadIndirectly(typeof(T));

	/// <summary>
	/// Loads a value of the specified type onto the evaluation stack indirectly.
	/// </summary>
	/// <param name="type">The type of the value</param>
	public static Container LoadIndirectly(Type? type)
		=> new(type is null ? OpCodes.Ldind_Ref
			: type.IsPointer ? OpCodes.Ldind_I
			: type.IsEnum ? OpCodes.Ldind_I4
			: _loadIndirectlyOpCodesByType.TryGetValue(type, out var value) ? value
			: OpCodes.Ldind_Ref);

	private static readonly Dictionary<Type, OpCode> _loadIndirectlyOpCodesByType = new()
	{
		{ typeof(sbyte), OpCodes.Ldind_I1 },
		{ typeof(bool), OpCodes.Ldind_I1 },
		{ typeof(short), OpCodes.Ldind_I2 },
		{ typeof(int), OpCodes.Ldind_I4 },
		{ typeof(long), OpCodes.Ldind_I8 },
		{ typeof(ulong), OpCodes.Ldind_I8 },
		{ typeof(float), OpCodes.Ldind_R4 },
		{ typeof(double), OpCodes.Ldind_R8 },
		{ typeof(byte), OpCodes.Ldind_U1 },
		{ typeof(ushort), OpCodes.Ldind_U2 },
		{ typeof(char), OpCodes.Ldind_U2 },
		{ typeof(uint), OpCodes.Ldind_U4 }
	};

	/// <summary>
	/// Stores a value of the specified type at a supplied address.
	/// </summary>
	/// <typeparam name="T">The type of the value</typeparam>
	public static Container StoreIndirectly<T>() => StoreIndirectly(typeof(T));

	/// <summary>
	/// Stores a value of the specified type at a supplied address.
	/// </summary>
	/// <param name="type">The type of the value</param>
	public static Container StoreIndirectly(Type? type)
		=> new(type is null ? OpCodes.Stind_Ref
			: type.IsPointer ? OpCodes.Stind_I
			: type.IsEnum ? OpCodes.Stind_I4
			: _storeIndirectlyOpCodesByType.TryGetValue(type, out var value) ? value
			: OpCodes.Stind_Ref);

	private static readonly Dictionary<Type, OpCode> _storeIndirectlyOpCodesByType = new()
	{
		{ typeof(sbyte), OpCodes.Stind_I1 },
		{ typeof(byte), OpCodes.Stind_I1 },
		{ typeof(bool), OpCodes.Stind_I1 },
		{ typeof(short), OpCodes.Stind_I2 },
		{ typeof(ushort), OpCodes.Stind_I2 },
		{ typeof(char), OpCodes.Stind_I2 },
		{ typeof(int), OpCodes.Stind_I4 },
		{ typeof(uint), OpCodes.Stind_I4 },
		{ typeof(long), OpCodes.Stind_I8 },
		{ typeof(ulong), OpCodes.Stind_I8 },
		{ typeof(float), OpCodes.Stind_R4 },
		{ typeof(double), OpCodes.Stind_R8 }
	};

	/// <summary>
	/// Replaces the array element at a given index with the value on the evaluation
	/// stack, whose type is specified in the instruction.
	/// </summary>
	/// <typeparam name="T">The element's type</typeparam>
	public static Container StoreElement<T>() => StoreElement(typeof(T));

	/// <summary>
	/// Replaces the array element at a given index with the value on the evaluation
	/// stack, whose type is specified in the instruction.
	/// </summary>
	/// <param name="type">The element's type</param>
	public static Container StoreElement(Type? type)
		=> type is null ? new(OpCodes.Stelem_Ref)
			: type.IsPointer ? new(OpCodes.Stelem_I)
			: !type.IsValueType ? new(OpCodes.Stelem_Ref)
			: _storeElementOpCodesByType.TryGetValue(type, out var value) ? new(value)
			: new(OpCodes.Stelem, type);

	private static readonly Dictionary<Type, OpCode> _storeElementOpCodesByType = new()
	{
		{ typeof(sbyte), OpCodes.Stelem_I1 },
		{ typeof(byte), OpCodes.Stelem_I1 },
		{ typeof(short), OpCodes.Stelem_I2 },
		{ typeof(ushort), OpCodes.Stelem_I2 },
		{ typeof(int), OpCodes.Stelem_I4 },
		{ typeof(uint), OpCodes.Stelem_I4 },
		{ typeof(long), OpCodes.Stelem_I8 },
		{ typeof(ulong), OpCodes.Stelem_I8 },
		{ typeof(float), OpCodes.Stelem_R4 },
		{ typeof(double), OpCodes.Stelem_R8 }
	};

	/// <summary>
	/// Loads the element at a specified array index onto the top of the evaluation stack
	/// as the type specified in the instruction.
	/// </summary>
	/// <typeparam name="T">The element's type</typeparam>
	public static Container LoadElement<T>() => LoadElement(typeof(T));

	/// <summary>
	/// Loads the element at a specified array index onto the top of the evaluation stack
	/// as the type specified in the instruction.
	/// </summary>
	/// <param name="type">The element's type</param>
	public static Container LoadElement(Type? type)
		=> type is null ? new(OpCodes.Ldelem_Ref)
			: type.IsPointer ? new(OpCodes.Ldelem_I)
			: !type.IsValueType ? new(OpCodes.Ldelem_Ref)
			: _loadElementOpCodesByType.TryGetValue(type, out var value) ? new(value)
			: new(OpCodes.Ldelem, type);

	private static readonly Dictionary<Type, OpCode> _loadElementOpCodesByType = new()
	{
		{ typeof(sbyte), OpCodes.Ldelem_I1 },
		{ typeof(short), OpCodes.Ldelem_I2 },
		{ typeof(int), OpCodes.Ldelem_I4 },
		{ typeof(long), OpCodes.Ldelem_I8 },
		{ typeof(ulong), OpCodes.Ldelem_I8 },
		{ typeof(float), OpCodes.Ldelem_R4 },
		{ typeof(double), OpCodes.Ldelem_R8 },
		{ typeof(byte), OpCodes.Ldelem_U1 },
		{ typeof(ushort), OpCodes.Ldelem_U2 },
		{ typeof(uint), OpCodes.Ldelem_U4 }
	};

	/// <summary>
	/// Loads the address of the array element at a specified array index onto the top
	/// of the evaluation stack as type & (managed pointer).
	/// </summary>
	/// <typeparam name="T">The element's type</typeparam>
	public static Container LoadElementAddress<T>() => LoadElementAddress(typeof(T));

	/// <summary>
	/// Loads the address of the array element at a specified array index onto the top
	/// of the evaluation stack as type & (managed pointer).
	/// </summary>
	/// <param name="type">The element's type</param>
	public static Container LoadElementAddress(Type type)
	{
		Guard.IsNotNull(type, nameof(type));

		return new(OpCodes.Ldelema, type);
	}

	/// <summary>
	/// Pushes the number of elements of a zero-based, one-dimensional array onto the
	/// evaluation stack.
	/// </summary>
	public static Container Length => new(OpCodes.Ldlen);

	/// <summary>
	/// Converts a metadata token to its runtime representation, pushing it onto the
	/// evaluation stack.
	/// </summary>
	/// <param name="memberInfo">The MemberInfo to push onto the stack</param>
	/// <exception cref="ArgumentNullException">memberInfo is null</exception>
	public static Container Token(MemberInfo memberInfo)
	{
		Guard.IsNotNull(memberInfo, nameof(memberInfo));

		return new(OpCodes.Ldtoken, memberInfo);
	}

	/// <summary>
	/// Attempts to cast an object passed by reference or convert the value on top
	/// of the evaluation stack to the specified class.
	/// </summary>
	/// <typeparam name="T">The type to cast or convert to</typeparam>
	public static Container Cast<T>() => Cast(typeof(T));

	/// <summary>
	/// Attempts to cast an object passed by reference or convert the value on top
	/// of the evaluation stack to the specified class.
	/// </summary>
	/// <param name="type">The type to cast or convert to</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	public static Container Cast(Type type)
	{
		Guard.IsNotNull(type);

		return type.IsValueType
			? Convert(type)
			: new(OpCodes.Castclass, type);
	}

	/// <summary>
	/// Pops a value from the stack and casts to the given type if possible pushing the result, otherwise pushes a null.
	/// </summary>
	/// <typeparam name="T">The type to cast to</typeparam>
	public static Container As<T>() => As(typeof(T));

	/// <summary>
	/// Pops a value from the stack and casts to the given type if possible pushing the result, otherwise pushes a null.
	/// </summary>
	/// <param name="type">The type to cast to</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	public static Container As(Type type)
	{
		Guard.IsNotNull(type);

		return new(OpCodes.Isinst, type);
	}

	/// <summary>
	/// Pops a value from the stack and casts to the given type if possible pushing the result, otherwise pushes a null.
	/// This is identical to `As`.
	/// </summary>
	/// <typeparam name="T">The type to cast to</typeparam>
	public static Container IsInstance<T>() => IsInstance(typeof(T));

	/// <summary>
	/// Pops a value from the stack and casts to the given type if possible pushing the result, otherwise pushes a null.
	/// This is identical to `As`.
	/// </summary>
	/// <param name="type">The type to cast to</param>
	/// <exception cref="ArgumentNullException">type is null</exception>
	public static Container IsInstance(Type type)
	{
		Guard.IsNotNull(type);

		return new(OpCodes.Isinst, type);
	}

	/// <summary>
	/// Converts the value on top of the evaluation stack to the given non-character primitive type.
	/// </summary>
	/// <typeparam name="T">The type to convert to</typeparam>
	public static Container Convert<T>() => Convert(typeof(T));

	/// <summary>
	/// Converts the value on top of the evaluation stack to the given non-character primitive type.
	/// </summary>
	/// <param name="type">The type to convert to</param>
	/// <exception cref="ArgumentException">Convert expects a non-character primitive type</exception>
	public static Container Convert(Type type)
	{
		Guard.IsNotNull(type, nameof(type));

		return new(!type.IsPrimitive || type == typeof(char)
			? ThrowHelper.ThrowArgumentException<OpCode>("Convert expects a non-character primitive type", nameof(type))
			: ConvertOpCodesByType.TryGetValue(type, out var value) ? value
			: ThrowHelper.ThrowArgumentException<OpCode>(
				$"Type {type.FullDescription()} cannot be converted to"));
	}

	public static readonly Dictionary<Type, OpCode> ConvertOpCodesByType = new()
	{
		{ typeof(byte), OpCodes.Conv_U1 },
		{ typeof(sbyte), OpCodes.Conv_I1 },
		{ typeof(bool), OpCodes.Conv_I1 },
		{ typeof(short), OpCodes.Conv_I2 },
		{ typeof(ushort), OpCodes.Conv_U2 },
		{ typeof(int), OpCodes.Conv_I4 },
		{ typeof(uint), OpCodes.Conv_U4 },
		{ typeof(long), OpCodes.Conv_I8 },
		{ typeof(ulong), OpCodes.Conv_U8 },
		{ typeof(IntPtr), OpCodes.Conv_I },
		{ typeof(UIntPtr), OpCodes.Conv_U },
		{ typeof(float), OpCodes.Conv_R4 },
		{ typeof(double), OpCodes.Conv_R8 }
	};

	/// <summary>
	/// Converts the primitive value on top of the evaluation stack to float32 as if it were unsigned.
	/// </summary>
	public static Container ConvertUnsignedToFloat() => new(OpCodes.Conv_R_Un);

	/// <summary>
	/// Converts the signed value on top of the evaluation stack to the given non-character primitive
	/// type, throwing System.OverflowException on overflow.
	/// </summary>
	/// <typeparam name="T">The type to convert to</typeparam>
	public static Container ConvertWithOverflowCheck<T>() => ConvertWithOverflowCheck(typeof(T));

	/// <summary>
	/// Converts the signed value on top of the evaluation stack to the given non-character primitive
	/// type, throwing System.OverflowException on overflow.
	/// </summary>
	/// <param name="type">The type to convert to</param>
	/// <exception cref="ArgumentException">ConvertWithOverflowCheck expects a non-character primitive type</exception>
	/// <exception cref="InvalidOperationException">float and double cannot be converted to with overflow checking</exception>
	public static Container ConvertWithOverflowCheck(Type type)
	{
		Guard.IsNotNull(type);

		return new(!type.IsPrimitive || type == typeof(char)
			? ThrowHelper.ThrowArgumentException<OpCode>(
				"ConvertWithOverflowCheck expects a non-character primitive type")
			: type == typeof(float)
				? ThrowHelper.ThrowInvalidOperationException<OpCode>(
					"There is no operation for converting to a float with overflow checking")
			: type == typeof(double)
				? ThrowHelper.ThrowInvalidOperationException<OpCode>(
					"There is no operation for converting to a double with overflow checking")
			: ConvertWithOverflowCheckOpCodesByType.TryGetValue(type, out var value) ? value
			: ThrowHelper.ThrowArgumentException<OpCode>(
				$"Type {type.FullDescription()} cannot be converted to"));
	}

	public static readonly Dictionary<Type, OpCode> ConvertWithOverflowCheckOpCodesByType = new()
	{
		{ typeof(byte), OpCodes.Conv_Ovf_U1 },
		{ typeof(sbyte), OpCodes.Conv_Ovf_I1 },
		{ typeof(bool), OpCodes.Conv_Ovf_I1 },
		{ typeof(short), OpCodes.Conv_Ovf_I2 },
		{ typeof(ushort), OpCodes.Conv_Ovf_U2 },
		{ typeof(int), OpCodes.Conv_Ovf_I4 },
		{ typeof(uint), OpCodes.Conv_Ovf_U4 },
		{ typeof(long), OpCodes.Conv_Ovf_I8 },
		{ typeof(ulong), OpCodes.Conv_Ovf_U8 },
		{ typeof(IntPtr), OpCodes.Conv_Ovf_I },
		{ typeof(UIntPtr), OpCodes.Conv_Ovf_U }
	};

	/// <summary>
	/// Converts the value on top of the evaluation stack to the given non-character primitive type
	/// as if it were unsigned, throwing System.OverflowException on overflow.
	/// </summary>
	/// <typeparam name="T">The type to convert to</typeparam>
	public static Container ConvertUnsignedWithOverflowCheck<T>() => ConvertUnsignedWithOverflowCheck(typeof(T));

	/// <summary>
	/// Converts the value on top of the evaluation stack to the given non-character primitive type
	/// as if it were unsigned, throwing System.OverflowException on overflow.
	/// </summary>
	/// <param name="type">The type to convert to</param>
	/// <exception cref="ArgumentException">ConvertUnsignedWithOverflowCheck expects a non-character primitive type</exception>
	/// <exception cref="InvalidOperationException">float and double cannot be converted to with overflow checking</exception>
	public static Container ConvertUnsignedWithOverflowCheck(Type type)
	{
		Guard.IsNotNull(type, nameof(type));

		return new(!type.IsPrimitive || type == typeof(char)
			? ThrowHelper.ThrowArgumentException<OpCode>(
				"ConvertUnsignedWithOverflowCheck expects a non-character primitive type")
			: type == typeof(float)
				? ThrowHelper.ThrowInvalidOperationException<OpCode>(
					"There is no operation for converting to a float with overflow checking")
			: type == typeof(double)
				? ThrowHelper.ThrowInvalidOperationException<OpCode>(
					"There is no operation for converting to a double with overflow checking")
			: ConvertUnsignedWithOverflowCheckOpCodesByType.TryGetValue(type, out var value) ? value
			: ThrowHelper.ThrowArgumentException<OpCode>(
				$"Type {type.FullDescription()} cannot be converted to"));
	}

	public static readonly Dictionary<Type, OpCode> ConvertUnsignedWithOverflowCheckOpCodesByType = new()
	{
		{ typeof(byte), OpCodes.Conv_Ovf_U1_Un },
		{ typeof(sbyte), OpCodes.Conv_Ovf_I1_Un },
		{ typeof(short), OpCodes.Conv_Ovf_I2_Un },
		{ typeof(ushort), OpCodes.Conv_Ovf_U2_Un },
		{ typeof(int), OpCodes.Conv_Ovf_I4_Un },
		{ typeof(uint), OpCodes.Conv_Ovf_U4_Un },
		{ typeof(long), OpCodes.Conv_Ovf_I8_Un },
		{ typeof(ulong), OpCodes.Conv_Ovf_U8_Un },
		{ typeof(IntPtr), OpCodes.Conv_Ovf_I_Un },
		{ typeof(UIntPtr), OpCodes.Conv_Ovf_U_Un }
	};

	public static bool CallReturns(this CodeInstruction instruction, Type type)
		=> (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
			&& ((MethodInfo)instruction.operand).ReturnType == type;

	public static bool FieldReturns(this CodeInstruction instruction, Type type)
		=> (instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldsfld)
			&& ((FieldInfo)instruction.operand).FieldType == type;

	public static bool Returns(this CodeInstruction instruction, Type type)
		=> instruction.CallReturns(type) || instruction.FieldReturns(type);

	public static int FirstArgumentIndex(MethodBase method, Type argumentType)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(argumentType);

		return !method.IsStatic && method.DeclaringType == argumentType
			? 0
			: FirstArgumentIndex(method, p => p.ParameterType == argumentType);
	}

	public static int FirstArgumentIndex(MethodBase method, Func<ParameterInfo, bool> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		var argument = method.GetParameters().FirstOrDefault(predicate);

		return argument != null
			? ArgumentIndex(argument)
			: ThrowHelper.ThrowInvalidOperationException<int>(
				$"No argument found for predicate {predicate.Method.FullDescription()} and method {method.FullDescription()}");
	}

	public static int ArgumentIndex(ParameterInfo parameter)
	{
		Guard.IsNotNull(parameter);

		return parameter.Position + (((MethodBase)parameter.Member).IsStatic ? 0 : 1);
	}

	public static IEnumerable<object> GetLocalOperandsOrIndices(CodeInstructions codes,
		Predicate<CodeInstruction> predicate)
	{
		Guard.IsNotNull(codes);
		Guard.IsNotNull(predicate);

		CodeInstruction? previousCode = null;
		foreach (var code in codes)
		{
			if (previousCode is not null && predicate(previousCode) && code.IsStloc() && code.opcode is var opcode)
			{
				yield return opcode.TryGetIndex()
					?? TryGetIndexFromOperand(code.operand)
					?? ThrowHelper.ThrowNotSupportedException<object>(
						$"{code.opcode} returned {code.operand?.ToString() ?? "null"}.");
			}

			previousCode = code;
		}
	}

	public static IEnumerable<int> GetLocalIndices(CodeInstructions codes, Predicate<CodeInstruction> predicate)
	{
		Guard.IsNotNull(codes);
		Guard.IsNotNull(predicate);

		foreach (var operand in GetLocalOperandsOrIndices(codes, predicate))
			yield return (int)operand;
	}

	public static IEnumerable<int> GetLocalIndices(MethodBase method, Predicate<LocalVariableInfo> predicate)
	{
		Guard.IsNotNull(method);
		Guard.IsNotNull(predicate);

		var methodBody = method.GetMethodBody();
		if (methodBody is null)
			ThrowHelper.ThrowArgumentException($"Method {method.FullDescription()} has no body");

		var variables = methodBody.LocalVariables;
		for (var i = 0; i < variables.Count; i++)
		{
			if (predicate(variables[i]))
				yield return variables[i].LocalIndex;
		}
	}

	public static CodeInstructions MethodReplacer<T, V>(this CodeInstructions instructions, T from, V to,
		bool throwOnFailure = true) where T : Delegate where V : Delegate
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(from);
		Guard.IsNotNull(to);

		var success = false;
		foreach (var instruction in instructions)
		{
			if (instruction.operand as MethodBase == from.Method)
			{
				instruction.opcode = to.Method.IsStatic ? OpCodes.Call : OpCodes.Callvirt;
				instruction.operand = to.Method;
				success = true;
			}

			yield return instruction;
		}

		if (throwOnFailure && !success)
		{
			ThrowHelper.ThrowInvalidOperationException(
				$"FishTranspiler.MethodReplacer couldn't find method {to.Method.FullDescription()} for {
					UtilityF.GetCallingAssembly().GetName().Name}, Version {
						UtilityF.GetCallingAssembly().GetName().Version}");
		}
	}

	public static CodeInstructions InsertBefore(this CodeInstructions instructions,
		Predicate<CodeInstruction> predicate, CodeInstruction instructionToInsert, bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(predicate);
		Guard.IsNotNull(instructionToInsert);

		return instructions.InsertBefore(predicate, new[] { instructionToInsert }, throwOnFailure);
	}

	public static CodeInstructions InsertBefore(this CodeInstructions instructions,
		Predicate<CodeInstruction> predicate, CodeInstructions instructionsToInsert, bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(predicate);
		UtilityF.ThrowIfNullOrEmpty(instructionsToInsert);

		var success = false;
		foreach (var code in instructions)
		{
			if (predicate(code))
			{
				foreach (var instruction in instructionsToInsert)
					yield return instruction;

				success = true;
			}

			yield return code;
		}

		if (throwOnFailure && !success)
		{
			ThrowHelper.ThrowInvalidOperationException(
				$"FishTranspiler.InsertBefore couldn't find target instruction for {
					predicate.Method.FullDescription()} of {UtilityF.GetCallingAssembly().GetName().Name}, Version {
						UtilityF.GetCallingAssembly().GetName().Version}");
		}
	}

	public static CodeInstructions InsertAfter(this CodeInstructions instructions, Predicate<CodeInstruction> predicate,
		CodeInstruction instructionToInsert, bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(predicate);
		Guard.IsNotNull(instructionToInsert);

		return instructions.InsertAfter(predicate, new[] { instructionToInsert }, throwOnFailure);
	}

	public static CodeInstructions InsertAfter(this CodeInstructions instructions, Predicate<CodeInstruction> predicate,
		CodeInstructions instructionsToInsert, bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(predicate);
		UtilityF.ThrowIfNullOrEmpty(instructionsToInsert);

		var success = false;
		foreach (var code in instructions)
		{
			yield return code;

			if (!predicate(code))
				continue;

			foreach (var instruction in instructionsToInsert)
				yield return instruction;

			success = true;
		}

		if (throwOnFailure && !success)
		{
			ThrowHelper.ThrowInvalidOperationException(
				$"FishTranspiler.InsertAfter couldn't find target instruction for {
					predicate.Method.FullDescription()} of {UtilityF.GetCallingAssembly().GetName().Name}, Version {
						UtilityF.GetCallingAssembly().GetName().Version}");
		}
	}

	public static CodeInstructions Replace(this CodeInstructions instructions, Predicate<CodeInstruction> predicate,
		Func<CodeInstruction, CodeInstruction> replacement, bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(predicate);
		Guard.IsNotNull(replacement);

		return instructions.Replace(predicate, c => new[] { replacement(c) }, throwOnFailure);
	}

	public static CodeInstructions Replace(this CodeInstructions instructions, Predicate<CodeInstruction> predicate,
		Func<CodeInstruction, CodeInstructions> replacement, bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(predicate);
		Guard.IsNotNull(replacement);

		var success = false;
		foreach (var code in instructions)
		{
			if (predicate(code))
			{
				foreach (var instruction in replacement(code))
					yield return instruction;

				success = true;
			}
			else
				yield return code;
		}

		if (throwOnFailure && !success)
		{
			ThrowHelper.ThrowInvalidOperationException(
				$"FishTranspiler.Replace couldn't find target instruction for {
					predicate.Method.FullDescription()} of {UtilityF.GetCallingAssembly().GetName().Name}, Version {
						UtilityF.GetCallingAssembly().GetName().Version}");
		}
	}

	public static CodeInstructions ReplaceAt(this CodeInstructions instructions,
		Func<List<CodeInstruction>, int, bool> position, Func<CodeInstruction, CodeInstruction> replacement,
		bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(position);
		Guard.IsNotNull(replacement);

		return instructions.ReplaceAt(position, c => new[] { replacement(c) }, throwOnFailure);
	}

	public static CodeInstructions ReplaceAt(this CodeInstructions instructions,
		Func<List<CodeInstruction>, int, bool> position, Func<CodeInstruction, CodeInstructions> replacement,
		bool throwOnFailure = true)
	{
		Guard.IsNotNull(instructions);
		Guard.IsNotNull(position);
		Guard.IsNotNull(replacement);

		var success = false;
		var codes = instructions as List<CodeInstruction> ?? instructions.ToList();
		for (var i = 0; i < codes.Count; i++)
		{
			if (!position(codes, i))
				continue;

			var code = codes[i];
			codes.RemoveAt(i);

			var replacementList = replacement(code).ToList();
			codes.InsertRange(i, replacementList);
			i += replacementList.Count - 1;
			success = true;
		}

		if (throwOnFailure && !success)
		{
			ThrowHelper.ThrowInvalidOperationException(
				$"FishTranspiler.ReplaceAt couldn't find target instruction for {
					position.Method.FullDescription()} of {UtilityF.GetCallingAssembly().GetName().Name}, Version {
					UtilityF.GetCallingAssembly().GetName().Version}");
		}

		return codes;
	}

	public static CodeInstruction With(this CodeInstruction instruction, OpCode? opcode = null, object? operand = null,
		IEnumerable<Label>? labels = null, IEnumerable<ExceptionBlock>? blocks = null)
	{
		Guard.IsNotNull(instruction);

		if (opcode != null)
			instruction.opcode = opcode.Value;

		if (operand != null)
			instruction.operand = operand;

		if (labels != null)
			instruction.labels.AddRange(labels);

		if (blocks != null)
			instruction.blocks.AddRange(blocks);

		return instruction;
	}

	public static bool LoadsLocalVariable(this OpCode opcode)
		=> CodeInstructionExtensions.loadVarCodes.Contains(opcode);

	public static OpCode ToLoadLocalVariable(this OpCode opcode)
		=> opcode.LoadsLocalVariable() && opcode != OpCodes.Ldarga && opcode != OpCodes.Ldarga_S ? opcode
			: opcode.TryGetIndex() is { } index ? GetLoadLocalOpCode(index)
			: opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S
			: opcode == OpCodes.Stloc ? OpCodes.Ldloc
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"{opcode} cannot be cast to Ldloc.");

	public static bool StoresLocalVariable(this OpCode opcode)
		=> CodeInstructionExtensions.storeVarCodes.Contains(opcode);

	public static OpCode ToStoreLocalVariable(this OpCode opcode)
		=> opcode.StoresLocalVariable() ? opcode
			: opcode.TryGetIndex() is { } index ? GetStoreLocalOpCode(index)
			: opcode == OpCodes.Ldloc_S ? OpCodes.Stloc_S
			: opcode == OpCodes.Ldloc ? OpCodes.Stloc
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"{opcode} cannot be cast to Stloc");

	public static bool LoadsElement(this OpCode opcode) => LoadElementOpCodes.Contains(opcode);

	public static bool StoresElement(this OpCode opcode) => StoreElementOpCodes.Contains(opcode);

	public static readonly HashSet<OpCode> LoadElementOpCodes =
	[
		OpCodes.Ldelem, OpCodes.Ldelema, OpCodes.Ldelem_I, OpCodes.Ldelem_I1, OpCodes.Ldelem_I2, OpCodes.Ldelem_I4,
		OpCodes.Ldelem_I8, OpCodes.Ldelem_R4, OpCodes.Ldelem_R8, OpCodes.Ldelem_Ref, OpCodes.Ldelem_U1,
		OpCodes.Ldelem_U2, OpCodes.Ldelem_U4
	];

	public static readonly HashSet<OpCode> StoreElementOpCodes =
	[
		OpCodes.Stelem, OpCodes.Stelem_I, OpCodes.Stelem_I1, OpCodes.Stelem_I2, OpCodes.Stelem_I4,
		OpCodes.Stelem_I8, OpCodes.Stelem_R4, OpCodes.Stelem_R8, OpCodes.Stelem_Ref
	];

	public static bool LoadsByRef(this OpCode opcode) => LoadByRefOpCodes.Contains(opcode);

	public static bool StoresByRef(this OpCode opcode) => StoreByRefOpCodes.Contains(opcode);

	public static readonly HashSet<OpCode> LoadByRefOpCodes =
	[
		OpCodes.Ldobj, OpCodes.Ldind_I, OpCodes.Ldind_I1, OpCodes.Ldind_I2, OpCodes.Ldind_I4, OpCodes.Ldind_I8,
		OpCodes.Ldind_R4, OpCodes.Ldind_R8, OpCodes.Ldind_Ref, OpCodes.Ldind_U1, OpCodes.Ldind_U2, OpCodes.Ldind_U4
	];

	public static readonly HashSet<OpCode> StoreByRefOpCodes =
	[
		OpCodes.Stobj, OpCodes.Stind_I, OpCodes.Stind_I1, OpCodes.Stind_I2, OpCodes.Stind_I4, OpCodes.Stind_I8,
		OpCodes.Stind_R4, OpCodes.Stind_R8, OpCodes.Stind_Ref
	];

	public static bool LoadsField(this OpCode opcode) => opcode.LoadsInstanceField() || opcode.LoadsStaticField();

	public static bool LoadsInstanceField(this OpCode opcode) => opcode == OpCodes.Ldfld || opcode == OpCodes.Ldflda;

	public static bool LoadsStaticField(this OpCode opcode) => opcode == OpCodes.Ldsfld || opcode == OpCodes.Ldsflda;

	public static OpCode ToLoadField(this OpCode opcode)
		=> opcode.LoadsField() ? opcode
			: opcode == OpCodes.Stfld ? OpCodes.Ldfld
			: opcode == OpCodes.Stsfld ? OpCodes.Ldsfld
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"{opcode} cannot be cast to Ldfld");

	public static bool StoresField(this OpCode opcode) => opcode.StoresStaticField() || opcode.StoresInstanceField();

	public static bool StoresStaticField(this OpCode opcode) => opcode == OpCodes.Stsfld;

	public static bool StoresInstanceField(this OpCode opcode) => opcode == OpCodes.Stfld;

	public static OpCode ToStoreField(this OpCode opcode)
		=> opcode.StoresField() ? opcode
			: opcode == OpCodes.Ldfld ? OpCodes.Stfld
			: opcode == OpCodes.Ldsfld ? OpCodes.Stsfld
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"{opcode} cannot be cast to Stfld");

	public static bool Calls(this OpCode opcode)
		=> opcode == OpCodes.Call
			|| opcode == OpCodes.Calli
			|| opcode == OpCodes.Callvirt;

	public static bool LoadsConstant(this OpCode opcode)
		=> CodeInstructionExtensions.constantLoadingCodes.Contains(opcode)
			|| opcode == OpCodes.Ldstr
			|| opcode == OpCodes.Ldnull
			|| opcode == OpCodes.Ldtoken;

	public static bool LoadsNull(this OpCode opcode) => opcode == OpCodes.Ldnull;

	public static bool LoadsString(this OpCode opcode) => opcode == OpCodes.Ldstr;

	public static bool Branches(this OpCode opcode)
		=> CodeInstructionExtensions.branchCodes.Contains(opcode)
			|| opcode == OpCodes.Switch;

	public static bool Compares(this OpCode opcode) => CompareOpCodes.Contains(opcode);

	public static readonly HashSet<OpCode> CompareOpCodes
		= [OpCodes.Ceq, OpCodes.Cgt, OpCodes.Cgt_Un, OpCodes.Clt, OpCodes.Clt_Un];

	public static bool Computes(this OpCode opcode) => ComputeOpCodes.Contains(opcode);

	public static readonly HashSet<OpCode> ComputeOpCodes =
	[
		OpCodes.Add, OpCodes.Add_Ovf, OpCodes.Add_Ovf_Un, OpCodes.Sub, OpCodes.Sub_Ovf, OpCodes.Sub_Ovf_Un,
		OpCodes.Mul, OpCodes.Mul_Ovf, OpCodes.Mul_Ovf_Un, OpCodes.Div, OpCodes.Div_Un, OpCodes.Rem, OpCodes.Rem_Un,
		OpCodes.And, OpCodes.Or, OpCodes.Xor, OpCodes.Shl, OpCodes.Shr, OpCodes.Shr_Un, OpCodes.Not, OpCodes.Neg
	];

	public static bool Converts(this OpCode opcode) => ConvertOpCodes.Contains(opcode);

	public static readonly HashSet<OpCode> ConvertOpCodes =
	[
		OpCodes.Conv_I, OpCodes.Conv_I1, OpCodes.Conv_I2, OpCodes.Conv_I4, OpCodes.Conv_I8, OpCodes.Conv_Ovf_I,
		OpCodes.Conv_Ovf_I1, OpCodes.Conv_Ovf_I1_Un, OpCodes.Conv_Ovf_I2, OpCodes.Conv_Ovf_I2_Un,
		OpCodes.Conv_Ovf_I4, OpCodes.Conv_Ovf_I4_Un, OpCodes.Conv_Ovf_I8, OpCodes.Conv_Ovf_I8_Un,
		OpCodes.Conv_Ovf_I_Un, OpCodes.Conv_Ovf_U, OpCodes.Conv_Ovf_U1, OpCodes.Conv_Ovf_U1_Un, OpCodes.Conv_Ovf_U2,
		OpCodes.Conv_Ovf_U2_Un, OpCodes.Conv_Ovf_U4, OpCodes.Conv_Ovf_U4_Un, OpCodes.Conv_Ovf_U8,
		OpCodes.Conv_Ovf_U8_Un, OpCodes.Conv_Ovf_U_Un, OpCodes.Conv_R4, OpCodes.Conv_R8, OpCodes.Conv_R_Un,
		OpCodes.Conv_U, OpCodes.Conv_U1, OpCodes.Conv_U2, OpCodes.Conv_U4, OpCodes.Conv_U8, OpCodes.Box,
		OpCodes.Unbox, OpCodes.Unbox_Any
	];

	public static bool LoadsArgument(this OpCode opcode) => LoadArgumentOpCodes.Contains(opcode);

	public static readonly HashSet<OpCode> LoadArgumentOpCodes =
	[
		OpCodes.Ldarg, OpCodes.Ldarga, OpCodes.Ldarga_S, OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2,
		OpCodes.Ldarg_3, OpCodes.Ldarg_S
	];

	public static OpCode ToStoreArgument(this OpCode opcode)
		=> opcode.StoresArgument() ? opcode
			: opcode.TryGetIndex() is { } index ? GetStoreArgumentOpCode(index)
			: opcode == OpCodes.Ldarg_S || opcode == OpCodes.Ldarga_S ? OpCodes.Starg_S
			: opcode == OpCodes.Ldarg || opcode == OpCodes.Ldarga ? OpCodes.Starg
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"{opcode} cannot be cast to Starg");

	public static OpCode ToLoadArgument(this OpCode opcode)
		=> opcode.LoadsArgument() && opcode != OpCodes.Ldarga && opcode != OpCodes.Ldarga_S ? opcode
			: opcode.TryGetIndex() is { } index ? GetLoadArgumentOpCode(index)
			: opcode == OpCodes.Starg_S ? OpCodes.Ldarg_S
			: opcode == OpCodes.Starg ? OpCodes.Ldarg
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"{opcode} cannot be cast to Ldarg");

	public static OpCode GetLoadArgumentOpCode(int index)
		=> index switch
		{
			0 => OpCodes.Ldarg_0,
			1 => OpCodes.Ldarg_1,
			2 => OpCodes.Ldarg_2,
			3 => OpCodes.Ldarg_3,
			< 256 => OpCodes.Ldarg_S,
			_ => OpCodes.Ldarg
		};

	public static OpCode GetLoadArgumentAddressOpCode(int index) => index < 256 ? OpCodes.Ldarga_S : OpCodes.Ldarga;

	public static bool StoresArgument(this OpCode opcode)
		=> opcode == OpCodes.Starg
			|| opcode == OpCodes.Starg_S;

	public static OpCode GetStoreArgumentOpCode(int index)
		=> index switch
		{
			< 256 => OpCodes.Starg_S,
			_ => OpCodes.Starg
		};

	public static OpCode GetStoreLocalOpCode(int index)
		=> index switch
		{
			0 => OpCodes.Stloc_0,
			1 => OpCodes.Stloc_1,
			2 => OpCodes.Stloc_2,
			3 => OpCodes.Stloc_3,
			< 256 => OpCodes.Stloc_S,
			_ => OpCodes.Stloc
		};

	public static OpCode GetLoadLocalOpCode(int index)
		=> index switch
		{
			0 => OpCodes.Ldloc_0,
			1 => OpCodes.Ldloc_1,
			2 => OpCodes.Ldloc_2,
			3 => OpCodes.Ldloc_3,
			< 256 => OpCodes.Ldloc_S,
			_ => OpCodes.Ldloc
		};

	public static OpCode GetLoadConstantOpCode(int index)
		=> index switch
		{
			>= -1 and <= 8 => _numberedLoadConstantOpCodes[index],
			< 128 and > -129 => OpCodes.Ldc_I4_S,
			_ => OpCodes.Ldc_I4
		};

	private static readonly Dictionary<int, OpCode> _numberedLoadConstantOpCodes = new()
	{
		{ -1, OpCodes.Ldc_I4_M1 },
		{ 0, OpCodes.Ldc_I4_0 },
		{ 1, OpCodes.Ldc_I4_1 },
		{ 2, OpCodes.Ldc_I4_2 },
		{ 3, OpCodes.Ldc_I4_3 },
		{ 4, OpCodes.Ldc_I4_4 },
		{ 5, OpCodes.Ldc_I4_5 },
		{ 6, OpCodes.Ldc_I4_6 },
		{ 7, OpCodes.Ldc_I4_7 },
		{ 8, OpCodes.Ldc_I4_8 }
	};

	public static readonly HashSet<OpCode> LoadConstantOpCodes
		= _numberedLoadConstantOpCodes.Values.Append(OpCodes.Ldc_I4_S).Append(OpCodes.Ldc_I4).ToHashSet();

	public static OpCode ToAddress(this OpCode opcode)
		=> opcode == OpCodes.Ldarg_0
			|| opcode == OpCodes.Ldarg_1
			|| opcode == OpCodes.Ldarg_2
			|| opcode == OpCodes.Ldarg_3
			|| opcode == OpCodes.Ldarg_S ? OpCodes.Ldarga_S
			: opcode == OpCodes.Ldarg ? OpCodes.Ldarga
			: opcode == OpCodes.Ldloc_0
			|| opcode == OpCodes.Ldloc_1
			|| opcode == OpCodes.Ldloc_2
			|| opcode == OpCodes.Ldloc_3
			|| opcode == OpCodes.Ldloc_S ? OpCodes.Ldloca_S
			: opcode == OpCodes.Ldloc ? OpCodes.Ldloca
			: opcode == OpCodes.Ldfld ? OpCodes.Ldflda
			: opcode == OpCodes.Ldsfld ? OpCodes.Ldsflda
			: ThrowHelper.ThrowInvalidOperationException<OpCode>($"Cannot cast {opcode} to address opcode");

	public static int? TryGetIndex(this OpCode opcode)
		=> _opCodeIndices.TryGetValue(opcode, out var value) ? value : null;

	private static readonly Dictionary<OpCode, int> _opCodeIndices = new()
	{
		{ OpCodes.Ldarg_0, 0 },
		{ OpCodes.Ldarg_1, 1 },
        { OpCodes.Ldarg_2, 2 },
        { OpCodes.Ldarg_3, 3 },
        { OpCodes.Ldloc_0, 0 },
        { OpCodes.Ldloc_1, 1 },
        { OpCodes.Ldloc_2, 2 },
        { OpCodes.Ldloc_3, 3 },
        { OpCodes.Stloc_0, 0 },
        { OpCodes.Stloc_1, 1 },
        { OpCodes.Stloc_2, 2 },
        { OpCodes.Stloc_3, 3 },
        { OpCodes.Ldc_I4_0, 0 },
        { OpCodes.Ldc_I4_1, 1 },
        { OpCodes.Ldc_I4_2, 2 },
        { OpCodes.Ldc_I4_3, 3 },
        { OpCodes.Ldc_I4_4, 4 },
        { OpCodes.Ldc_I4_5, 5 },
        { OpCodes.Ldc_I4_6, 6 },
        { OpCodes.Ldc_I4_7, 7 },
        { OpCodes.Ldc_I4_8, 8 },
        { OpCodes.Ldc_I4_M1, -1 }
	};

	public static object? GetOperandFromIndex(int index) => index > 3 ? index : null;

	public static object? GetOperandOfConstant([SuppressMessage("Naming", "CA1720")] int integer)
		=> integer is < 9 and > -2 ? null : integer;

	public static object? GetOperandFromBuilder(LocalBuilder builder)
	{
		Guard.IsNotNull(builder);

		return builder.LocalIndex > 3 ? builder : null;
	}

	public static IList<LocalVariableInfo> GetLocalVariables(this MethodBase method)
	{
		var methodBody = method.GetMethodBody();
		if (methodBody is null)
			ThrowHelper.ThrowArgumentException($"Method {method.FullDescription()} has no body.");
		
		return methodBody.LocalVariables;
	}

	public static int? TryGetIndexFromOperand(object? obj)
		=> obj is LocalVariableInfo info ? info.LocalIndex
			: obj is byte @byte ? @byte
			: obj is ushort @ushort ? @ushort
			: obj is int @int ? @int
			: obj is not string and IConvertible convertible ? convertible.ToInt32(CultureInfo.InvariantCulture)
			: int.TryParse(obj?.ToString(), out var parsedNumber) ? parsedNumber
			: null;

	public static bool CompareOperands(object? lhs, object? rhs)
		=> TryGetIndexFromOperand(lhs) is { } lhIndex && TryGetIndexFromOperand(rhs) is { } rhIndex
			? lhIndex == rhIndex
			: lhs?.Equals(rhs) ?? rhs is null;

	public static void Emit(this ILGenerator generator, Container fishTranspiler)
	{
		Guard.IsNotNull(generator);

		switch (fishTranspiler.Operand)
		{
			case null:
				generator.Emit(fishTranspiler.OpCode);
				break;
			case byte byteCase:
				generator.Emit(fishTranspiler.OpCode, byteCase);
				break;
			case ConstructorInfo conCase:
				generator.Emit(fishTranspiler.OpCode, conCase);
				break;
			case double doubleCase:
				generator.Emit(fishTranspiler.OpCode, doubleCase);
				break;
			case FieldInfo fieldCase:
				generator.Emit(fishTranspiler.OpCode, fieldCase);
				break;
			case short shortCase:
				generator.Emit(fishTranspiler.OpCode, shortCase);
				break;
			case int intCase:
				generator.Emit(fishTranspiler.OpCode, intCase);
				break;
			case long longCase:
				generator.Emit(fishTranspiler.OpCode, longCase);
				break;
			case Label labelCase:
				generator.Emit(fishTranspiler.OpCode, labelCase);
				break;
			case Label[] labelsCase:
				generator.Emit(fishTranspiler.OpCode, labelsCase);
				break;
			case LocalBuilder localCase:
				generator.Emit(fishTranspiler.OpCode, localCase);
				break;
			case MethodInfo methCase:
				generator.Emit(fishTranspiler.OpCode, methCase);
				break;
			case sbyte sbyteCase:
				generator.Emit(fishTranspiler.OpCode, sbyteCase);
				break;
			case SignatureHelper signatureCase:
				generator.Emit(fishTranspiler.OpCode, signatureCase);
				break;
			case float floatCase:
				generator.Emit(fishTranspiler.OpCode, floatCase);
				break;
			case string stringCase:
				generator.Emit(fishTranspiler.OpCode, stringCase);
				break;
			case Type typeCase:
				generator.Emit(fishTranspiler.OpCode, typeCase);
				break;
			case LocalVariableInfo localInfo:
				generator.Emit(fishTranspiler.OpCode, localInfo.LocalIndex);
				break;
			default:
				ThrowHelper.ThrowArgumentException(
					$"Invalid FishTranspiler operand for Emit method: {fishTranspiler.Operand}");
				break;
		}
	}
}