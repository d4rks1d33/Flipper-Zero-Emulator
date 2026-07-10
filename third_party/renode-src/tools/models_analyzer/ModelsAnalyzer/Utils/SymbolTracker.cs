//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ModelsAnalyzer.SymbolHelpers;

namespace ModelsAnalyzer;

public class SymbolTracker
{
    public SymbolTracker(NLog.Logger? logger = null)
    {
        Logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
    }

    /// <summary> This function returns symbols that might point to the same object </summary>
    /// <remarks>
    /// e.g. symbolX = symbolY, both symbols will be returned by function
    /// but also symbolX = symbolY.Function()
    /// </remarks>
    public ImmutableArray<ISymbol> FindSymbolsRelatedByAssignment(SemanticModel model, ISymbol? symbolToSearch, SyntaxNode? alternateRoot = null)
    {
        if(symbolToSearch is null)
        {
            return ImmutableArray.Create<ISymbol>();
        }
        var allSymbols = GetAllReferencedSymbols(model, alternateRoot, includeDeclarations: true);

        HashSet<ISymbol> rets = new(SymbolEqualityComparer.Default);

        HashSet<ISymbol> parsedSymbols = new(SymbolEqualityComparer.Default);
        Stack<ISymbol> queuedSymbols = new();
        queuedSymbols.Push(symbolToSearch);

        while(queuedSymbols.TryPop(out var currentSymbol))
        {
            parsedSymbols.Add(currentSymbol);

            if(!(currentSymbol is ILocalSymbol or IFieldSymbol or IParameterSymbol))
            {
                Logger.ConditionalTrace("Symbol kind: {kind} will be ignored in finding related.", currentSymbol.Kind.ToString());
                continue;
            }

            rets.Add(currentSymbol);

            // walk through all symbol references (including its declaration) to see if it was assigned somewhere
            var symbolRef = FilterReferencesToSymbol(currentSymbol, allSymbols);
            if(symbolRef is null)
            {
                continue;
            }
            foreach(var location in symbolRef.LocationsOfReferences)
            {
                try
                {
                    var node = model.SyntaxTree.GetSyntaxNodeFromLocation(location);
                    if(node.Ancestors().WithinCodeBlock().OfType<AssignmentExpressionSyntax>().FirstOrDefault() is AssignmentExpressionSyntax assignmentExpr)
                    {
                        var left = model.GetSymbolInfo(assignmentExpr.Left).Symbol;
                        pushIfNotParsed(left);
                        if(currentSymbol is not IParameterSymbol)
                        {
                            var right = model.GetSymbolInfo(assignmentExpr.Right).Symbol;
                            pushIfNotParsed(right);
                        }
                    }

                    if(node.AncestorsAndSelf().WithinCodeBlock().OfType<VariableDeclaratorSyntax>().FirstOrDefault() is VariableDeclaratorSyntax es)
                    {
                        pushIfNotParsed(model.GetDeclaredSymbol(es));
                    }
                }
                catch(ArgumentOutOfRangeException)
                {
                    Logger.Trace("Location {location} is outside analysis scope. Cannot get syntax node!", location);
                }
            }
        }

        Logger.Trace("Found related symbols: {symbols}.", rets.ForEachAndJoinToString());
        return rets.ToImmutableArray();

        void pushIfNotParsed(ISymbol? symbol)
        {
            if(symbol is not null && !parsedSymbols.Contains(symbol))
            {
                if(TrackBreak.Any(e => e.Invoke(symbol)))
                {
                    Logger.ConditionalTrace("Ignoring symbol {name}, because of custom break function.", symbol.Name);
                    return;
                }
                queuedSymbols.Push(symbol);
            }
        }
    }

    public void AddTrackBreak(Predicate<ISymbol> func)
    {
        TrackBreak.Add(func);
    }

    public List<Predicate<ISymbol>> TrackBreak { private get; init; } = new();
    private readonly NLog.Logger Logger;
}