//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using CommandLine;
using Microsoft.CodeAnalysis;

namespace ModelsAnalyzer.Options;
public class Options
{
#nullable disable

    [Option('p', "project", Required = true, HelpText = "Set path to Project.", SetName = "project")]
    public string Project { get; private set; }

    [Option('s', "solution", Required = true, HelpText = "Set path to Solution.", SetName = "solution")]
    public string Solution { get; private set; }

    [Option("analyzers-lib", HelpText = "Set path to analyzers dll.")]
    public IEnumerable<string> Analyzers { get; private set; }

    ///------------

    [Option('d', "debugger-wait", Default = false, HelpText = "Wait for debugger to connect.")]
    public bool WaitForDebugger { get; private set; }

    [Option("compilation-diagnostics", Default = false, HelpText = "Print solution/project compilation diagnostics above \"hidden\" level. Can be extremely long. This has nothing to do with the analyzer diagnostics.")]
    public bool PrintCompilationDiagnostics { get; private set; }

    [Option('l', "logLevel", Default = "Debug", HelpText = "Set the log level.")]
    public string LogLevel { get; private set; }

    [Option("severity", Default = DiagnosticSeverity.Hidden, HelpText = "Set the analyzer global severity level to filter output.")]
    public DiagnosticSeverity Severity { get; private set; }

    [Option('a', "analyzers", HelpText = "Run only these analyzers.")]
    public IEnumerable<string> AnalyzerWhitelist { get; private set; }

    [Option('f', "files", HelpText = "Analyze only these files.")]
    public IEnumerable<string> FilesWhitelist { get; private set; }

    [Option("no-collapse", Default = false, HelpText = "Don't collapse report of traversing projects and files, that were not analyzed.")]
    public bool NoCollapseEmpty { get; private set; }

    [Option('o', "output", Default = "", HelpText = "Output directory for analysis results.")]
    public string OutputDirectory { get; private set; }

    [Option("diagnostic-output", Default = "", HelpText = "Output directory for diagnostic rules, instead of STDOUT.")]
    public string DiagnosticOutputDirectory { get; private set; }

    [Option("flat-output", Default = false, HelpText = "Don't put output files into subfolders named after analyzed peripherals. This will break ModelsCompare, so don't use if you want to parse output with ModelsCompare.")]
    public bool FlatOutput { get; private set; }

    [Option("no-indent-json", Default = false, HelpText = "Don't indent json with whitespaces, to save space.")]
    public bool NoIndentJson { get; private set; }

    [Option("show-summary", Default = false, HelpText = "Aggregate and show summary of individual analyzer statuses and documents. It can be resource intensive.")]
    public bool ShowAnalyzersIndividualSummaries { get; private set; }

    // CurrentOptions will be filled in Main, or the program will be closed, so disabling nullability checks
    public static Options CurrentOptions { get; private set; }

#nullable enable

    public static bool ParseArguments(string[] args)
    {
        var result = Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options => Options.CurrentOptions = options);

        if(result is NotParsed<Options> np)
        {
            foreach(var error in np.Errors)
            {
                if(error is BadFormatConversionError err)
                {
                    if(err.NameInfo.NameText == "severity")
                    {
                        Console.WriteLine("Available values for severity are: {0}",
                            String.Join(", ", ((DiagnosticSeverity[])Enum.GetValues(typeof(DiagnosticSeverity))).Select(e => e.ToString()))
                        );
                    }
                }
            }
            return false;
        }

        return true;
    }

}
