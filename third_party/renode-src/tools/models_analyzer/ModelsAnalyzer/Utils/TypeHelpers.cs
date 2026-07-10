//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModelsAnalyzer;

public static class TypeHelpers
{
    private static string removeGenericPart(string s)
    {
        var pos = s.IndexOf('<');
        return pos >= 0 ? s.Remove(pos) : s;
    }

    public static bool DoesImplementInterface(INamedTypeSymbol? cls, string name, bool genericIndifferent = false)
    {
        if(cls is null)
        {
            return false;
        }

        var ifaces = cls.AllInterfaces.Select(s => s.ToDisplayString());
        if(genericIndifferent)
        {
            ifaces = ifaces.Select(s => removeGenericPart(s));
        }

        return ifaces.Where(s => s == name).Any();
    }

    public static bool IsEqualToTypeString(ITypeSymbol? type, bool genericIndifferent = false, params string[] name)
    {
        return name.Select(e => IsEqualToTypeString(type, e, genericIndifferent)).Any(e => e);
    }

    public static bool IsEqualToTypeString(ITypeSymbol? type, string name, bool genericIndifferent = false)
    {
        if(type is null)
        {
            return false;
        }

        var typeStr = type.ToDisplayString();
        if(genericIndifferent)
        {
            typeStr = removeGenericPart(typeStr);
        }

        if(typeStr == name)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// If <c>s</c> is array type this function decays TypeSymbol to element type
    /// otherwise returns unchanged ITypeSymbol
    /// </summary>
    public static ITypeSymbol DecayArrayTypeToElementType(this ITypeSymbol s)
    {
        return (s as IArrayTypeSymbol)?.ElementType ?? s;
    }

    // There is no base interface for HasType in Roslyn (Field and Property symbols both have types, but they are derived from Symbol which doesn't)
    // so we use reflection
    private static readonly Regex typePropertyRegex = new(@"(^|\.)Type$", RegexOptions.Compiled);
    public static ITypeSymbol? GetTypeSymbolIfPresent(this ISymbol s)
    {
        return s.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SingleOrDefault(e => typePropertyRegex.IsMatch(e.Name))?.GetValue(s) as ITypeSymbol;
    }

    /// <summary>
    /// This function should return the original TypeInfo for expression - that is before explicit cast
    /// </summary>
    public static TypeInfo GetOriginalTypeInfo(this SemanticModel semanticModel, ExpressionSyntax expression)
    {
        var expr = expression switch
        {
            CastExpressionSyntax e => e.Expression,
            IsPatternExpressionSyntax e => e.Expression,
            BinaryExpressionSyntax e when e.IsKind(SyntaxKind.AsExpression) => e.Left,
            _ => expression
        };
        return semanticModel.GetTypeInfo(expr);
    }

}