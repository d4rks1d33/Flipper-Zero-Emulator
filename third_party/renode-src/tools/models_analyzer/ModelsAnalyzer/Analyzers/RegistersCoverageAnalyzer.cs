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
using static ModelsAnalyzer.RegisterFieldAnalysisHelpers;

namespace ModelsAnalyzer;

using RegisterInfoGroup = List<RegisterGroup>;
using RegisterInfoGroupInternal = Dictionary<string, List<RegisterInfo>>;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RegistersCoverageAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus, IAnalyzerWithExtraInfo<RegisterInfoGroup>
{
    public RegistersCoverageAnalyzer()
    {
        setStatusError = () => { AnalyzerStatus.Error(); };
        setStatusIncomplete = () => { AnalyzerStatus.Incomplete(); };
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleRegisterFieldsOverlapping, RuleRegisterGapsInFieldCoverage);

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    public string AnalyzerSuffix { get; } = "registersInfo";
    public bool ShouldBeSerialized { get; private set; } = false;

    private readonly RegisterInfoGroupInternal analyzerRegisterGroups = new();
    public RegisterInfoGroup AnalyzerExtraInfo { get; private set; } = new();
    private readonly object @lock = new();

    private readonly Action setStatusError;
    private readonly Action setStatusIncomplete;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        analyzerRegisterGroups.Clear();
        AnalyzerExtraInfo.Clear();
        ShouldBeSerialized = false;
        AnalyzerStatus.Reset();

        context.RegisterSemanticModelAction(ctx =>
        {
            var registerAnalysis = new RegisterAnalysis(ctx.SemanticModel, Logger, setStatusIncomplete, setStatusError);
            var enumAnalysis = new RegisterEnumAnalysis(ctx.SemanticModel);
            var registerGroups = enumAnalysis.FindRegistersSymbols();

            if(!registerGroups.Any())
            {
                Logger.Debug("No Registers enum, this analysis won't run.");
                AnalyzerStatus.Skip();
                return;
            }

            foreach(var group in registerGroups)
            {
                foreach(var register in group.Registers)
                {
                    var parsedRegister = DoAnalysis(ctx, registerAnalysis, group.Name, register);
                    ReportGapsInCoverage(ctx, parsedRegister, register);
                    ReportOverlappingRanges(ctx, parsedRegister);
                }
            }

            lock(@lock)
            {
                foreach(var kval in analyzerRegisterGroups)
                {
                    AnalyzerExtraInfo.Add(new RegisterGroup(kval.Key, kval.Value));
                }
            }

            AnalyzerStatus.Pass();
        });
    }

    private RegisterInfo DoAnalysis(SemanticModelAnalysisContext context, RegisterAnalysis registerAnalysis, string GroupName, RegisterEnumField register)
    {
        var currentRegister = registerAnalysis.GetRegisterInfo(register);

        Logger.Trace("Got register: {register}", currentRegister);

        lock(@lock)
        {
            if(!analyzerRegisterGroups.TryAdd(GroupName, new List<RegisterInfo> { currentRegister }))
            {
                analyzerRegisterGroups[GroupName].Add(currentRegister);
            }
        }

        ShouldBeSerialized = true;

        return currentRegister;
    }

    private static void ReportGapsInCoverage(SemanticModelAnalysisContext context, RegisterInfo registerInfo, RegisterEnumField register)
    {
        if(registerInfo.Width is null)
        {
            Logger.Trace("Register {register} has unknown width, we can't search for gaps.", registerInfo.Name);
            return;
        }
        if(registerInfo.Fields.Any(e =>
            e.SpecialKind.HasFlag(RegisterFieldInfoSpecialKind.VariableLength) ||
            e.SpecialKind.HasFlag(RegisterFieldInfoSpecialKind.VariablePosition)
        ))
        {
            Logger.Debug("Register {register} has some fields with variable width, we won't search for gaps.");
            return;
        }
        var gaps = ReturnGapsInCoverage(registerInfo, registerInfo.Width.Value);
        foreach(var gap in gaps)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                // TODO: adjust location
                RuleRegisterGapsInFieldCoverage, register.RegisterSymbol.Locations.First(), gap.Item1, gap.Item2, registerInfo.Name, gap.Item3
            ));
        }
    }

    private static IReadOnlyCollection<(int, int, uint)> ReturnGapsInCoverage(RegisterInfo registerInfo, int width)
    {
        if(!registerInfo.Fields.Any())
        {
            return new[] { (0, registerInfo.Width!.Value, 0U) };
        }

        var gaps = new Stack<(int, int, uint)>();
        var groups = registerInfo.Fields.OrderBy((RegisterFieldInfo e) => e.Range.Start).GroupBy(e => e.BlockId);

        foreach(IEnumerable<RegisterFieldInfo> sortedFields in groups)
        {
            // start and end are inclusive
            var blockId = sortedFields.First().BlockId;

            if(sortedFields.First().Range.Start > 0)
            {
                gaps.Push((0, sortedFields.First().Range.Start, blockId));
            }

            var end = sortedFields.First().Range.End;
            foreach(var field in sortedFields.Skip(1))
            {
                if(end + 1 < field.Range.Start) // There is a gap (+1 because discrete variables)
                {
                    gaps.Push((end, field.Range.Start, blockId));

                    end = field.Range.End;
                }
                else // No gap
                {
                    end = field.Range.End;
                }
            }

            if(end < width - 1)
            {
                gaps.Push((end, width, blockId));
            }
        }

        return gaps;
    }

    private static void ReportOverlappingRanges(SemanticModelAnalysisContext context, RegisterInfo registerInfo)
    {
        var combinations = registerInfo.Fields
            .Where(f => !(f.SpecialKind.HasFlag(RegisterFieldInfoSpecialKind.VariableLength) || f.SpecialKind.HasFlag(RegisterFieldInfoSpecialKind.VariablePosition)))
            .PairCombinations();

        foreach(var (a, b) in combinations)
        {
            var doOverlap = AreRegisterFieldsOverlapping(a, b);
            if(doOverlap && a.BlockId == b.BlockId)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    // TODO: adjust location
                    RuleRegisterFieldsOverlapping, a.Location, a.Name, b.Name, registerInfo.Name
                ));

                Logger.Debug("Register's {register} fields are overlapping: {fieldA} {fieldB}.", registerInfo.Name, a.Name, b.Name);
            }
            else if(doOverlap && a.BlockId != b.BlockId)
            {
                Logger.Debug("Register's {register} fields: {fieldA} {fieldB} are NOT overlapping because they don't share the same code block.", registerInfo.Name, a.Name, b.Name);
            }
        }
    }

    private static bool AreRegisterFieldsOverlapping(RegisterFieldInfo a, RegisterFieldInfo b)
    {
        var minEnd = Math.Min(a.Range.End, b.Range.End);
        var maxStart = Math.Max(a.Range.Start, b.Range.Start);
        return minEnd >= maxStart;
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
}