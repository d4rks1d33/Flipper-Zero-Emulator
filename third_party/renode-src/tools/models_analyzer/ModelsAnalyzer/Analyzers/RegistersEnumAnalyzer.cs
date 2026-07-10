//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using static ModelsAnalyzer.Rules;
using static ModelsAnalyzer.ContextualHelpers;

namespace ModelsAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RegistersEnumAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(RuleNoExplicitMemberValue, RuleNotLongUnderlyingType, RuleNoRegistersEnum);

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    private class RegistersEnumAnalyzerInternalStateful
    {
        private volatile int registersCtr = 0;

        public void GetRegistersAndValues(SyntaxNodeAnalysisContext context)
        {
            var registers = (EnumDeclarationSyntax)context.Node;
            if(!CanBeRegistersEnum(registers))
            {
                return;
            }

            var enumAnalyzer = new RegisterEnumAnalysis(context.SemanticModel);

            if(!enumAnalyzer.HasCorrectUnderlyingType(registers))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(RuleNotLongUnderlyingType, registers.GetNameToken().GetLocation(), registers.Identifier.Text)
                );
            }

            enumAnalyzer.GetRegistersAndValues(registers, (EnumMemberDeclarationSyntax e, ISymbol symbol, long val) =>
            {
                if(e.EqualsValue is null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(RuleNoExplicitMemberValue, e.GetLocation(), symbol.Name, val)
                    );
                }
            });
            Interlocked.Increment(ref registersCtr);
        }

        public void EndAnalysis(SymbolAnalysisContext context)
        {
            if(registersCtr > 0)
            {
                return;
            }

            // highlight class name
            foreach(var location in context.Symbol.Locations)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(RuleNoRegistersEnum, location)
                );
            }
        }
    }

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        var analyzerInternal = new RegistersEnumAnalyzerInternalStateful();

        context.RegisterSymbolStartAction(ctx =>
        {
            ctx.RegisterSyntaxNodeAction(analyzerInternal.GetRegistersAndValues, SyntaxKind.EnumDeclaration);

            if(ctx.Symbol is INamedTypeSymbol ns && IsClassAPeripheral(ns))
            {
                ctx.RegisterSymbolEndAction(analyzerInternal.EndAnalysis);
            }

            AnalyzerStatus.Pass();
        }, SymbolKind.NamedType);
    }
}
