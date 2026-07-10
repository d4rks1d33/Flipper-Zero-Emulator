//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModelsAnalyzer;

public static class SymbolHelpers
{
    public record class SymbolInfoWithLocation
    (
        SymbolInfo SymbolInfo,
        List<Location> LocationsOfReferences
    )
    {
        public override string ToString()
        {
            var rets = new StringBuilder();
            rets.Append(this.GetType().Name);
            rets.Append(" { ");
            rets.Append(SymbolInfo.Symbol?.Name);
            rets.AppendLine(" [ Locations: ");
            foreach(var location in LocationsOfReferences)
            {
                rets.Append('\t');
                rets.AppendLine(location.GetMappedLineSpan().ToString());
            }
            rets.AppendLine(" ] ");
            rets.AppendLine("} ");

            return rets.ToString();
        }
    }

    public static IEnumerable<INamedTypeSymbol> GetInheritanceChain(this INamedTypeSymbol cls)
    {
        for(var node = cls.BaseType; node != null; node = node.BaseType)
        {
            yield return node;
        }
    }

    public static IEnumerable<SymbolInfoWithLocation> GetAllReferencedSymbols(SemanticModel model, SyntaxNode? alternateRoot = null, bool includeDeclarations = false)
    {
        var syntaxTreeRoot = alternateRoot ?? model.SyntaxTree.GetRoot();
        IEnumerable<SyntaxNode> identifiers = syntaxTreeRoot.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
        var allSymbols = new Dictionary<SymbolInfo, SymbolInfoWithLocation>();

        foreach(var node in identifiers)
        {
            var symbolInfo = model.GetSymbolInfo(node);
            if(!allSymbols.TryAdd(symbolInfo, new(symbolInfo, new List<Location> { node.GetLocation() })))
            {
                allSymbols[symbolInfo].LocationsOfReferences.Add(node.GetLocation());
            }
            else if(includeDeclarations)
            {
                var locations = symbolInfo.Symbol?.Locations;
                if(locations is not null)
                {
                    allSymbols[symbolInfo].LocationsOfReferences.AddRange(locations);
                }
            }
        }

        return allSymbols.Select(t => t.Value);
    }

    public static SymbolInfoWithLocation? FilterReferencesToSymbol(ISymbol symbol, IEnumerable<SymbolInfoWithLocation> allSymbols)
    {
        var referencedSymbols = allSymbols.Where(s => s.SymbolInfo.Symbol is not null)
                .Where(s => SymbolEqualityComparer.Default.Equals(s.SymbolInfo.Symbol, symbol)).SingleOrDefault();

        return referencedSymbols;
    }

    public static IEnumerable<ISymbol> FilterEqualSymbols(ISymbol symbol, IEnumerable<ISymbol> allSymbols)
    {
        var symbols = allSymbols.Where(s => s is not null)
                .Where(s => SymbolEqualityComparer.Default.Equals(s, symbol));

        return symbols;
    }
}