//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModelsAnalyzer;

public static class FunctionHelpers
{
    public record class ResolvedFunctionArgument
    (
        string ArgumentName,
        ITypeSymbol? ArgumentType,
        // If argument's value doesn't resolve to a constant value, Optional will have 'HasValue' set to false
        Optional<object?> ConstValue,
        // Position of argument in ArgumentList in function invocation, or none if argument doesn't exist
        Optional<int> PositionInCall = new(),
        // Expression syntax node for the invocation
        Optional<SyntaxNode> InvokedArgumentExpression = new()
    )
    {
        public bool IsExplicitlyProvided => PositionInCall.HasValue;
        public bool IsValueConst => ConstValue.HasValue;
    }

    public static IEnumerable<ResolvedFunctionArgument> ResolveArgumentsInFunctionInvocation(ExpressionSyntax expression, SemanticModel sm)
    {
        return expression switch
        {
            InvocationExpressionSyntax invocation => ResolveArgumentsInFunctionInvocation(invocation, sm),
            ObjectCreationExpressionSyntax creation => ResolveArgumentsInFunctionInvocation(creation, sm),
            _ => throw new ArgumentException("Type is not valid.")

        };
    }


    public static IEnumerable<ResolvedFunctionArgument> ResolveArgumentsInFunctionInvocation(InvocationExpressionSyntax invocation, SemanticModel sm)
    {
        var arguments = invocation.ArgumentList.Arguments;
        return ResolveArgumentsInFunctionInvocationInner(invocation, arguments, sm);
    }

    public static IEnumerable<ResolvedFunctionArgument> ResolveArgumentsInFunctionInvocation(ObjectCreationExpressionSyntax creation, SemanticModel sm)
    {
        var arguments = creation.ArgumentList?.Arguments;
        if(arguments is null)
        {
            return Enumerable.Empty<ResolvedFunctionArgument>();
        }
        return ResolveArgumentsInFunctionInvocationInner(creation, arguments.Value, sm);
    }

    private static IEnumerable<ResolvedFunctionArgument> ResolveArgumentsInFunctionInvocationInner(ExpressionSyntax expression, SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel sm)
    {
        var vals = new Dictionary<string, ResolvedFunctionArgument>();

        if(sm.GetSymbolInfo(expression).Symbol is not IMethodSymbol methodSymbol)
        {
            throw new InvalidOperationException("Could not get function symbol for invocation. Is the semantic model correct?");
        }

        foreach(var (idx, arg) in arguments.Enumerate())
        {
            string paramName;

            if(arg.NameColon is NameColonSyntax nc)
            {
                paramName = nc.Name.ToString();
            }
            else
            {
                try
                {
                    var param = methodSymbol.Parameters[idx];
                    paramName = param.Name;
                }
                catch(IndexOutOfRangeException)
                {
                    // TODO: variadic argument support - not necessary now, but it won't work
                    continue;
                }
            }

            var value = sm.GetConstantValue(arg.Expression);
            // Convert interpolated string into more or less logical constant value
            if(!value.HasValue && arg.Expression is InterpolatedStringExpressionSyntax iex)
            {
                value = iex.GetText().ToString().TrimStart(new char[] { '@', '$', '"' }).TrimEnd('"');
            }

            var info = sm.GetTypeInfo(arg);
            vals.Add(
                paramName,
                new ResolvedFunctionArgument(paramName, info.ConvertedType ?? info.Type, value, idx, arg.Expression)
            );
        }

        // seed optional parameters
        foreach(var param in methodSymbol.Parameters)
        {
            if(param.HasExplicitDefaultValue)
            {
                var info = param.Type;
                vals.TryAdd(
                    param.Name,
                        new ResolvedFunctionArgument(
                            param.Name,
                            info,
                            param.ExplicitDefaultValue,
                            // Partial methods should have matching signature (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/partial-method)
                            // we just get the first one - if they don't match project won't compile either way
                            InvokedArgumentExpression: ((param.DeclaringSyntaxReferences.First().GetSyntax() as ParameterSyntax)?.Default)?.Value ?? new Optional<SyntaxNode>()
                        )
                );
            }
        }

        return vals.Select(x => x.Value);
    }

    public static IList<InvocationExpressionSyntax> GetInvocationChainFromLambdaGeneralized(LambdaExpressionSyntax lambda, int lambdaParameterIndex, SymbolTracker symbolTracker, SemanticModel semanticModel)
    {
        var rets = new List<InvocationExpressionSyntax>();

        if(lambda is SimpleLambdaExpressionSyntax && lambdaParameterIndex != 0)
        {
            throw new InvalidOperationException("This lambda should only have one argument at index 0");
        }

        var lambdaParam = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters[lambdaParameterIndex],
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            _ => throw new ArgumentException("Lambda is not ParenthesizedLambdaExpressionSyntax and SimpleLambdaExpressionSyntax")
        };

        var regSymbol = semanticModel.GetDeclaredSymbol(lambdaParam).ThrowIfNull();
        var relatedSymbols = symbolTracker.FindSymbolsRelatedByAssignment(semanticModel, regSymbol, lambda);
        var defineManySymbols = SymbolHelpers.GetAllReferencedSymbols(semanticModel, lambda);

        foreach(var relatedSymbol in relatedSymbols)
        {
            var filteredInvocations = SymbolHelpers.FilterReferencesToSymbol(relatedSymbol, defineManySymbols);

            var syntaxNodes = filteredInvocations?.LocationsOfReferences.Select(
                x => semanticModel.SyntaxTree.GetSyntaxNodeFromLocation(x)
            );

            if(syntaxNodes is not null)
            {
                rets.AddRange(
                    syntaxNodes.SelectMany(n => n.Ancestors().TakeUntilType<SyntaxNode, ParenthesizedLambdaExpressionSyntax>().OfType<InvocationExpressionSyntax>())
                );
            }
        }

        return rets;
    }
}