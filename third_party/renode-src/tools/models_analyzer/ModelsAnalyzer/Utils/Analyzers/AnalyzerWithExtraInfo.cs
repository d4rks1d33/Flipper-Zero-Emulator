//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
namespace ModelsAnalyzer;

public interface IAnalyzerHasExtraInfo
{
    string AnalyzerSuffix { get; }
    bool ShouldBeSerialized { get; }
}

public interface IAnalyzerWithExtraInfo<T> : IAnalyzerHasExtraInfo
{
    T AnalyzerExtraInfo { get; }
}