//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModelsAnalyzer;

public class RegisterFormatIdentifier
{
    public RegisterFormatIdentifier(SemanticModel semanticModel, NLog.Logger? logger = null, Action? onInvalidState = null)
    {
        Logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
        SemanticModel = semanticModel;
        OnInvalidState = onInvalidState;
    }

    public bool IsRegisterUsedInSwitchStatement(Location location, ISymbol s)
        => IsRegisterUsedInSwitchStatement(location, s, out _);

    public bool IsRegisterUsedInSwitchStatement(Location location, ISymbol s, out int? width)
    {
        width = null;
        var node = SemanticModel.SyntaxTree.GetSyntaxNodeFromLocation(location);
        var switchAncestors = node.Ancestors().OfType<SwitchStatementSyntax>();
        //var methodAncestors = node.Ancestors().OfType<MethodDeclarationSyntax>();

        // proposal: check signature of the function if necessary (if it returns something or receives something)
        // TODO

        foreach(var sw in switchAncestors)
        {
            // if above us is a switch case that casts some offset val to Register enum this might be a way to simulate access (e.g. AppUart.cs)
            var originalType = SemanticModel.GetOriginalTypeInfo(sw.Expression);
            var convertedType = SemanticModel.GetTypeInfo(sw.Expression).ConvertedType;
            if(SymbolEqualityComparer.Default.Equals(s.ContainingType, convertedType))
            {
                width = GuessWidthFromBuiltInType(originalType.Type?.SpecialType);
                return true;
            }
            else
            {
                Logger.Trace("Expression at {location} does not convert to Registers type.",
                    sw.Expression.GetLocation().GetMappedLineSpan());
                // Don't warn, it should be OK - if the expression doesn't cast to Register enum, ignore it
                // OnInvalidState?.Invoke();
            }
        }

        return false;
    }

    private static int? GuessWidthFromBuiltInType(SpecialType? type)
    {
        return type switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte => 8,
            SpecialType.System_Int16 or SpecialType.System_UInt16 => 16,
            SpecialType.System_Int32 or SpecialType.System_UInt32 => 32,
            SpecialType.System_Int64 or SpecialType.System_UInt64 => 64,
            _ => null
        };
    }

    public bool IsRegisterDefinedInExtensionSyntax(Location location, ISymbol s)
        => IsRegisterDefinedInExtensionSyntax(location, s, out _);

    public bool IsRegisterDefinedInExtensionSyntax(Location location, ISymbol s, out int? width)
    {
        width = null;
        var node = SemanticModel.SyntaxTree.GetSyntaxNodeFromLocation(location);

        // we check if the register is being accessed e.g. Register.X
        // otherwise the register might be used for another reason (e.g. in MPC5567_INTC to compute offset), but not an error
        if(node.Parent is not MemberAccessExpressionSyntax initExpr)
        {
            Logger.Trace("{0} is not a MemberAccessSyntax, not defining register {1}?",
                node.Parent?.ToFullString(), s.Name);
            return false;
        }

        // if the grandparent is an Invocation Expression, we check if it is one that defines something
        // e.g. Define32. This list needs to be maintained by hand.
        if(initExpr.Parent?.Parent is InvocationExpressionSyntax e)
        {
            var symbolInfo = SemanticModel.GetSymbolInfo(e);
            var symbolStr = symbolInfo.GetSymbolOrThrowException().ContainingSymbol.ToDisplayString();

            if(ContextualHelpers.RegisterDefinitionExtensionsLocations.Contains(symbolStr))
            {
                width = ContextualHelpers.SymbolExtensionsLocationsToWidthMap[symbolStr];
                return true;
            }
            else
            {
                Logger.Error("Cannot determine width for Register {field}.", s.Name);
                OnInvalidState?.Invoke();
            }
        }

        return false;
    }

    public bool IsRegisterDefinedInDictSyntax(Location location, ISymbol s)
        => IsRegisterDefinedInDictSyntax(location, s, out _);

    public bool IsRegisterDefinedInDictSyntax(Location location, ISymbol s, out int? width)
    {
        width = null;
        var node = SemanticModel.SyntaxTree.GetSyntaxNodeFromLocation(location);
        var initExpr = node.Ancestors().OfType<InitializerExpressionSyntax>();

        // Approach:
        // -> check if our ancestor (not necessarily direct parent) is of InitializerExpression - this would mean
        // we might assign the object to a Dict
        if(!initExpr.Any())
        {
            return false;
        }
        // -> check the specific initializer kind
        foreach(var e in initExpr)
        {
            if(e.IsKind(SyntaxKind.ComplexElementInitializerExpression))
            {
                // this is likely a dict key-val item so
                // check if our parent is a dict
                if(e.Parent.IsKind(SyntaxKind.CollectionInitializerExpression))
                {
                    // check if the register symbols is a key in the dict
                    // TODO refactor?
                    if(!e.Expressions[0].DescendantNodesAndSelf().Contains(node))
                    {
                        Logger?.Trace("Register isn't a dict key at: {location}", node.GetLocation().GetMappedLineSpan());
                        return false;
                    }


                    if(e.Parent.Parent is ObjectCreationExpressionSyntax dict
                        && dict.Type is GenericNameSyntax gs)
                    {
                        var registerType = gs.TypeArgumentList.Arguments[1];
                        var symbolStr = SemanticModel.GetSymbolInfo(registerType).GetSymbolOrThrowException().ToDisplayString();

                        try
                        {
                            width = ContextualHelpers.RegisterDefinitionLocationsToWidthMap[symbolStr];
                        }
                        catch(KeyNotFoundException)
                        {
                            Logger?.Warn("Type symbol at location cannot be mapped to width: {location} ", registerType.GetLocation());
                        }

                        // it is
                        return true;
                    }
                    else
                    {
                        Logger?.Warn("Cannot determine width for Register {field}", s.Name);
                        OnInvalidState?.Invoke();
                    }
                }
                else
                {
                    Logger?.Warn("Direct parent of ComplexElementInitializer of Register {0} is not a dict.", s.Name);
                    OnInvalidState?.Invoke();
                    continue;
                }
            }
            else
            {
                Logger?.Warn("Syntax kind {1} for initialization of {0} is unexpected. Inspect the case manually and fix the analyzer.",
                    s.Name,
                    e.Kind().ToString());
                OnInvalidState?.Invoke();
                continue;
            }
        }

        return false;
    }

    private readonly SemanticModel SemanticModel;

    private readonly NLog.Logger Logger;
    private readonly Action? OnInvalidState;

}