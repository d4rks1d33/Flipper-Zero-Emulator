//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;

namespace ModelsAnalyzer;

/*
    These are the diagnostic rules for all analyzers
    They are here, so it's easier to keep track of them
*/
internal static class Rules
{
#pragma warning disable RS2008 // Enable analyzer release tracking

    internal static DiagnosticDescriptor TestErrorRule =
        new(
            "TEST001",
            "This is a test error rule",
            "Test error rule",
            "renode.Peripherals.TestErrorRule",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "This should not exist in Release build.");

    internal static DiagnosticDescriptor NoDiagnosticsAvailable =
        new(
            "TEST002",
            "No diagnostics should be reported by this analyzer",
            "No diagnostics should be reported by this analyzer",
            "renode.Peripherals.NoDiagnosticsAvailable",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            description: "No diagnostics should be reported by this analyzer. This rule is meant to trigger analyzers that report no diagnostics.");

    internal static DiagnosticDescriptor RuleNoExplicitMemberValue =
        new(
            "REN001",
            "Registers enum member has no explicit value",
            "{0} has no explicit value, {1} inferred",
            "renode.Peripherals.RegistersEnum",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Set the value explicitly.");

    internal static DiagnosticDescriptor RuleNotLongUnderlyingType =
        new(
            "REN002",
            "Registers enum underlying is not long",
            "Enum {0} underlying type is not long",
            "renode.Peripherals.RegistersEnum",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "Set the Registers underlying type to long.");

    internal static DiagnosticDescriptor RuleNoRegistersEnum =
        new(
            "REN003",
            "Registers enum is not present in the current file",
            "Registers enum is not present in the current file",
            "renode.Peripherals.RegistersEnum",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Create Registers enum describing registers layout.");


    internal static DiagnosticDescriptor RuleRegisterDefinitelyUnused =
        new(
            "REN004",
            "This register is definitely unused",
            "Register {0} is definitely unused",
            "renode.Peripherals.Registers",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The register is not used properly.");

    internal static DiagnosticDescriptor RuleRegisterProbablyUnused =
        new(
            "REN005",
            "This register is probably unused",
            "Register {0} is probably unused",
            "renode.Peripherals.Registers",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The register is not used properly.");

    internal static DiagnosticDescriptor RuleRegisterDefinedInDictSyntax =
        new(
            "REN006",
            "This register is defined in dict syntax",
            "Register {0} is defined in dict syntax",
            "renode.Peripherals.Registers",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            description: "The register uses dictionary syntax.");

    internal static DiagnosticDescriptor RuleRegisterDefinedInDeclarativeSyntax =
        new(
            "REN007",
            "This register is defined in declarative syntax",
            "Register {0} is defined in declarative syntax",
            "renode.Peripherals.Registers",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            description: "The register uses declarative syntax ( Registers.X.Define() ).");

    internal static DiagnosticDescriptor RuleRegisterDefinedInSwitchSyntax =
        new(
            "REN008",
            "This register is defined in switch syntax",
            "Register {0} is defined in switch syntax",
            "renode.Peripherals.Registers",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            description: "The register uses switch syntax ( switch() case Register.X: ).");

    internal static DiagnosticDescriptor RuleRegisterNotDefinedInDeclarativeSyntax =
        new(
            "REN009",
            "This register is not defined in declarative syntax",
            "Register {0} is not defined in declarative syntax",
            "renode.Peripherals.Registers",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The register does not use declarative syntax.");

    internal static DiagnosticDescriptor RuleNoResetMethod =
        new(
            "REN010",
            "This peripheral has no Reset() method",
            "Peripheral {0} has no Reset() method",
            "renode.Peripherals.Reset",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "This peripheral needs to define Reset() method.");

    internal static DiagnosticDescriptor RuleMemberNotReset =
        new(
            "REN011",
            "This member is not reset",
            "Member {0} is not reset",
            "renode.Peripherals.Reset",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "This member is not reset. You should reset it within Reset() method or make it immutable.");

    internal static DiagnosticDescriptor RuleRegisterFieldsOverlapping =
        new(
            "REN012",
            "Register's fields are overlapping",
            "Field \"{0}\" and \"{1}\" are overlapping in Register \"{2}\"",
            "renode.Peripherals.Registers.Fields",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Register's fields should not have intersecting ranges.");

    internal static DiagnosticDescriptor RuleRegisterGapsInFieldCoverage =
        new(
            "REN013",
            "There are gaps in register fields coverage",
            "Gap between bits \"{0}\" and \"{1}\" exists in Register \"{2}\", layout variant \"{3}\"",
            "renode.Peripherals.Registers.Fields",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Register's fields should cover the entire register. Use ReservedBits or Tags if necessary.");
}