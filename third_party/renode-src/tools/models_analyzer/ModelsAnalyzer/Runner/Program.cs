//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using System.Reflection;
using ModelsAnalyzer;
using ModelsAnalyzer.Options;
using CommandLine;
using NLog;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Diagnostics;

namespace Runner;

public static partial class Runner
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    static IEnumerable<Assembly> LoadAnalyzerAssemblies()
    {
        if(!Options.CurrentOptions.Analyzers.Any())
        {
            // Try loading default assembly (referenced RenodeAnalyzers.dll)
            Logger.Debug("Trying to load default analyzers library.");
            return new[] { Assembly.Load(new AssemblyName("RenodeAnalyzers")) };
        }
        else
        {
            return Options.CurrentOptions.Analyzers.Select(analyzerLib =>
                Assembly.LoadFrom(analyzerLib)
            );
        }
    }

    static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        Logger.Info("Loading analyzers...");

        var analyzerAssemblies = LoadAnalyzerAssemblies();
        var analyzerList = analyzerAssemblies.SelectMany(asm =>
                asm.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Any()
                    && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                )
        );

        if(Options.CurrentOptions.AnalyzerWhitelist.Any())
        {
            analyzerList = analyzerList.Where(a => Options.CurrentOptions.AnalyzerWhitelist.Contains(a.Name));
        }

        var analyzerObjs = analyzerList.Select(a => Activator.CreateInstance(a)).Cast<DiagnosticAnalyzer>();

        if(analyzerObjs.Any())
        {
            Logger.Info("Loaded the following analyzers: {0}",
                String.Join(", ", analyzerObjs.Select(a => a.GetType().Name)));
        }
        return analyzerObjs.ToImmutableArray();
    }

    static async Task<SolutionLoader> OpenSolutionAsync()
    {
        var pl = new SolutionLoader();
        await pl.OpenSolution(Options.CurrentOptions.Solution);
        Logger.Info("Loaded solution {solution}", Options.CurrentOptions.Solution);
        return pl;
    }

    static async Task<ProjectLoader> OpenProjectAsync()
    {
        var pl = new ProjectLoader();
        await pl.OpenProject(Options.CurrentOptions.Project);
        Logger.Info("Loaded project {project}", Options.CurrentOptions.Project);
        return pl;
    }

    static async Task<int> Main(string[] args)
    {
        if(!Options.ParseArguments(args))
        {
            Console.WriteLine("Failed to parse arguments!");
            return 1;
        }

        if(Options.CurrentOptions.WaitForDebugger)
        {
            Console.WriteLine("Waiting for debugger...");
            while(!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
        }

        NLog.LogManager.Setup().LoadConfiguration(builder =>
        {
            builder.ForLogger().FilterMinLevel(LogLevel.FromString(Options.CurrentOptions.LogLevel))
                .WriteToConsole("[${level:uppercase=true}] {${logger}} ${message}");
            builder.ForLogger().FilterMinLevel(LogLevel.Trace).WriteToFile(fileName: "logfile.txt");
        });

        static void spawnDirectoryIfNotEmpty(string directory)
        {
            if(directory != "")
            {
                if(!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                Logger.Debug("Extra data output directory at: {directory}", directory);
            }
        }

        spawnDirectoryIfNotEmpty(Options.CurrentOptions.OutputDirectory);
        spawnDirectoryIfNotEmpty(Options.CurrentOptions.DiagnosticOutputDirectory);

        var analyzerObjs = LoadAnalyzers();
        if(!analyzerObjs.Any())
        {
            Logger.Fatal("No analyzers loaded. Check if the provided assemblies contain analyzers derived from DiagnosticAnalyzer and marked with DiagnosticAnalyzerAttribute.");
            return 1;
        }

        Logger.Info("Initializing workspace...");
        using AbstractProjectLoader projectLoader = (Options.CurrentOptions.Project is null) ? await OpenSolutionAsync() : await OpenProjectAsync();
        Logger.Info("Compiling...");
        await projectLoader.Compile();

        if(Options.CurrentOptions.PrintCompilationDiagnostics)
        {
            Console.WriteLine(projectLoader.GetAllDiagnostics().Where(t => t.Severity >= DiagnosticSeverity.Info).ForEachAndJoinToString("\n"));
        }

        var analyzerOptions = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>());

        var projectsAndCompilations = projectLoader.GetProjectsAndCompilations();

        var analysisRunner = new AnalysisRunner(Options.CurrentOptions);
        await analysisRunner.RunAnalysis(analyzerObjs, analyzerOptions, projectsAndCompilations);

        Console.WriteLine("\n-----\nAll done");
        return 0;
    }
}
