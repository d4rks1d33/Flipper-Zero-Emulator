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

namespace ModelsAnalyzer;

using ExtraInfoType = Dictionary<string, HashSet<string>>;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PeripheralClassesAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus, IAnalyzerWithExtraInfo<ExtraInfoType>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NoDiagnosticsAvailable);

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    public bool ShouldBeSerialized { get; private set; } = false;
    public string AnalyzerSuffix { get; } = "classesInfo";
    public ExtraInfoType AnalyzerExtraInfo { get; } = new();

    private readonly object dictionaryLock = new();

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        AnalyzerExtraInfo.Clear();
        ShouldBeSerialized = false;
        AnalyzerStatus.Reset();

        context.RegisterSymbolAction(ctx =>
        {
            if(ctx.Symbol is not INamedTypeSymbol clsSymbol)
            {
                return;
            }

            if(ContextualHelpers.IsClassAPeripheral(clsSymbol))
            {
                var inherited = clsSymbol.GetInheritanceChain().Select(e => e.ToDisplayString()).Where(e => e != "object").ToHashSet();
                inherited.UnionWith(clsSymbol.AllInterfaces.Select(e => e.ToDisplayString()));

                lock(dictionaryLock)
                {
                    if(!AnalyzerExtraInfo.TryAdd(clsSymbol.Name, inherited))
                    {
                        Logger.Error("Possible duplicate symbol.");
                        AnalyzerStatus.Error();

                        var internalSet = AnalyzerExtraInfo[clsSymbol.Name];
                        internalSet.UnionWith(inherited);
                    }
                    ShouldBeSerialized = true;
                }
            }

            AnalyzerStatus.Pass();
        }, SymbolKind.NamedType);
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
}