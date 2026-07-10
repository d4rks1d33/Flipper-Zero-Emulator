//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Microsoft.CodeAnalysis;
using ModelsAnalyzer;

namespace Runner;

class AnalyzerStatusAggregator
{
    public AnalyzerStatusAggregator(IAnalyzerWithStatus analyzer)
    {
        var enumValues = Helpers.EnumToEnumerable<AnalyzerStatusKind>();

        StatusCounts = new(enumValues.Count());
        DocumentsByStatus = new(enumValues.Count());

        foreach(var i in enumValues)
        {
            StatusCounts.Add(i, 0);
            DocumentsByStatus.Add(i, new List<string>());
        }

        boundAnalyzer = analyzer;
    }

    private readonly IAnalyzerWithStatus boundAnalyzer;

    public Dictionary<AnalyzerStatusKind, int> StatusCounts { get; private init; }
    public Dictionary<AnalyzerStatusKind, List<string>> DocumentsByStatus { get; private init; }
    public int TotalParsed { get; private set; }

    public void AddResult(Document document, bool resetStatus = true)
    {
        var status = boundAnalyzer.AnalyzerStatus.Status;

        StatusCounts[status] += 1;
        DocumentsByStatus[status].Add(document.Name);

        ++TotalParsed;

        if(resetStatus)
        {
            boundAnalyzer.AnalyzerStatus.Reset();
        }
    }

    public string GetName()
    {
        return boundAnalyzer.GetType().ToString();
    }
}