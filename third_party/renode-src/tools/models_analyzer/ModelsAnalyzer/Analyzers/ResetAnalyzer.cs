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
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static ModelsAnalyzer.Rules;
using static ModelsAnalyzer.ContextualHelpers;

namespace ModelsAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ResetAnalyzer : DiagnosticAnalyzer, IAnalyzerWithStatus
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleNoResetMethod, RuleMemberNotReset);

    public ProtectedAnalyzerStatus AnalyzerStatus { get; } = new();

    private readonly HashSet<ISymbol> candidateResetSymbols = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> referencedSymbols = new(SymbolEqualityComparer.Default);

#pragma warning disable RS1026 // "Enable concurrent execution"
    public override void Initialize(AnalysisContext context)
    {
        // context.EnableConcurrentExecution(); // HashSets are not thread safe, but still there is only one action registered, that won't execute concurrently
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        AnalyzerStatus.Reset();
        referencedSymbols.Clear();
        candidateResetSymbols.Clear();

        // This analyzer works in two phases
        // * first it finds all candidate symbols, that should be reset and stores them in `candidateResetSymbols`
        // * then it parses Reset() method and descends into methods invoked from Reset(), that are not invoked on Members or Locals - potential helper resets
        // finally it calculates set difference and outputs symbols that should be reset, but are not
        context.RegisterSemanticModelAction(ctx =>
        {
            var cls = ctx.SemanticModel.SyntaxTree.GetAllClasses();

            foreach(var cl in cls)
            {
                if(ctx.SemanticModel.GetDeclaredSymbol(cl) is not INamedTypeSymbol classSymbol)
                {
                    AnalyzerStatus.Error();
                    throw new Exception("Class does not resolve to NamedTypeSymbol, something is seriously wrong.");
                }
                if(!IsClassAPeripheral(classSymbol))
                {
                    Logger.Trace("{class} is not a peripheral, but some helper utility. Moving forward.", classSymbol.Name);
                    continue;
                }

                var resetSymbols = FilterPropertiesAndNonConstFields(classSymbol.GetMembers());

                var defaultParser = new InnerParser();
                // Install symbol filters - if you want to filter out specific symbol type, you probably should start here
                // or create a new InnerParser with different filter mode
                defaultParser.AddCommonParser(FilterMethods.IgnorePrimitives);
                defaultParser.AddCommonParser(FilterMethods.IgnoreIRegisterField);
                defaultParser.AddCommonParser(FilterMethods.IgnoreMachine);

                // TODO: look at interruptManager's constructor to see it's mode of operation (if it will use attributes or get irq as constructor attribute)
                var interruptManager = resetSymbols
                    .Where(s => TypeHelpers.IsEqualToTypeString(FilterMethods.getTypeInfo(s), InterruptManagerTypeReferenceName, true));
                if(interruptManager.Any())
                {
                    defaultParser.AddCommonParser(FilterMethods.IgnoreGPIOsIfTaggedWithIrqProviderAttribute);
                }

                resetSymbols = defaultParser.FilterOut(resetSymbols);
                candidateResetSymbols.UnionWith(resetSymbols);

                Logger.Trace("Found filtered members for class {class}: {members}", classSymbol.Name, candidateResetSymbols.ForEachAndJoinToString());

                // Find Reset() and try parsing it
                if(!PrepareForResetAnalysis(ctx, classSymbol, cl))
                {
                    continue;
                }
            }

            var symbolsNotReset = candidateResetSymbols.Except(referencedSymbols);
            Logger.Debug("These symbols are not reset: {symbols}", symbolsNotReset.ForEachAndJoinToString());

            foreach(var symbol in symbolsNotReset)
            {
                foreach(var location in symbol.Locations)
                {
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(RuleMemberNotReset, location, symbol.Name)
                    );
                }
            }

            AnalyzerStatus.Pass();
        });

    }

    // Only return Properties and Fields
    // Additionally fields can't be const - it would have no sense to consider them for reset
    static IEnumerable<ISymbol> FilterPropertiesAndNonConstFields(IEnumerable<ISymbol> classMembers)
    {
        return classMembers
                    .Where(m => !m.IsImplicitlyDeclared)
                    .Where(m =>
                        (m is IPropertySymbol ps)
                        || (m is IFieldSymbol fs && !fs.IsConst)
                    );
    }

    class InnerParser
    {
        public InnerParser(FilterModeKind filterMode = FilterModeKind.AllFail)
        {
            FilterMode = filterMode;
        }

        public IEnumerable<ISymbol> FilterOut(IEnumerable<ISymbol> symbolList)
        {
            foreach(var fieldOrPropertySymbol in symbolList)
            {
                var innerResults = ParseInner(fieldOrPropertySymbol);

                Func<IEnumerable<bool>, bool> filterCondition = FilterMode switch
                {
                    FilterModeKind.AllPass => (results) => results.All(result => result),
                    FilterModeKind.AllFail => (results) => results.All(result => !result),
                    FilterModeKind.AnyPass => (results) => results.Any(result => result),
                    FilterModeKind.AnyFail => (results) => results.Any(result => !result),
                    _ => throw new ArgumentException("Invalid filter mode")
                };

                if(filterCondition.Invoke(innerResults))
                {
                    yield return fieldOrPropertySymbol;
                }
            }
        }

        IEnumerable<bool> ParseInner(ISymbol fieldOrPropertySymbol)
        {
            return fieldOrPropertySymbol switch
            {
                IPropertySymbol property => PropertiesParsers.Select(parser => parser.Invoke(property)),
                IFieldSymbol field => FieldsParsers.Select(parser => parser.Invoke(field)),
                _ => throw new Exception("Unexpected type symbol")
            };
        }

        public FilterModeKind FilterMode { get; init; }

        // When not to ignore a symbol
        // e.g. AllFail means we don't filter out a symbol out when all hooks return false
        public enum FilterModeKind
        {
            AllPass,
            AllFail,
            AnyPass,
            AnyFail,
        }

        public void AddCommonParser(Func<ISymbol, bool> parser)
        {
            PropertiesParsers.Add(parser);
            FieldsParsers.Add(parser);
        }

        public List<Func<IPropertySymbol, bool>> PropertiesParsers { get; init; } = new();
        public List<Func<IFieldSymbol, bool>> FieldsParsers { get; init; } = new();
    }

    private static class FilterMethods
    {
        // IRQs tagged with IrqProviderAttribute should be handled by InterruptManager
        // it's not always the case, e.g. if it receives IRQ as ctor parameter, and this is TODO!
        internal static bool IgnoreGPIOsIfTaggedWithIrqProviderAttribute(ISymbol symbol)
        {
            var typeInfo = getTypeInfo(symbol) as INamedTypeSymbol;
            if(TypeHelpers.DoesImplementInterface(typeInfo, GPIOInterfaceReferenceName))
            {
                if(symbol.GetAttributes().Where(e => e.AttributeClass?.OriginalDefinition.ToDisplayString() == IrqProviderAttributeReferenceName).Any())
                {
                    Logger.Trace("Skipping member since it's a GPIO that should be controlled by InterruptManager: {name}", symbol.Name);
                    return true;
                }
            }
            return false;
        }

        // We probably don't need to reset Machine object within a peripheral
        internal static bool IgnoreMachine(ISymbol symbol)
        {
            var typeInfo = getTypeInfo(symbol).DecayArrayTypeToElementType();
            if(typeInfo.OriginalDefinition.ToDisplayString() == MachineTypeReferenceName)
            {
                Logger.Trace("Skipping member since it's a Machine : {name}", symbol.Name);
                return true;
            }
            return false;
        }

        internal static bool IgnorePrimitives(ISymbol symbol)
        {
            var typeInfo = getTypeInfo(symbol).DecayArrayTypeToElementType();
            var readOnly = isReadOnly(symbol);
            // IsUnmanaged type will fetch primitives like long, int, etc. which don't satisfy typeInfo.IsReadOnly
            // example of what we can detect with this: public int Size => 0x100; - a common pattern
            if((typeInfo.IsReadOnly || typeInfo.IsUnmanagedType) && readOnly)
            {
                Logger.Trace("Skipping member of readonly type of readonly property: {name}", symbol.Name);
                return true;
            }
            return false;
        }

        // Skips IRegisterField (should be reset when RegisterCollection is reset)
        // also skips jagged arrays of IRegisterField
        internal static bool IgnoreIRegisterField(ISymbol symbol)
        {
            ITypeSymbol typeInfo = getTypeInfo(symbol);
            do
            {
                typeInfo = typeInfo.DecayArrayTypeToElementType();
            }
            while(typeInfo is IArrayTypeSymbol);

            if(typeInfo is INamedTypeSymbol mns)
            {
                if(TypeHelpers.DoesImplementInterface(mns, RegisterFieldTypeReferenceName, true))
                {
                    Logger.Trace("Skipping IRegisterField: {name}", symbol.Name);
                    return true;
                }
            }
            return false;
        }

        internal static ITypeSymbol getTypeInfo(ISymbol symbol)
        {
            return symbol switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => throw new Exception("Invalid type."),
            };
        }

        internal static bool isReadOnly(ISymbol symbol)
        {
            return symbol switch
            {
                IPropertySymbol property => property.IsReadOnly,
                IFieldSymbol field => field.IsReadOnly,
                _ => throw new Exception("Invalid type."),
            };
        }
    }

    // Finds `Reset()` function and executes recursive walker
    // returns false if it can't find Reset function or the function is abstract (so has no body)
    bool PrepareForResetAnalysis(SemanticModelAnalysisContext ctx, INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclarationNode)
    {
        var resetMethod = classSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name == "Reset").SingleOrDefault();
        if(resetMethod is null)
        {
            Logger.Trace("No Reset method!");
            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleNoResetMethod, classDeclarationNode.GetNameToken().GetLocation(), classSymbol.Name
            ));
            return false;
        }
        if(resetMethod.IsAbstract)
        {
            Logger.Trace("Abstract Reset, no sense in parsing it further.");
            return false;
        }

        RecursivelyParseReset(ctx, resetMethod.Locations, MaxParseDescentDepth);

        return true;
    }

    // This function parses Reset() and saves referenced symbols
    // It descends into invoked functions if they aren't invoked on class members or local variables
    // maxDepth controls how deep should it go
    // It won't enter base.Reset()
    void RecursivelyParseReset(SemanticModelAnalysisContext ctx, IEnumerable<Location> locations, int maxDepth)
    {
        foreach(var loc in locations)
        {
            try
            {
                var syntaxNode = ctx.SemanticModel.SyntaxTree.GetSyntaxNodeFromLocation(loc);
                var operations = ctx.SemanticModel.GetOperation(syntaxNode).DescendantsAndSelf();
                foreach(var operation in operations)
                {
                    if(operation is IMemberReferenceOperation op)
                    {
                        var parent = op.Parent;
                        // backtrack through operations
                        // we can't be sure if the whole array is reset, but let's be optimistic
                        while(parent is IArgumentOperation or IConversionOperation or IArrayElementReferenceOperation)
                        {
                            // is passed as regular argument to a function
                            // this means that object.DoStuff() is accepted, but DoStuff(object) is not
                            // this might sound convoluted, but that's usually how it's done
                            if(parent is IArgumentOperation argOp && !argOp.IsImplicit)
                            {
                                break;
                            }
                            parent = parent.Parent;
                        }
                        if(!(parent is IAssignmentOperation or IInvocationOperation))
                        {
                            Logger.Trace("Op {operation} is not an assignment or invocation, not adding it as reset. {operation}", op.Member.ToString(), parent?.ToString());
                            continue;
                        }
                        Logger.Trace("Adding op member as reset: {operation}", op.Member.ToString());
                        referencedSymbols.Add(op.Member);
                    }
                    else if(operation is IInvocationOperation invocation)
                    {
                        // this case should be handled by analyzer above.
                        // we search for methods, that are invoked - possibly helper resets
                        // LocalReference are ops on locally defined variables - this is TODO, but safer to ignore them for now - will lead to false positives, but it's ok
                        if(operation.DescendantsAndSelf().OfType<IMemberReferenceOperation>().Any()
                            || operation.DescendantsAndSelf().OfType<ILocalReferenceOperation>().Any()
                        )
                        {
                            continue;
                        }
                        Logger.Trace("Invocation of {invocation} found.", invocation.TargetMethod.ToString());

                        if(invocation.TargetMethod.Name == "Reset" /*&& invocation.TargetMethod.IsVirtual*/)
                        {
                            Logger.Trace("Invocation of {invocation} is base.Reset().", invocation.TargetMethod.ToString());
                            continue;
                        }
                        if(maxDepth > 0)
                        {
                            Logger.Trace("Descending into: {method}", invocation.TargetMethod.Name);
                            RecursivelyParseReset(ctx, invocation.TargetMethod.Locations, maxDepth - 1);
                        }
                        else
                        {
                            Logger.Warn("Max parse depth exceeded, this peripheral is really complicated, or is there a cycle in Reset involved?");
                            AnalyzerStatus.Incomplete();
                        }
                    }
                }
            }
            catch(ArgumentOutOfRangeException)
            {
                // TODO: it's in syntax tree of other file - what can we do??
                Logger.Warn("Symbol location outside of syntax scope: {location}", loc.ToString());
                AnalyzerStatus.Incomplete();
            }
        }
    }

    // Maximum depth Reset() walker will descend into (RecursivelyParseReset)
    public int MaxParseDescentDepth { get; set; } = 25;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

}