//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
namespace ModelsAnalyzer;

public enum AnalyzerStatusKind
{
    // Analyzer should not return this status
    Undefined = -1,
    // Analysis of a document was skipped
    // e.g. because of a missing precondition
    Skip = 0,
    // Analysis passed without major problems, yielding some information
    Pass,
    // Analysis yielded some information but some is missing because of unimplemented functionality within analyzer
    // e.g. handling some exotic cases
    Incomplete,
    // Analysis yielded some information but finished with errors, indicating that fixes to the analyzer are needed in the future
    Error,
    // Analyzer crashed during analysis, possibly without completing it, immediate fixup to the analyzer is needed
    // This will be set by failure handler, shouldn't be set manually
    Fatal,
}

public class ProtectedAnalyzerStatus
{
    private AnalyzerStatusKind _analyzerStatus = AnalyzerStatusKind.Undefined;
    private readonly object analyzerLock = new();

    public AnalyzerStatusKind Status
    {
        get
        {
            lock(analyzerLock)
            {
                return _analyzerStatus;
            }
        }
        set
        {
            lock(analyzerLock)
            {
                // bigger means more critical condition
                if(value > _analyzerStatus)
                {
                    _analyzerStatus = value;
                }
            }
        }
    }

    public void Reset()
    {
        lock(analyzerLock)
        {
            _analyzerStatus = AnalyzerStatusKind.Undefined;
        }
    }

    public void Skip() => Status = AnalyzerStatusKind.Skip;
    public void Pass() => Status = AnalyzerStatusKind.Pass;
    public void Incomplete() => Status = AnalyzerStatusKind.Incomplete;
    public void Error() => Status = AnalyzerStatusKind.Error;

    public static implicit operator AnalyzerStatusKind(ProtectedAnalyzerStatus g) => g.Status;
}

public interface IAnalyzerWithStatus
{
    ProtectedAnalyzerStatus AnalyzerStatus { get; }
}