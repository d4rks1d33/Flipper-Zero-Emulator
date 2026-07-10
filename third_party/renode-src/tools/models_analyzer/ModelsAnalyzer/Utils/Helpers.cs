//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace ModelsAnalyzer;

public static partial class Helpers
{
    public static SyntaxNode GetSyntaxNodeFromLocation(this SyntaxTree st, Location l)
    {
        return st.GetRoot().FindNode(l.SourceSpan);
    }

    /// <summary> Get syntax token that represents class/enum/... declaration name </summary>
    /// <remarks> Useful when reporting diagnostics </remarks>
    public static SyntaxToken GetNameToken(this BaseTypeDeclarationSyntax cls)
    {
        return cls.ChildTokens().OfType<SyntaxToken>().Single(s => s.IsKind(SyntaxKind.IdentifierToken));
    }

    public static ImmutableArray<ClassDeclarationSyntax> GetAllClasses(this SyntaxTree st)
    {
        return st.GetRoot().DescendantNodesAndSelf().OfType<ClassDeclarationSyntax>().ToImmutableArray();
    }

    public static string GetOriginalName(this ReferencedSymbol rs)
    {
        return rs.Definition.Name;
    }

    public static ISymbol GetSymbolOrThrowException(this SymbolInfo symbolInfo)
    {
        if(symbolInfo.Symbol is null)
        {
            var candidateNames = symbolInfo.CandidateSymbols.Select(e => e.Name);
            throw new Exception(String.Format("ERROR: The symbol binding is not clearly resolved! Reason: {0}. Candidates are: {1}.",
                symbolInfo.CandidateReason.ToString(),
                !candidateNames.Any() ? "No candidates!" : String.Join(", ", candidateNames)
            ));
        }
        return symbolInfo.Symbol;
    }

    public static bool WithinSameCodeBlock(this SyntaxTree tree, Location location1, Location location2)
    {
        var node1 = tree.GetSyntaxNodeFromLocation(location1);
        var node2 = tree.GetSyntaxNodeFromLocation(location2);

        var ancestors1 = node1.AncestorsAndSelf().WithinCodeBlock().Last();
        var ancestors2 = node2.AncestorsAndSelf().WithinCodeBlock().Last();

        return ancestors1 == ancestors2;
    }

    /// <summary> Convert each element to string and join it </summary>
    public static string ForEachAndJoinToString<T>(this IEnumerable<T> enumerable, string separator = ", ")
    {
        return String.Join(separator, enumerable.Select(x => x?.ToString() ?? "[null]"));
    }

    /// <summary> Like Python Enumerate returns tuple (index, element) </summary>
    public static IEnumerable<(int, TSource)> Enumerate<TSource>(this IEnumerable<TSource> source)
    {
        return source.Select((x, idx) => (idx, x));
    }

    public static IEnumerable<TSource> TakeUntilType<TSource, TType>(this IEnumerable<TSource> source)
    {
        foreach(var element in source)
        {
            yield return element;
            if(element is TType)
            {
                yield break;
            }
        }
    }

    public static IEnumerable<SyntaxNode> WithinCodeBlock(this IEnumerable<SyntaxNode> source)
    {
        return source.TakeUntilType<SyntaxNode, BlockSyntax>();
    }

    public static IEnumerable<T> EnumToEnumerable<T>() where T : System.Enum
    {
        return Enum.GetValues(typeof(T)).Cast<T>();
    }

    public static T ThrowIfNull<T>(this T? obj, string? customMessage = null)
    {
        return obj ?? throw new NullReferenceException(customMessage ?? $"{nameof(obj)} is null.");
    }

    public static IEnumerable<(T, T)> PairCombinations<T>(this IEnumerable<T> source)
    {
        foreach(var (idx, e) in source.Enumerate())
        {
            foreach(var f in source.Skip(idx + 1))
            {
                yield return (e, f);
            }
        }
        yield break;
    }
}
