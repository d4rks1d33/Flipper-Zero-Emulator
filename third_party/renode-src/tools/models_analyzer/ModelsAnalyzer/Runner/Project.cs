//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace ModelsAnalyzer
{
    public class SolutionLoader : AbstractProjectLoader
    {
        public async Task<Solution> OpenSolution(string where)
        {
            Solution = await this.Workspace.OpenSolutionAsync(where);
            return Solution;
        }

        public override async Task Compile()
        {
            if(Solution is null)
            {
                throw new InvalidOperationException("Load a solution before trying to compile.");
            }

            var sortedProjs = Solution.GetProjectDependencyGraph().GetTopologicallySortedProjects().Select(id => (id, Solution.GetProject(id)));

            var count = sortedProjs.Count();
            var projects = new List<ProjectLoader>(count);

            var compilationParallelTasks = new List<Task>();
            foreach(var (idx, (id, proj)) in sortedProjs.Enumerate())
            {
                if(proj is null)
                {
                    throw new InvalidOperationException($"A project with id: {id} mentioned in the dependency graph is non-existent! Something is not right with your solution.");
                }

                var projectLoaded = new ProjectLoader();
                projectLoaded.ImportProject(proj);
                projects.Add(projectLoaded);

                Logger?.Debug("[{0}/{1}] Trying to compile {2}...", idx + 1, count, proj.Name);

                compilationParallelTasks.Add(projectLoaded.Compile());
            }

            try
            {
                Logger?.Debug("Awaiting async compilation results...");
                await Task.WhenAll(compilationParallelTasks);
            }
            catch(InvalidOperationException) // this is OK - we can have a solution with dummy projects, just report it and don't save it
            {
                Logger?.Info("Skipping saving compilation results for projects {0} as they are either not compilable or not valid C# projects",
                    projects.Where(p => p.Compilation is null).Select(p => p.Project!.Name).ForEachAndJoinToString());
            }

            this.Projects = projects.Where(p => p.Compilation is not null).ToImmutableArray();
        }

        public override ImmutableArray<(Project, CSharpCompilation)> GetProjectsAndCompilations()
        {
            return Projects.Where(p => p.Project is not null && p.Compilation is not null)
                .Select(p => (p.Project!, p.Compilation!)).ToImmutableArray();
        }

        public override ImmutableArray<Diagnostic> GetAllDiagnostics()
        {
            return GetProjectsAndCompilations().SelectMany(pc => pc.Item2.GetDiagnostics()).ToImmutableArray();
        }

        public Solution? Solution { get; private set; }

        public ImmutableArray<ProjectLoader> Projects { get; private set; }

        private static readonly NLog.Logger? Logger = NLog.LogManager.GetCurrentClassLogger();
    }

    public class ProjectLoader : AbstractProjectLoader
    {
        // TODO - warn when opening a project that doesn't use Net Framework instead of .NET
        public async Task<Project> OpenProject(string where)
        {
            Project = await this.Workspace.OpenProjectAsync(where);
            return Project;
        }

        public void ImportProject(Project p)
        {
            this.Project = p;
        }

        public override async Task Compile()
        {
            if(Project is null)
            {
                throw new InvalidOperationException("Load a project before trying to compile.");
            }

            Compilation = await Project.GetCompilationAsync() as CSharpCompilation;
            if(Compilation is null)
            {
                throw new InvalidOperationException("Compilation not supported.");
            }
        }

        public override ImmutableArray<Diagnostic> GetAllDiagnostics()
        {
            return Compilation?.GetDiagnostics() ?? Enumerable.Empty<Diagnostic>().ToImmutableArray();
        }

        public Document FindDocumentByName(string name, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            if(Project is null)
            {
                throw new InvalidOperationException("Project is invalid or has not been initialized.");
            }

            try
            {
                return Project.Documents.Where(d => d.Name.Contains(name, stringComparison)).Single();
            }
            catch(InvalidOperationException)
            {
                throw new FileNotFoundException($"No single document named {name} in the open project.");
            }
        }

        public override ImmutableArray<(Project, CSharpCompilation)> GetProjectsAndCompilations()
        {
            if(Project is null || Compilation is null)
            {
                return ImmutableArray.Create<(Project, CSharpCompilation)>();
            }
            return ImmutableArray.Create(new (Project, CSharpCompilation)[] { (Project, Compilation) });
        }

        public Project? Project { get; private set; }

        public CSharpCompilation? Compilation { get; private set; }
    }

    public abstract class AbstractProjectLoader : IDisposable
    {
        static AbstractProjectLoader()
        {
            MSBuildLocator.RegisterDefaults();
        }

        public AbstractProjectLoader()
        {
            Workspace = MSBuildWorkspace.Create();
        }

        public void Dispose()
        {
            Workspace.Dispose();
        }

        public abstract Task Compile();

        //
        // Summary:
        //    This should return tuples only of projects that have a compilation object that is not null.
        //    Usually this means that the compilation was successful.
        //    You have to compile the project/solution first to get any results.
        public abstract ImmutableArray<(Project, CSharpCompilation)> GetProjectsAndCompilations();

        public abstract ImmutableArray<Diagnostic> GetAllDiagnostics();

        public MSBuildWorkspace Workspace { get; private set; }

    }
}