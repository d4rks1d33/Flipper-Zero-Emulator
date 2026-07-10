//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using static ModelsAnalyzer.Rules;
using static ModelsAnalyzer.SymbolHelpers;

namespace ModelsAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RegistersDefinitionAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus
{
    public RegistersDefinitionAnalyzer()
    {
        setStatusError = () => { AnalyzerStatus.Error(); };
        setStatusIncomplete = () => { AnalyzerStatus.Incomplete(); };
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        RuleRegisterDefinitelyUnused, RuleRegisterProbablyUnused, RuleRegisterNotDefinedInDeclarativeSyntax,
        RuleRegisterDefinedInDictSyntax, RuleRegisterDefinedInDeclarativeSyntax, RuleRegisterDefinedInSwitchSyntax
    );

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    private readonly Action setStatusError;
    private readonly Action setStatusIncomplete;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        AnalyzerStatus.Reset();

        context.RegisterSemanticModelAction(ctx =>
        {
            var identifier = new RegisterFormatIdentifier(ctx.SemanticModel, Logger, setStatusError);
            var registerAnalysis = new RegisterAnalysis(ctx.SemanticModel, Logger, setStatusIncomplete, setStatusError);
            var enumAnalysis = new RegisterEnumAnalysis(ctx.SemanticModel);

            var registers = enumAnalysis.FindRegistersSymbols().SelectMany(e => e.Registers);
            if(!registers.Any())
            {
                Logger?.Debug("No Registers or empty Registers, this analysis won't run.");
                AnalyzerStatus.Skip();
                return;
            }

            var allSymbols = GetAllReferencedSymbols(ctx.SemanticModel);
            foreach(var enumMember in registers)
            {
                var referred = FilterReferencesToSymbol(enumMember.RegisterSymbol, allSymbols);

                // DefineMany fired, this register is a child register
                // no diagnostics to report, but not undefined either
                if(registerAnalysis.GetDummyReg(enumMember) is not null)
                {
                    continue;
                }

                if(referred is null)
                {
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(RuleRegisterDefinitelyUnused, enumMember.RegisterSymbol.Locations.First(), enumMember.RegisterSymbol.ToDisplayString())
                    );
                    continue;
                }

                if(!referred.LocationsOfReferences.Any())
                {
                    Logger?.Warn("The register symbol {0} has no locations in code. Cannot determine how it is used.", referred.SymbolInfo.Symbol!.Name);
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(RuleRegisterProbablyUnused, enumMember.RegisterSymbol.Locations.First(), enumMember.RegisterSymbol.ToDisplayString())
                    );
                    continue;
                }

                DoAnalysis(ctx, referred, enumMember, identifier, registerAnalysis);
            }
            AnalyzerStatus.Pass();
        });
    }

    private static void DoAnalysis(SemanticModelAnalysisContext ctx, SymbolInfoWithLocation referred, RegisterEnumField enumMember, RegisterFormatIdentifier identifier, RegisterAnalysis registerAnalysis)
    {
        bool isDefinedDecl = false;
        bool isDefinedDict = false;
        bool isDefinedSwitch = false;
        bool isDefined = false;

        var symbol = enumMember.RegisterSymbol;
        foreach(var location in referred.LocationsOfReferences)
        {
            isDefinedDict |= identifier.IsRegisterDefinedInDictSyntax(location, symbol);
            isDefinedDecl |= identifier.IsRegisterDefinedInExtensionSyntax(location, symbol);
            // TODO it's interesting, but let's just mark it as incomplete instead of error
            // usually this is just an exotic case in codebase like LSM9DS1_Magnetic
            isDefinedSwitch |= identifier.IsRegisterUsedInSwitchStatement(location, symbol);

            if(isDefinedSwitch)
            {
                ctx.ReportDiagnostic(
                        Diagnostic.Create(RuleRegisterDefinedInSwitchSyntax, location, symbol.Name)
                    );
            }
            if(isDefinedDict)
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(RuleRegisterDefinedInDictSyntax, location, symbol.Name)
                );
            }
            if(isDefinedDecl)
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(RuleRegisterDefinedInDeclarativeSyntax, location, symbol.Name)
                );
                registerAnalysis.TrackDefineMany(enumMember, location);
            }
            isDefined |= isDefinedDecl | isDefinedDict | isDefinedSwitch;
        }

        var shortName = referred.SymbolInfo.Symbol!.Name;
        if(!isDefined)
        {
            Logger?.Warn("The register {0} is likely undefined. The analyzer could not find actions that would mean it is definitely defined.", shortName);
            ctx.ReportDiagnostic(
                Diagnostic.Create(RuleRegisterProbablyUnused, enumMember.RegisterSymbol.Locations.First(), shortName)
            );

            Logger?.Debug("## References to the register symbol, that were not classified as initialization:");
            Logger?.Debug(
                string.Join("\n",
                    referred.LocationsOfReferences.Select(l => l.GetMappedLineSpan().ToString())
                )
            );

            Logger?.Debug("## Review these locations and fix up the analyzer if they should be marked.");
        }
        else
        {
            if(!(isDefinedDecl || isDefinedDict))
            {
                Logger?.Trace("Register {name} is not defined in declarative format.", shortName);
                ctx.ReportDiagnostic(
                    Diagnostic.Create(RuleRegisterNotDefinedInDeclarativeSyntax, enumMember.RegisterSymbol.Locations.First(), shortName)
                );
            }
        }

    }

    private static readonly NLog.Logger? Logger = NLog.LogManager.GetCurrentClassLogger();
}
