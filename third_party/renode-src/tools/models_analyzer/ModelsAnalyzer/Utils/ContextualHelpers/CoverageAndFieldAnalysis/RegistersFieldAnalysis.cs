//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ModelsAnalyzer.RegisterFieldAnalysisHelpers;
using static ModelsAnalyzer.FunctionHelpers;
using static ModelsAnalyzer.SymbolHelpers;

namespace ModelsAnalyzer;

internal class RegisterFieldAnalysis
{
    public RegisterFieldAnalysis(NLog.Logger? logger = null, Action? onUnimplementedFeature = null, Action? onInvalidState = null)
    {
        Logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
        OnUnimplementedFeature = onUnimplementedFeature;
        OnInvalidState = onInvalidState;
    }

    // For best results pass all invocation chains of a register field at once
    public IList<RegisterFieldInfo> ObtainCoverageFromCallChains(SemanticModel semanticModel, IEnumerable<InvocationExpressionSyntax> coverageGenerators)
    {
        var currentRegisterCoverage = new List<RegisterFieldInfo>();
        uint uniqueId = 0, blockId = 0;
        foreach(var invocation in coverageGenerators)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).GetSymbolOrThrowException();
            var invokedFunctionName = symbol.Name; // is a name of invoked function

            Logger.Trace("Found coverage generator: {name}", invokedFunctionName);

            // TODO - not needed recalculation ?
            var analyzedInvocation = ResolveArgumentsInFunctionInvocation(invocation, semanticModel);

            // Coverage generator handling is quite liberal (the only requirement is that it should have an argument named 'position'), but let's restrict searching to several predefined namespaces
            if(!permittedGeneratorNamespaces.Contains(symbol.ContainingSymbol.ToDisplayString()))
            {
                Logger.Debug("Ignoring {symbol} as a valid coverage generator, because it doesn't belong to whitelisted namespace.", symbol.Name);
                Logger.ConditionalTrace("It belongs to {namespace}", symbol.ContainingSymbol.ToDisplayString());
                Logger.ConditionalTrace("Whitelisted namespaces are: {namespace}", permittedGeneratorNamespaces.ForEachAndJoinToString());
                continue;
            }
            else
            {
                // Reset isn't a generator even though it's within namespace, skip it
                if(symbol.Name == "Reset")
                {
                    Logger.Debug("Ignoring Reset symbol, as it's not a coverage generator.");
                    continue;
                }
            }

            // What we got is likely a coverage generator now
            var fieldInfo = GetFieldInfoFromInvocation(analyzedInvocation, invokedFunctionName, invocation.GetLocation(), ref uniqueId, ref blockId);
            // but we can still fail - fail with error then
            if(fieldInfo is null)
            {
                Logger.Error("{name} at line,column {location} is not a valid coverage generator, add it to the blacklist, or fix its syntax for the analyzer (a coverage generator has 'position' and optional 'width' arguments).",
                    invokedFunctionName,
                    invocation.GetLocation().GetMappedLineSpan().StartLinePosition);
                OnInvalidState?.Invoke();
                continue;
            }

            currentRegisterCoverage.Add(fieldInfo);
        }

        foreach(var (cov1, cov2) in currentRegisterCoverage.PairCombinations())
        {
            // TODO: This should be improved taking ifs, elses into account, not just being in one code block
            if(semanticModel.SyntaxTree.WithinSameCodeBlock(cov1.Location, cov2.Location))
            {
                // cov2 is in the inner loop
                cov2.BlockId = cov1.BlockId;
            }
        }

        Logger.Trace("Obtained coverage: {info}", currentRegisterCoverage.ForEachAndJoinToString("\n"));
        return currentRegisterCoverage;
    }

    private RegisterFieldInfo? GetFieldInfoFromInvocation(IEnumerable<ResolvedFunctionArgument> analyzedInvocation, string invokedFunctionName, Location location, ref uint uniqueId, ref uint blockId)
    {
        // Iterate over arguments of function invocation
        int width = 1;
        int position = -1;
        var name = String.Empty;
        var kind = RegisterFieldInfoSpecialKind.None;
        bool hasReadCb = false, hasWriteCb = false, hasChangeCb = false, hasValueProviderCb = false;
        var fieldMode = new List<string>();

        foreach(var arg in analyzedInvocation)
        {
            // If user registered callback in source, we remember it
            if(arg.IsExplicitlyProvided)
            {
                switch(arg.ArgumentName)
                {
                    case "readCallback":
                        hasReadCb = true;
                        break;
                    case "writeCallback":
                        hasWriteCb = true;
                        break;
                    case "changeCallback":
                        hasChangeCb = true;
                        break;
                    case "valueProviderCallback":
                        hasValueProviderCb = true;
                        break;
                }
            }
            if(!arg.IsValueConst)
            {
                switch(arg.ArgumentName)
                {
                    case "count":
                    case "width":
                        kind |= RegisterFieldInfoSpecialKind.VariableLength;
                        break;
                    case "position":
                        kind |= RegisterFieldInfoSpecialKind.VariablePosition;
                        position = 0; // Not really, but we see the kind in summary
                        break;
                    case "mode": // See below
                        kind |= RegisterFieldInfoSpecialKind.VariableAccessMode;
                        fieldMode.AddRange(arg.InvokedArgumentExpression.ToString().Split('|').Select(e => e.Trim()));
                        break;
                }

                Logger.Trace("Argument {name} has no constant value.", arg.ArgumentName);
                continue;
            }

            var value = arg.ConstValue.Value!;
            switch(arg.ArgumentName)
            {
                case "count":
                case "width":
                    width = (int)Convert.ChangeType(value, typeof(int));
                    break;
                case "position":
                    position = (int)Convert.ChangeType(value, typeof(int));
                    break;
                case "name":
                    name = (string)value;
                    break;
                case "mode":
                    // It's intentional - we want a descriptive name instead of a bit field that is FieldMode
                    fieldMode.AddRange(arg.InvokedArgumentExpression.ToString().Split('|').Select(e => e.Trim()));
                    break;
            }
        }

        if(position == -1)
        {
            return null;
        }

        Logger.Trace("Found coverage at {position} with width {width}", position, width);

        if(invokedFunctionName.Contains("Reserved", StringComparison.OrdinalIgnoreCase))
        {
            kind |= RegisterFieldInfoSpecialKind.Reserved;
            if(name == string.Empty)
            {
                name = "RESERVED";
            }
        }

        if(invokedFunctionName.Contains("Ignored", StringComparison.OrdinalIgnoreCase))
        {
            kind |= RegisterFieldInfoSpecialKind.Ignored;
            if(name == string.Empty)
            {
                name = "IGNORED";
            }
        }

        if(invokedFunctionName.Contains("Tag", StringComparison.OrdinalIgnoreCase))
        {
            kind |= RegisterFieldInfoSpecialKind.Tag;
        }

        return new RegisterFieldInfo(
            uniqueId++,
            position..(position + width - 1),
            name,
            invokedFunctionName,
            location,
            kind,
            new CallbackInfo(hasReadCb, hasWriteCb, hasChangeCb, hasValueProviderCb),
            fieldMode
        )
        {
            BlockId = blockId++
        };
    }

    // Namespaces containing symbols that generate coverage
    private readonly string[] permittedGeneratorNamespaces = new[]
    {
        ContextualHelpers.PeripheralRegisterGenericExtensionsLocations,
        ContextualHelpers.PeripheralRegisterSymbol
    };

    private readonly Action? OnUnimplementedFeature;
    private readonly Action? OnInvalidState;
    private readonly NLog.Logger Logger;
}