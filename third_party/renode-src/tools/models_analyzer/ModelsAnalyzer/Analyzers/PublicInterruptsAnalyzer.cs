//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static ModelsAnalyzer.Rules;
using static ModelsAnalyzer.ContextualHelpers;
using static ModelsAnalyzer.TypeHelpers;

namespace ModelsAnalyzer;

using ExtraInfoType = Dictionary<string, List<string>>;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PublicInterruptsAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus, IAnalyzerWithExtraInfo<ExtraInfoType>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NoDiagnosticsAvailable);

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    public bool ShouldBeSerialized { get; private set; } = false;
    public string AnalyzerSuffix { get; } = "interruptInfo";
    public ExtraInfoType AnalyzerExtraInfo { get; } = new();

    private readonly object @lock = new();

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        AnalyzerExtraInfo.Clear();
        ShouldBeSerialized = false;
        AnalyzerStatus.Reset();

        // Start as "passing" - there might be peripherals without field or property symbols (e.g. Silencer)
        // If we need to fail we will fail in the future
        AnalyzerStatus.Pass();

        context.RegisterSymbolAction(ctx =>
        {
            if(!(ctx.Symbol is INamedTypeSymbol classSymbol && IsClassAPeripheral(classSymbol)))
            {
                Logger.ConditionalTrace("Ignoring {name}, because it is not a peripheral class.", ctx.Symbol.Name);
                return;
            }

            foreach(var member in classSymbol.GetMembers().OfType<IPropertySymbol>().Where(e => e.DeclaredAccessibility == Accessibility.Public))
            {
                DoAnalysis(classSymbol, member);
            }

            if(DoesImplementInterface(classSymbol, NumberedGPIOInterfaceReferenceName))
            {
                Logger.Debug("Class {name} has numbered GPIO.", classSymbol.Name);

                lock(@lock)
                {
                    ShouldBeSerialized = true;
                    if(!AnalyzerExtraInfo.TryAdd(classSymbol.Name, new List<string> { MessageHasINumberedGPIO }))
                    {
                        AnalyzerExtraInfo[classSymbol.Name].Add(MessageHasINumberedGPIO);
                    }
                }
            }

        }, new[] { SymbolKind.NamedType });
    }

    private void DoAnalysis(INamedTypeSymbol classSymbol, ISymbol memberSymbol)
    {
        bool decayed = false;
        var type = memberSymbol.GetTypeSymbolIfPresent();

        // If one or more dimensional array, decay to element type
        while((type as IArrayTypeSymbol)?.DecayArrayTypeToElementType() is ITypeSymbol decayedSymbol)
        {
            decayed = true;
            type = decayedSymbol;
        }

        if(!DoesImplementInterface(type as INamedTypeSymbol, GPIOInterfaceReferenceName))
        {
            return;
        }

        if(decayed)
        {
            // Maybe we should output diagnostics here? Should users create public arrays of GPIOs?
            Logger.Warn("Arrays of GPIOs are unsupported.");
            AnalyzerStatus.Incomplete();
        }

        Logger.Debug("Symbol {name} is a GPIO.", memberSymbol.Name);

        lock(@lock)
        {
            ShouldBeSerialized = true;

            if(!AnalyzerExtraInfo.TryAdd(classSymbol.Name, new List<string> { memberSymbol.Name }))
            {
                AnalyzerExtraInfo[classSymbol.Name].Add(memberSymbol.Name);
            }
        }
    }

    private const string MessageHasINumberedGPIO = "[Numbered Interrupts]";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
}