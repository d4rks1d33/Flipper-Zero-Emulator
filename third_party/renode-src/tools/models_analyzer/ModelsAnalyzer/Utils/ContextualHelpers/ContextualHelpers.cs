//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModelsAnalyzer;

/*
    These are helpers that don't just operate on general Roslyn data, 
    but depend on some additional information on Renode Framework internals
*/
public static class ContextualHelpers
{
    public static bool CanBeRegistersEnum(EnumDeclarationSyntax node)
    {
        var text = node.Identifier.Text;
        return text.Contains("Register", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Offset", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsClassAPeripheral(INamedTypeSymbol cls)
    {
        return TypeHelpers.DoesImplementInterface(cls, "Antmicro.Renode.Peripherals.IPeripheral");
    }

    public static ImmutableArray<(ClassDeclarationSyntax, INamedTypeSymbol)> GetAllPeripheralClasses(SemanticModel semanticModel)
    {
        var classes = semanticModel.SyntaxTree.GetAllClasses();

        var peripheralsClasses = new List<(ClassDeclarationSyntax, INamedTypeSymbol)>();
        foreach(var cls in classes)
        {
            if(semanticModel.GetDeclaredSymbol(cls) is not INamedTypeSymbol clsSymbol)
            {
                throw new Exception($"Unexpected error: class symbol {cls.ToFullString()} not resolved to NamedTypeSymbol.");
            }
            if(ContextualHelpers.IsClassAPeripheral(clsSymbol))
            {
                peripheralsClasses.Add((cls, clsSymbol));
            }
            else
            {
                Logger.Trace("Class {name} is not a peripheral. Skipping!", clsSymbol.Name);
            }
        }

        return peripheralsClasses.ToImmutableArray();
    }

    // Symbols from these locations are considered "Peripheral Extensions" i. e. when they are executed on Registers symbols,
    // they define these registers, using the declarative API
    public static readonly string[] RegisterDefinitionExtensionsLocations =
    {
        "Antmicro.Renode.Peripherals.BasicBytePeripheralExtensions",
        "Antmicro.Renode.Peripherals.BasicWordPeripheralExtensions",
        "Antmicro.Renode.Peripherals.BasicDoubleWordPeripheralExtensions",
        "Antmicro.Renode.Peripherals.BasicQuadWordPeripheralExtensions",
    };

    public const string PeripheralRegisterSymbol = "Antmicro.Renode.Core.Structure.Registers.PeripheralRegister";
    public const string PeripheralRegisterGenericExtensionsLocations = "Antmicro.Renode.Core.Structure.Registers.PeripheralRegisterExtensions";

    // Maps register width, based on which Defines it uses (from which location)
    public static readonly Dictionary<string, int> SymbolExtensionsLocationsToWidthMap = new()
    {
        { RegisterDefinitionExtensionsLocations[0], 8 },
        { RegisterDefinitionExtensionsLocations[1], 16 },
        { RegisterDefinitionExtensionsLocations[2], 32 },
        { RegisterDefinitionExtensionsLocations[3], 64 },
    };

    public static readonly Dictionary<string, int> RegisterDefinitionLocationsToWidthMap = new()
    {
        { "Antmicro.Renode.Core.Structure.Registers.ByteRegister", 8 },
        { "Antmicro.Renode.Core.Structure.Registers.WordRegister", 16 },
        { "Antmicro.Renode.Core.Structure.Registers.DoubleWordRegister", 32 },
        { "Antmicro.Renode.Core.Structure.Registers.QuadWordRegister", 64 },
    };

    public static string[] RegisterTypes => RegisterDefinitionLocationsToWidthMap.Keys.ToArray();

    public const string RegisterFieldTypeReferenceName = "Antmicro.Renode.Core.Structure.Registers.IRegisterField";
    public const string MachineTypeReferenceName = "Antmicro.Renode.Core.Machine";
    public const string GPIOInterfaceReferenceName = "Antmicro.Renode.Core.IGPIO";
    public const string IrqProviderAttributeReferenceName = "Antmicro.Renode.Utilities.IrqProviderAttribute";
    public const string InterruptManagerTypeReferenceName = "Antmicro.Renode.Utilities.InterruptManager";
    public const string NumberedGPIOInterfaceReferenceName = "Antmicro.Renode.Core.INumberedGPIOOutput";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
}