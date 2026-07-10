//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ModelsAnalyzer.RegisterFieldAnalysisHelpers;
using static ModelsAnalyzer.SymbolHelpers;
using static ModelsAnalyzer.FunctionHelpers;

namespace ModelsAnalyzer;

public class RegisterAnalysis
{
    public RegisterAnalysis(SemanticModel semanticModel, NLog.Logger? logger = null, Action? onUnimplementedFeature = null, Action? onInvalidState = null)
    {
        Logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
        SemanticModel = semanticModel;
        AllSymbols = GetAllReferencedSymbols(SemanticModel);
        AllSymbolsAndDeclarations = GetAllReferencedSymbols(semanticModel, includeDeclarations: true);

        OnUnimplementedFeature = onUnimplementedFeature;
        OnInvalidState = onInvalidState;

        symbolTrackerForDict = new(Logger);
        symbolTrackerForExtension = new(Logger);

        // Stop tracking on different type than Double/Byte/.../Register
        Predicate<ISymbol> onlyRegisterRule = symbol =>
            !TypeHelpers.IsEqualToTypeString(symbol.GetTypeSymbolIfPresent(), true, ContextualHelpers.RegisterTypes);

        symbolTrackerForDict.AddTrackBreak(onlyRegisterRule);
        symbolTrackerForExtension.AddTrackBreak(onlyRegisterRule);

        fieldAnalysis = new(Logger, OnUnimplementedFeature, OnInvalidState);

        registerFormatIdentifier = new RegisterFormatIdentifier(semanticModel, Logger, onInvalidState);
    }

    public RegisterInfo GetRegisterInfo(RegisterEnumField registerElement)
    {
        // This register has been implicitly declared previously, e.g. by DefineMany with step - there will be no new fields to analyze
        if(GetDummyReg(registerElement) is DummyReg reg)
        {
            // We transcribe some properties from the parent reg, so the output format is more consistent and easier to read
            return new RegisterInfo(
                registerElement.RegisterSymbol.Name,
                registerElement.RegisterSymbol.Name,
                registerElement.RegisterAddress,
                reg.ParentRegisterInfoPointer.Width,
                reg.ParentRegisterInfoPointer.ResetValue,
                CallbackInfo: reg.ParentRegisterInfoPointer.CallbackInfo,
                SpecialKind: reg.ParentRegisterInfoPointer.SpecialKind,
                ParentReg: reg.ParentRegisterInfoPointer.Name
            )
            {
                Fields = reg.ParentRegisterInfoPointer.Fields
            };
        }

        // TODO: consider only definition locations
        var aggregatedInvocations = ExpandRegisterCreation(registerElement, out var width);
        if(aggregatedInvocations is null)
        {
            return new RegisterInfo(registerElement.RegisterSymbol.Name, registerElement.RegisterSymbol.Name, registerElement.RegisterAddress, SpecialKind: RegisterSpecialKind.MaybeUndefined);
        }

        var coverageGenerators = HandleDefinition(aggregatedInvocations, registerElement, out var partialRegisterInfo, out var isDefined);
        var fieldInfoList = fieldAnalysis.ObtainCoverageFromCallChains(SemanticModel, coverageGenerators);

        var ret = new RegisterInfo(
            String.IsNullOrEmpty(partialRegisterInfo.Name) ? registerElement.RegisterSymbol.Name : partialRegisterInfo.Name,
            registerElement.RegisterSymbol.Name,
            registerElement.RegisterAddress,
            width,
            partialRegisterInfo.ResetValue,
            CallbackInfo: partialRegisterInfo.CallbackInfo,
            SpecialKind: !isDefined ? RegisterSpecialKind.NoDefineFound : RegisterSpecialKind.None,
            ArrayInfo: partialRegisterInfo.Array
        );
        ret.Fields.AddRange(fieldInfoList);

        // Link the children to the parent (DefineMany), so we can recover common properties later
        foreach(var dummyReg in partialRegisterInfo.LinkedDummyRegs)
        {
            dummyReg.ParentRegisterInfoPointer = ret;
        }

        return ret;
    }

    /// <summary>
    /// Register symbol for DefineMany tracking - this way, we can check if other registers been initialized with DefineMany.
    /// Also makes use a of all RegisterAnalysis goodies, like reference tracking, but less overhead since we don't investigate coverage fully.
    /// </summary>
    /// <remarks>
    /// For safety, don't use in conjunction with GetRegisterInfo, on the same class instance.
    /// </remarks>
    // TODO: This is still not a nice solution, will need to be improved
    public void TrackDefineMany(RegisterEnumField registerElement, Location location)
    {
        var aggregatedInvocations = ProcessRegisterSymbolAtLocation(registerElement, location, out _);
        if(!aggregatedInvocations.Any())
        {
            return;
        }
        _ = HandleDefinition(aggregatedInvocations, registerElement, out _, out _);
    }

    /// <summary> Get parent register - the one where DefineMany was invoked </summary>
    public DummyReg? GetDummyReg(RegisterEnumField registerElement)
    {
        if(DummyRegs.TryGetValue(registerElement.RegisterAddress, out var dummyReg))
        {
            return dummyReg;
        }
        return null;
    }

    /// <summary>
    /// Given RegisterEnumField this function expands possibly related symbols, and returns list of expressions
    /// that might be definitions or field definitions. To obtain full coverage of fields, call to HandleDefinition is required, to resolve lambdas of DefineMany.
    /// </summary>
    private IList<ExpressionSyntax>? ExpandRegisterCreation(RegisterEnumField registerElement, out int? width)
    {
        width = null;

        var symbolInfo = FilterReferencesToSymbol(registerElement.RegisterSymbol, AllSymbols);
        if(symbolInfo is null)
        {
            return null;
        }

        if(!SymbolEqualityComparer.Default.Equals(symbolInfo.SymbolInfo.Symbol, registerElement.RegisterSymbol))
        {
            throw new ArgumentException("Mismatched symbols in ExpandRegisterCreation");
        }

        var aggregatedInvocations = new List<ExpressionSyntax>();
        foreach(var location in symbolInfo.LocationsOfReferences)
        {
            aggregatedInvocations.AddRange(ProcessRegisterSymbolAtLocation(registerElement, location, out var currentWidth));
            width = Nullable.Compare(width, currentWidth) > 0 ? width : currentWidth;
        }

        return aggregatedInvocations;
    }

    private IEnumerable<ExpressionSyntax> ProcessRegisterSymbolAtLocation(RegisterEnumField registerElement, Location location, out int? currentWidth)
    {
        Logger.ConditionalTrace("Processing symbol {symbol} at {location}", registerElement.RegisterSymbol, location.GetMappedLineSpan().StartLinePosition.ToString());

        if(registerFormatIdentifier.IsRegisterDefinedInExtensionSyntax(location, registerElement.RegisterSymbol, out currentWidth))
        {
            return HandleExtensionSyntax(registerElement.RegisterSymbol, location);
        }
        else if(registerFormatIdentifier.IsRegisterDefinedInDictSyntax(location, registerElement.RegisterSymbol, out currentWidth))
        {
            return HandleDictSyntax(location);
        }
        else
        {
            // Try to obtain width if all else failed, but don't try to search for field coverage
            // registerFormatIdentifier.IsRegisterUsedInSwitchStatement(location, registerElement.RegisterSymbol, out currentWidth);

            Logger.Debug("Skipping coverage analysis for {name} at line,column: {location} - coverage analysis is only supported for declarative definition syntax.",
                registerElement.RegisterSymbol.Name,
                location.GetMappedLineSpan().StartLinePosition);
        }

        return Enumerable.Empty<ExpressionSyntax>();
    }

    private IEnumerable<ExpressionSyntax> HandleExtensionSyntax(ISymbol symbol, Location location)
    {
        return SearchForCoverageDefinitions(symbol, symbolTrackerForExtension);
    }

    private IEnumerable<ExpressionSyntax> HandleDictSyntax(Location location)
    {
        var root = SemanticModel.SyntaxTree.GetSyntaxNodeFromLocation(location);
        // we navigate to the value of dict initializer
        var complexInitializerExpr = root.Ancestors().OfType<InitializerExpressionSyntax>().First();
        if(!complexInitializerExpr.IsKind(SyntaxKind.ComplexElementInitializerExpression))
        {
            Logger.Error("Precondition for dict syntax analysis failed, fix the analyzer! At approx: {location}", location.GetMappedLineSpan().StartLinePosition);
            OnInvalidState?.Invoke();
            return Enumerable.Empty<ExpressionSyntax>();
        }
        if(complexInitializerExpr.Expressions.Count != 2)
        {
            Logger.Error("This initializer has more or less than 2 expressions, that's unexpected. Fix the analyzer! At approx: {location}", location.GetMappedLineSpan().StartLinePosition);
            OnInvalidState?.Invoke();
            return Enumerable.Empty<ExpressionSyntax>();
        }

        var creation = complexInitializerExpr.Expressions[1].DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        if(creation is null)
        {
            Logger.Debug("Object is not created in place, but passed by reference. At approx: {location}", location.GetMappedLineSpan().StartLinePosition);

            var nodes = complexInitializerExpr.Expressions[1].DescendantNodesAndSelf();
            var set = SearchForCoverageDefinitions(
                SemanticModel.GetSymbolInfo(nodes.OfType<IdentifierNameSyntax>().First()).Symbol,
                symbolTrackerForDict
            );
            // handle rare cases when we return register from some function invocation (e.g. CreateRWRegister) that is a static member of class, and not invoked on an object
            set.UnionWith(nodes.OfType<InvocationExpressionSyntax>());
            return set;
        }
        else
        {
            return creation.AncestorsAndSelf().WithinCodeBlock().OfType<InvocationExpressionSyntax>()
                .Append<ExpressionSyntax>(creation.AncestorsAndSelf().WithinCodeBlock().OfType<ObjectCreationExpressionSyntax>().First());
        }
    }

    private static bool IsRegisterDefinition(ISymbol symbol)
    {
        var containingLocationName = symbol.ContainingSymbol.ToDisplayString();
        return ContextualHelpers.RegisterDefinitionExtensionsLocations.Contains(containingLocationName)
                || (symbol is IMethodSymbol && symbol.Name == ".ctor")
                || (symbol is IMethodSymbol && symbol.Name == "CreateRWRegister");
    }


    private record class PartialRegisterInfo(string? Name, CallbackInfo CallbackInfo, long? ResetValue, ArrayOfRegisters Array, List<DummyReg> LinkedDummyRegs);

    private IList<InvocationExpressionSyntax> HandleDefinition(IEnumerable<ExpressionSyntax> expressions, RegisterEnumField registerElement, out PartialRegisterInfo partialRegisterInfo, out bool isDefined)
    {
        bool hasWriteCb = false, hasReadCb = false, hasChangeCb = false;
        string? name = null;
        long? resetValue = null;
        var linkedDummyRegs = new List<DummyReg>();
        var isArrayOfRegister = new ArrayOfRegisters(false, 0, 0);

        var coverageGenerators = new List<InvocationExpressionSyntax>();
        var expandedExpressions = new List<ExpressionSyntax>();
        isDefined = false;

        foreach(var expression in expressions)
        {
            var symbol = SemanticModel.GetSymbolInfo(expression).GetSymbolOrThrowException();
            var containingLocationName = symbol.ContainingSymbol.ToDisplayString();
            var analyzedInvocation = ResolveArgumentsInFunctionInvocation(expression, SemanticModel);

            // Try preserve the original order
            expandedExpressions.Add(expression);

            if(IsRegisterDefinition(symbol))
            {
                // DefineMany - we need to parse lambda it receives
                if(symbol.Name == "DefineMany")
                {
                    expandedExpressions.AddRange(GetCoverageFromDefineMany(analyzedInvocation));
                }
                if(isDefined)
                {
                    Logger.Warn("Redefinition at {location}, might be conditional. Handling this is not yet well supported. This analysis will be unreliable.", expression.GetLocation().GetMappedLineSpan().StartLinePosition);
                    OnUnimplementedFeature?.Invoke();
                }

                isDefined = true;
            }

            // Handling fluent conditional syntax "Then/Else"
            // TODO: this is just simple expansion of lambda
            // it would be nice to format this properly for display, e.g. by assigning sane CodeBlockIds
            if(symbol.Name is "Then" or "Else")
            {
                var conditionalChain = analyzedInvocation.Where(arg => arg.ArgumentName == "action").Single().InvokedArgumentExpression;
                if(conditionalChain.HasValue)
                {
                    if(conditionalChain.Value is LambdaExpressionSyntax lambda)
                    {
                        expandedExpressions.AddRange(GetInvocationChainFromLambdaGeneralized(lambda, 0, symbolTrackerForExtension, SemanticModel));
                    }
                    else
                    {
                        Logger.Warn("Then/Else action parameter is not a lambda. This is currently unsupported by this analyzer.");
                        OnUnimplementedFeature?.Invoke();
                    }
                }
                else
                {
                    Logger.Warn("Then/Else without action lambda. This is unsupported by this analyzer.");
                    OnUnimplementedFeature?.Invoke();
                }
            }
        }

        // For each invocation in the code block (chain of Register.X.Define.WithTag.WithFlag... etc.)
        foreach(var expression in expandedExpressions)
        {
            var symbol = SemanticModel.GetSymbolInfo(expression).GetSymbolOrThrowException();
            var funcName = symbol.Name; // is a name of invoked function

            var analyzedInvocation = ResolveArgumentsInFunctionInvocation(expression, SemanticModel);

#if ADDITIONAL_LOGGING
            Logger.ConditionalTrace(funcName);
            Logger.ConditionalTrace(analyzedInvocation.ForEachAndJoinToString());
            Logger.ConditionalTrace("-----");
#endif

            if(callbacksInjectors.Contains(funcName))
            {
                switch(funcName)
                {
                    case string a when a.Contains("Write"):
                        hasWriteCb = true;
                        break;
                    case string a when a.Contains("Read"):
                        hasReadCb = true;
                        break;
                    case string a when a.Contains("Change"):
                        hasChangeCb = true;
                        break;
                    default:
                        Logger.Error("{name} at line,column {location} is not a recognized callback injector. Don't know what to do with it.",
                            funcName,
                            expression.GetLocation().GetMappedLineSpan().StartLinePosition);
                        OnInvalidState?.Invoke();
                        break;
                };

                continue;
            }

            var containingLocationName = symbol.ContainingSymbol.ToDisplayString();
            if(IsRegisterDefinition(symbol))
            {
                var resetNullableValue = analyzedInvocation.SingleOrDefault(p => p.ArgumentName == "resetValue" && p.IsValueConst)?.ConstValue.Value;
                if(resetNullableValue is not null)
                {
                    resetValue = (long)Convert.ChangeType(resetNullableValue, typeof(long));
                }
                name = (string?)analyzedInvocation.SingleOrDefault(p => p.ArgumentName == "name" && p.IsValueConst)?.ConstValue.Value;
                if(symbol.Name == "DefineMany")
                {
                    foreach((var childRegisterAddr, var childRegister) in GetRegistersCreatedByDefineMany(registerElement, analyzedInvocation, out isArrayOfRegister))
                    {
                        var dummyReg = new DummyReg(childRegister, null!);
                        DummyRegs.Add(childRegisterAddr, dummyReg);
                        linkedDummyRegs.Add(dummyReg);
                    }
                }
                continue;
            }

            if(expression is InvocationExpressionSyntax invocation)
            {
                coverageGenerators.Add(invocation);
            }
        }

        // we now have filtered out Defines and callback injectors (WithXCallback)
        partialRegisterInfo = new PartialRegisterInfo(name, new CallbackInfo(hasReadCb, hasWriteCb, hasChangeCb), resetValue, isArrayOfRegister, linkedDummyRegs);
        return coverageGenerators;
    }

    private IList<(long, ISymbol)> GetRegistersCreatedByDefineMany(RegisterEnumField registerElement, IEnumerable<ResolvedFunctionArgument> analyzedInvocation, out ArrayOfRegisters arrayOfRegister)
    {
        var rets = new List<(long, ISymbol)>();
        arrayOfRegister = new ArrayOfRegisters(true, 0, 0);
        try
        {
            var defineManyCount = (int)Convert.ChangeType(analyzedInvocation.Single(p => p.ArgumentName == "count" && p.IsValueConst).ConstValue.Value!, typeof(int));
            var defineManyStep = (int)Convert.ChangeType(analyzedInvocation.Single(p => p.ArgumentName == "stepInBytes" && p.IsValueConst).ConstValue.Value!, typeof(int));
            arrayOfRegister = new ArrayOfRegisters(true, defineManyCount, defineManyStep);

            Logger?.Trace("DefineMany registers with step {step} and count {count}", defineManyStep, defineManyCount);
            for(var i = 0; i < defineManyCount; ++i)
            {
                rets.Add((registerElement.RegisterAddress + defineManyStep * i, registerElement.RegisterSymbol));
            }
        }
        catch(InvalidOperationException)
        {
            Logger.Warn("DefineMany arguments cannot be resolved at compile time.");
            OnUnimplementedFeature?.Invoke();
        }
        return rets;
    }

    private IList<InvocationExpressionSyntax> GetCoverageFromDefineMany(IEnumerable<ResolvedFunctionArgument> analyzedInvocation)
    {
        // look into lambda of DefineMany
        if(analyzedInvocation.FirstOrDefault(arg => arg.ArgumentName == "setup") is ResolvedFunctionArgument setup)
        {
            if(setup.InvokedArgumentExpression.HasValue)
            {
                if(setup.InvokedArgumentExpression.Value is ParenthesizedLambdaExpressionSyntax lambda)
                {
                    // The first parameter is the register reference
                    return GetInvocationChainFromLambdaGeneralized(lambda, 0, symbolTrackerForExtension, SemanticModel);
                }
                else
                {
                    // TODO - what if it's function call?
                    Logger.Warn("DefineMany setup parameter is not a lambda. This is currently unsupported by this analyzer.");
                    OnUnimplementedFeature?.Invoke();
                }
            }
            else
            {
                Logger.Warn("DefineMany without setup lambda. This is unsupported by this analyzer.");
                OnUnimplementedFeature?.Invoke();
            }
        }

        return new List<InvocationExpressionSyntax>();
    }

    private HashSet<ExpressionSyntax> SearchForCoverageDefinitions(ISymbol? symbolToExpand, SymbolTracker symbolTracker)
    {
        var additionalInvocations = new HashSet<ExpressionSyntax>(); // we will run into repeated symbols many times, remove duplicates

        // Also filtered by type
        var tracked = symbolTracker.FindSymbolsRelatedByAssignment(SemanticModel, symbolToExpand);
        foreach(var track in tracked)
        {
            var locations = SymbolHelpers.FilterReferencesToSymbol(track, AllSymbolsAndDeclarations) ?? throw new Exception("A symbol has no declaration in scope!");
            foreach(var loc in locations.LocationsOfReferences)
            {
                var node = SemanticModel.SyntaxTree.GetSyntaxNodeFromLocation(loc);

                if(node.Parent is MemberAccessExpressionSyntax ms)
                {
                    // VERY special case if the register is named literally "Value" then it's a member of the enum - if so don't skip
                    if(!(SemanticModel.GetSymbolInfo(ms.Expression).Symbol is INamedTypeSymbol ns && ns.EnumUnderlyingType is not null))
                    {
                        // If node accesses .Value member skip this - we are reading a register's field value here
                        if(ms.Name.Identifier.ToString() == "Value")
                        {
                            continue;
                        }
                    }
                }

                additionalInvocations.UnionWith(node.AncestorsAndSelf().WithinCodeBlock().OfType<InvocationExpressionSyntax>());

                // For assignments add right node to parse
                if(node.AncestorsAndSelf()
                    .OfType<AssignmentExpressionSyntax>().FirstOrDefault() is AssignmentExpressionSyntax fs)
                {
                    additionalInvocations.UnionWith(fs.Right.DescendantNodesAndSelf(DontDescendIntoArguments).OfType<InvocationExpressionSyntax>());
                    additionalInvocations.UnionWith(fs.Right.DescendantNodesAndSelf(DontDescendIntoArguments).OfType<ObjectCreationExpressionSyntax>());
                }

                // Same for variable creation with assignment operator
                if(node.DescendantNodesAndSelf()
                    .OfType<EqualsValueClauseSyntax>().FirstOrDefault() is EqualsValueClauseSyntax es)
                {
                    additionalInvocations.UnionWith(es.Value.DescendantNodesAndSelf(DontDescendIntoArguments).OfType<InvocationExpressionSyntax>());
                    additionalInvocations.UnionWith(es.Value.DescendantNodesAndSelf(DontDescendIntoArguments).OfType<ObjectCreationExpressionSyntax>());
                }
            }
        }

        // This way we wont descend into function args in invocation chain and start parsing lambdas
        // This is only needed if we are descending from top to bottom in call chain
        static bool DontDescendIntoArguments(SyntaxNode node)
        {
            if(node is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
            {
                return true;
            }
            return false;
        }

        return additionalInvocations;
    }

    // These function names don't add coverage, but are used to inject callbacks
    private readonly string[] callbacksInjectors = new string[] {
        "WithReadCallback",
        "WithWriteCallback",
        "WithChangeCallback",
        "DefineWriteCallback",
        "DefineReadCallback",
        "DefineChangeCallback",
    };

    // currently only Regs declared by DefineMany
    public class DummyReg
    {
        public DummyReg(ISymbol symbol, RegisterInfo parentRegisterInfoPointer)
        {
            Symbol = symbol;
            ParentRegisterInfoPointer = parentRegisterInfoPointer;
        }
        public ISymbol Symbol;
        public RegisterInfo ParentRegisterInfoPointer;
    }
    private readonly Dictionary<long, DummyReg> DummyRegs = new();

    private readonly RegisterFieldAnalysis fieldAnalysis;
    private readonly RegisterFormatIdentifier registerFormatIdentifier;
    private readonly SymbolTracker symbolTrackerForDict;
    private readonly SymbolTracker symbolTrackerForExtension;

    private readonly SemanticModel SemanticModel;
    private readonly IEnumerable<SymbolInfoWithLocation> AllSymbols;
    private readonly IEnumerable<SymbolInfoWithLocation> AllSymbolsAndDeclarations;

    private readonly NLog.Logger Logger;
    private readonly Action? OnUnimplementedFeature;
    private readonly Action? OnInvalidState;
}