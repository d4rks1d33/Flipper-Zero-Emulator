//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ModelsAnalyzer.ContextualHelpers;

namespace ModelsAnalyzer;

using RegisterEnumGroup = RegisterGroup<RegisterEnumField>;

public record class RegisterEnumField(ISymbol RegisterSymbol, long RegisterAddress);

public record class RegisterGroup<T>(string Name, IList<T> Registers);

public class RegisterEnumAnalysis
{
    public RegisterEnumAnalysis(SemanticModel model)
    {
        this.SemanticModel = model;
    }

    public ImmutableArray<RegisterEnumGroup> FindRegistersSymbols()
    {
        List<RegisterEnumGroup> registerGroups = new();

        var cls = GetAllPeripheralClasses(SemanticModel);
        foreach(var cl in cls)
        {
            var enums = cl.Item1.DescendantNodes().OfType<EnumDeclarationSyntax>();
            foreach(var enumDecl in enums)
            {
                if(!CanBeRegistersEnum(enumDecl))
                {
                    continue;
                }

                var list = GetRegistersAndValues(enumDecl);
                registerGroups.Add(new RegisterEnumGroup(enumDecl.Identifier.Text, list));
            }
        }

        return registerGroups.ToImmutableArray();
    }

    public bool HasCorrectUnderlyingType(EnumDeclarationSyntax registers)
    {
        var enumSymbol = ThrowIfSymbolIsIncorrect(registers);
        var longTypeBuiltin = SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int64);

        // this is guaranteed to be an enum at this point
        if(enumSymbol.EnumUnderlyingType!.SpecialType != longTypeBuiltin.SpecialType)
        {
            return false;
        }
        return true;
    }

    public IList<RegisterEnumField> GetRegistersAndValues(EnumDeclarationSyntax registers, Action<EnumMemberDeclarationSyntax, ISymbol, long>? onMemberSymbolParsed = null)
    {
        var enumSymbol = ThrowIfSymbolIsIncorrect(registers);

        var rets = new List<RegisterEnumField>();
        long prev = -1;
        foreach(var e in registers.Members)
        {
            var symbol = SemanticModel.GetDeclaredSymbol(e);
            if(symbol is null)
            {
                throw new InvalidDataException("Enum member cannot be resolved to symbol. This can signify a problem with project's compilation - check if the project is compiled without errors.");
            }

            long constVal = ++prev;

            if(e.EqualsValue is not null)
            {
                constVal = (long)Convert.ChangeType(SemanticModel.GetConstantValue(e.EqualsValue.Value).Value!, typeof(long));
            }

            onMemberSymbolParsed?.Invoke(e, symbol, constVal);

            rets.Add(new(symbol, constVal));
            prev = constVal;
        }
        return rets;
    }

    private INamedTypeSymbol ThrowIfSymbolIsIncorrect(EnumDeclarationSyntax registers)
    {
        if(SemanticModel.GetDeclaredSymbol(registers) is not INamedTypeSymbol enumSymbol)
        {
            throw new InvalidDataException("Enum Syntax Node does not resolve to INamedTypeSymbol Symbol. This is serious and unexpected - check if the project is compiled without errors.");
        }
        return enumSymbol;
    }

    private readonly SemanticModel SemanticModel;
}
