//
// Copyright (c) 2022-2024 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using NLog;
using static ModelsAnalyzer.Rules;

namespace ModelsAnalyzer;

using ExtraInfoType = Dictionary<string, Dictionary<string, object>>;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PeripheralAttributeAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus, IAnalyzerWithExtraInfo<ExtraInfoType>
{
    public PeripheralAttributeAnalyzer() { }

    public PeripheralAttributeAnalyzer(IEnumerable<string> interestingAttributes, bool analyzeChain)
    {
        this.interestingAttributes = interestingAttributes;
        this.analyzeChain = analyzeChain;
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NoDiagnosticsAvailable);

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    public string AnalyzerSuffix => "attributes";
    public bool ShouldBeSerialized { get; private set; }
    public ExtraInfoType AnalyzerExtraInfo { get; } = new();

#pragma warning disable RS1026  // "Enable concurrent execution"
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        AnalyzerExtraInfo.Clear();
        ShouldBeSerialized = false;
        AnalyzerStatus.Reset();

        context.RegisterSymbolAction(ctx =>
        {
            if(ctx.Symbol is not INamedTypeSymbol clsSymbol)
            {
                AnalyzerStatus.Skip();
                return;
            }
            if(!ContextualHelpers.IsClassAPeripheral(clsSymbol))
            {
                AnalyzerStatus.Skip();
                return;
            }

            var attributes = clsSymbol.GetAttributes();
            if(attributes.IsEmpty)
            {
                AnalyzerStatus.Skip();
                return;
            }

            foreach(var attr in attributes)
            {
                if(attr?.AttributeClass is null)
                {
                    Logger.Log(LogLevel.Error, "Can't obtain attribute {0}", attr?.ToString());
                    AnalyzerStatus.Error();
                    return;
                }

                var name = attr.AttributeClass.ToDisplayString();

                if(interestingAttributes != null && !interestingAttributes.Contains(name))
                {
                    // One last chance - if any classes in inheritance chain are matching, we still allow this to pass
                    if(!(analyzeChain
                        && attr.AttributeClass.GetInheritanceChain().Where(s => s.ToDisplayString() == name).Any()))
                    {
                        continue;
                    }
                }

                var ctorParamsNullable = attr?.AttributeConstructor?.Parameters;
                if(ctorParamsNullable is not IEnumerable<IParameterSymbol> ctorParams)
                {
                    Logger.Log(LogLevel.Error, "Can't obtain constructor for {0}", attr?.ToString());
                    AnalyzerStatus.Error();
                    return;
                }

                var processedData = new Dictionary<string, object>();
                CreateAttributeInfo(ctorParams.Select(p => p.Name), attr!.ConstructorArguments, processedData);
                CreateAttributeInfo(attr.NamedArguments.Select(s => s.Key), attr.NamedArguments.Select(s => s.Value), processedData);

                AnalyzerExtraInfo.Add(name, processedData);
            }

            ShouldBeSerialized = true;
            AnalyzerStatus.Pass();
        }, SymbolKind.NamedType);
    }

    private static object ConvertArgumentToDisplay(TypedConstant ctorArg)
    {
        if(ctorArg.Kind is TypedConstantKind.Error or TypedConstantKind.Array)
        {
            throw new InvalidOperationException($"Arg {ctorArg} is {ctorArg.Kind}!");
        }

        if(ctorArg.Kind is TypedConstantKind.Type or TypedConstantKind.Enum)
        {
            return ctorArg.ToCSharpString();
        }
        else
        {
            return ctorArg.Value ?? "[null]";
        }
    }

    private static object ProcessAttributeArgument(TypedConstant ctorArg)
    {
        if(ctorArg.Kind == TypedConstantKind.Array)
        {
            var rets = new List<object>();
            foreach(var v in ctorArg.Values)
            {
                rets.Add(ProcessAttributeArgument(v));
            }
            return rets;
        }
        else
        {
            return ConvertArgumentToDisplay(ctorArg);
        }
    }

    private static void CreateAttributeInfo(IEnumerable<(string, TypedConstant)> namesVals, Dictionary<string, object> processedData)
    {
        foreach(var (ctorArgName, ctorArg) in namesVals)
        {
            processedData.Add(ctorArgName, ProcessAttributeArgument(ctorArg));
        }
    }

    private static void CreateAttributeInfo(IEnumerable<string> names, IEnumerable<TypedConstant> vals, Dictionary<string, object> processedData)
    {
        CreateAttributeInfo(names.Zip(vals), processedData);
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly IEnumerable<string>? interestingAttributes;
    private readonly bool analyzeChain;
}
