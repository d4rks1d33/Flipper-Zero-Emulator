//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Text;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using System.Linq;

namespace ModelsAnalyzer;

public static class RegisterFieldAnalysisHelpers
{
    public record class RegisterGroup(string Name, List<RegisterInfo> Registers);

    [Flags]
    // This is unused now
    public enum RegisterSpecialKind
    {
        None = 0,
        /// <summary> This register might be undefined (exists only as declaration) </summary>
        MaybeUndefined = 1 << 0,
        /// <summary> This register is does not contain .Define invocation </summary>
        /// <remarks> Note, that it is not the same as MaybeUndefined - the register can be handled in other way, e.g. in a switch </remarks>
        NoDefineFound = 1 << 1,
    }

    public record struct CallbackInfo
    (
        bool HasReadCb = false,
        bool HasWriteCb = false,
        bool HasChangeCb = false,
        bool HasValueProviderCb = false
    );

    public record struct ArrayOfRegisters(bool IsArray = false, int Length = 0, int Stride = 0); // Whether the register is replicated many times (DefineMany or a for loop)

    public record class RegisterInfo
    (
        string Name, // This is the name stated in definition, unless there is none - then same as OriginalName
        string OriginalName, // This is the name stated in "Registers" enum
        long Address,
        int? Width = null,
        long? ResetValue = null,
        RegisterSpecialKind SpecialKind = RegisterSpecialKind.None,
        CallbackInfo CallbackInfo = new CallbackInfo(),
        string? ParentReg = null,
        ArrayOfRegisters ArrayInfo = new ArrayOfRegisters()
    )
    {
        public List<RegisterFieldInfo> Fields { get; init; } = new List<RegisterFieldInfo>();
        public override string ToString()
        {
            var rets = new StringBuilder();
            rets.Append(this.GetType().Name);
            rets.Append(" { ");
            rets.Append($"Name={Name} ");
            rets.Append($"Width={Width} ");
            rets.Append($"Address={Address} ");
            rets.Append($"Kind={SpecialKind} ");
            //rets.Append($"Kind={SpecialKind} ");
            rets.Append("\n\t");

            rets.Append(Fields.ForEachAndJoinToString("\n\t"));

            rets.AppendLine();
            rets.Append("} ");

            return rets.ToString();
        }
    }

    [Flags]
    public enum RegisterFieldInfoSpecialKind
    {
        None = 0,
        /// <summary> This register field is reserved - it should implement no functionality, and might not have a name </summary>
        Reserved = 1 << 0,
        /// <summary> This register field's length cannot be determined at compile time (is not a constant value?) </summary>
        VariableLength = 1 << 1,
        /// <summary> This register field's position cannot be determined at compile time </summary>
        VariablePosition = 1 << 2,
        /// <summary> Is defined "WithIgnoredX" </summary>
        Ignored = 1 << 3,
        /// <summary> This register is a tag </summary>
        Tag = 1 << 4,
        /// <summary> Field mode depends on runtime variable </summary>
        VariableAccessMode = 1 << 5,
    }

    /// <remarks> Ranges should be inclusive </remarks>
    public record struct RegisterFieldRange
    (
        int Start,
        int End
    )
    {
        public static implicit operator RegisterFieldRange(Range range)
        {
            if(range.Start.IsFromEnd || range.End.IsFromEnd)
            {
                throw new ArgumentException("Invalid conversion: Range cannot be from end");
            }
            return new RegisterFieldRange(range.Start.Value, range.End.Value);
        }
    }

    public record class RegisterFieldInfo
    (
        uint UniqueId,
        RegisterFieldRange Range,
        string Name,
        string GeneratorName,
        [property: JsonIgnore] Location Location,
        RegisterFieldInfoSpecialKind SpecialKind = RegisterFieldInfoSpecialKind.None,
        CallbackInfo CallbackInfo = new CallbackInfo(),
        List<string>? FieldMode = null
    )
    {
        // Id has no meaning, other than separates fields between code blocks
        // it can improve report visibility, since usually in separate blocks there might be conflicting fields (conditionally defined)
        public uint BlockId { get; set; } = 0;
    }
}