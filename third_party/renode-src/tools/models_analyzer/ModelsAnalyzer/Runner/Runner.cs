//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CSharp.RuntimeBinder;
using ModelsAnalyzer;
using ModelsAnalyzer.Options;
using Newtonsoft.Json;
using NLog;
using static ModelsAnalyzer.RegisterFieldAnalysisHelpers;

namespace Runner;

internal class AnalysisRunner
{
    public AnalysisRunner(Options optionsForAnalyzer)
    {
        analysisContextOptions = optionsForAnalyzer;

        outputDataSerializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        outputDataSerializer.Converters.Add(new RoslynLocalizableStringConverter());

        outputDataSerializer.Formatting = analysisContextOptions.NoIndentJson ? Formatting.None : Formatting.Indented;
    }

    private readonly JsonSerializer outputDataSerializer = new();

    private static void AnalyzerExceptionHandler(Exception exception, DiagnosticAnalyzer analyzer, Diagnostic diagnostic)
    {
        Logger.Error("Analyzer {0} failed with unhandled exception! {1}", analyzer.ToString(), exception.ToString());
        if(analyzer is IAnalyzerWithStatus ans)
        {
            ans.AnalyzerStatus.Status = AnalyzerStatusKind.Fatal;
        }
    }

    private readonly List<AnalyzerStatusAggregator> analyzerStatusAggregators = new();

    private void SetupAnalyzersStatusAggregators(IEnumerable<DiagnosticAnalyzer> analyzerObjs)
    {
        if(!analysisContextOptions.ShowAnalyzersIndividualSummaries)
        {
            return;
        }
        var filteredAnalyzers = analyzerObjs.OfType<IAnalyzerWithStatus>();

        foreach(var analyzer in filteredAnalyzers)
        {
            Logger.Debug("Setup status aggregator for {analyzer}", analyzer.ToString());
            analyzerStatusAggregators.Add(new AnalyzerStatusAggregator(analyzer));
        }
    }

    private void AggregateAnalyzersStatus(Document document)
    {
        if(!analysisContextOptions.ShowAnalyzersIndividualSummaries)
        {
            return;
        }
        foreach(var agg in analyzerStatusAggregators)
        {
            agg.AddResult(document);
        }
    }

    private void PrintIndividualSummaries()
    {
        if(!analysisContextOptions.ShowAnalyzersIndividualSummaries)
        {
            return;
        }
        foreach(var agg in analyzerStatusAggregators)
        {
            Console.WriteLine("----");
            Console.WriteLine("Analyzer: {0}", agg.GetName());
            foreach(var kind in Helpers.EnumToEnumerable<AnalyzerStatusKind>())
            {
                Console.WriteLine(
                    "[{1}/{2}] ({3}%) status {0}",
                    kind.ToString(),
                    agg.StatusCounts[kind],
                    agg.TotalParsed,
                    agg.TotalParsed != 0 ? Math.Round((agg.StatusCounts[kind] / (1.0d * agg.TotalParsed)) * 100, 2) : 0
                );
            }
            foreach(var kind in Helpers.EnumToEnumerable<AnalyzerStatusKind>())
            {
                if(agg.StatusCounts[kind] > 0)
                {
                    Console.WriteLine("Documents for status {0}:", kind.ToString());
                    if(kind is AnalyzerStatusKind.Skip or AnalyzerStatusKind.Pass)
                    {
                        Console.WriteLine("[Collapsed]");
                    }
                    else
                    {
                        Console.WriteLine(agg.DocumentsByStatus[kind].ForEachAndJoinToString());
                    }
                }
            }
        }
    }

    private string CreateDirectoryIfNotFlat(string outputDir, string folderName)
    {
        if(!analysisContextOptions.FlatOutput)
        {
            outputDir = Path.Join(outputDir, folderName);
            if(!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
        }
        return outputDir;
    }

    // Output diagnostic rules - either to console, or Json-serialized into a file (one per peripheral)
    private void AggregateDiagnostics(Document document, IEnumerable<Diagnostic> diagnostics)
    {
        var filteredDiagnostics = diagnostics.Where(s => s.Severity >= analysisContextOptions.Severity);

        var outputDir = analysisContextOptions.DiagnosticOutputDirectory;
        // If we don't specify output directory, we will print diagnostics to console
        if(outputDir != "")
        {
            const string summaryFileName = "severity-diagnosticInfo.json";
            var outputSummaryFileName = Path.Join(outputDir, summaryFileName);

            outputDir = CreateDirectoryIfNotFlat(outputDir, document.Name);
            var fileName = document.Name + "-diagnosticInfo.json";
            // output diagnostics for higher severity levels into one separate file
            // higher than info, meaning warnings and errors
            var outputFileName = Path.Join(outputDir, fileName);

            var projectedDiagnostics = filteredDiagnostics.Select(e => new
            {
                e.Id,
                e.Descriptor,
                LocationSpan = e.Location.GetMappedLineSpan(),
                e.Severity,
                HumanMessage = e.GetMessage(),
                HumanDescription = e.ToString(),
            });
            using(StreamWriter file = File.CreateText(outputFileName))
            {
                outputDataSerializer.Serialize(file, projectedDiagnostics);
            }
            using(StreamWriter file = File.AppendText(outputSummaryFileName))
            {
                outputDataSerializer.Serialize(file, projectedDiagnostics
                    .Where(s => s.Severity > DiagnosticSeverity.Info).Select(e => e.HumanDescription)
                );
            }
        }
        else
        {
            foreach(var diag in filteredDiagnostics)
            {
                Console.WriteLine(diag);
            }
        }
    }

    // Handles additional analyzer output (analyzers that output more than just diagnostic rules - e.g. RegistersCoverageAnalyzer)
    // Currently just serializes it into Json, and dumps into separate files, one per each peripheral
    private void AggregateAdditionalAnalyzerInfo(Document document, IEnumerable<DiagnosticAnalyzer> analyzerObjs)
    {
        if(analysisContextOptions.OutputDirectory == "")
        {
            return;
        }

        var analyzersWithExtraInfo = analyzerObjs.OfType<IAnalyzerHasExtraInfo>();

        foreach(var analyzer in analyzersWithExtraInfo)
        {
            if(!analyzer.ShouldBeSerialized)
            {
                continue;
            }
            var outputDir = CreateDirectoryIfNotFlat(analysisContextOptions.OutputDirectory, document.Name);

            var fileName = document.Name + "-" + analyzer.AnalyzerSuffix + ".json";
            var outputFileName = Path.Join(outputDir, fileName);

            // fallback to generic handler
            try
            {
                dynamic anExtra = analyzer;
                using(StreamWriter file = File.CreateText(outputFileName))
                {
                    outputDataSerializer.Serialize(file, anExtra.AnalyzerExtraInfo);
                }
            }
            catch(RuntimeBinderException)
            {
                Logger.Fatal("Cannot serialize data from: {analyzerName}", analyzer.GetType().Name);
            }
        }
    }

    public async Task RunAnalysis(ImmutableArray<DiagnosticAnalyzer> analyzerObjs, AnalyzerOptions analyzerOptions, ImmutableArray<(Project, CSharpCompilation)> projectsAndCompilations)
    {
        SetupAnalyzersStatusAggregators(analyzerObjs);

        foreach(var (project, compilation) in projectsAndCompilations)
        {
            var selectedDocuments = project.Documents;
            if(analysisContextOptions.FilesWhitelist.Any())
            {
                selectedDocuments = selectedDocuments.Where(d => analysisContextOptions.FilesWhitelist.Contains(d.Name));
            }

            if(!analysisContextOptions.NoCollapseEmpty && !selectedDocuments.Any())
            {
                continue;
            }

            Console.WriteLine("\n######\nBegin analysis for project: {0}.\n", project.Name);

            foreach(var document in selectedDocuments)
            {
                var reportBuilder = new StringBuilder();
                try
                {
                    reportBuilder.AppendFormat("\n------\nBegin analysis for document: {0}.\n", document.Name);

                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if(syntaxTree is null)
                    {
                        if(!analysisContextOptions.NoCollapseEmpty)
                        {
                            reportBuilder.Clear();
                        }
                        else
                        {
                            Console.WriteLine(reportBuilder);
                        }
                        Logger.Error("Syntax tree for document {0} could not be retrieved. Does the project compile at all? Analysis aborted.", document.Name);
                        continue;
                    }
                    var semanticModel = compilation.GetSemanticModel(syntaxTree, true);

                    var peripherals = ContextualHelpers.GetAllPeripheralClasses(semanticModel);
                    if(!peripherals.Any())
                    {
                        reportBuilder.Append("No peripherals here (no class implements either directly or via inheritance, IPeripheral interface), skipping.");
                        if(!analysisContextOptions.NoCollapseEmpty)
                        {
                            reportBuilder.Clear();
                        }
                        else
                        {
                            Console.WriteLine(reportBuilder);
                        }
                        continue;
                    }
                    Console.WriteLine(reportBuilder);

                    if(peripherals.Length > 1)
                    {
                        Logger.Warn("This file contains multiple peripherals, analysis might be invalid for complex scenarios. Pay close attention if analysis displays garbage data.");
                    }

                    var compilationWithAnalyzers = compilation.WithAnalyzers(analyzerObjs,
                        new CompilationWithAnalyzersOptions(analyzerOptions, AnalyzerExceptionHandler, false, false)
                    );

                    var tokenSource = new CancellationTokenSource();

                    var semanticDiag = await compilationWithAnalyzers.GetAnalyzerSemanticDiagnosticsAsync(semanticModel, null, tokenSource.Token);
                    var syntaxDiag = await compilationWithAnalyzers.GetAnalyzerSyntaxDiagnosticsAsync(semanticModel.SyntaxTree, tokenSource.Token);

                    AggregateDiagnostics(document, semanticDiag.Concat(syntaxDiag));
                    AggregateAdditionalAnalyzerInfo(document, analyzerObjs);
                    AggregateAnalyzersStatus(document);
                }
                catch(Exception e)
                {
                    Console.WriteLine(reportBuilder);
                    Logger.Error("Analysis interrupted because of an unhandled exception within Runner: {0}", e);
                }
            }
        }

        PrintIndividualSummaries();
    }

    private readonly Options analysisContextOptions;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
}