using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lucent.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MountLifetimeAnalyzer : DiagnosticAnalyzer
{
    public const string DroppedTemporaryId = "LUC004A001";
    public const string RepeatedMountId = "LUC004A002";
    public const string UndisposedLocalId = "LUC004A003";
    public const string EscapedRootId = "LUC004A004";

    private static readonly DiagnosticDescriptor DroppedTemporary = new(
        DroppedTemporaryId,
        "Dispose mounted Lucent component",
        "The mounted Lucent component temporary is not retained for disposal",
        "Lucent.Interop",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RepeatedMount = new(
        RepeatedMountId,
        "Mount a Lucent component once",
        "Lucent component '{0}' is mounted more than once on an executable path",
        "Lucent.Interop",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UndisposedLocal = new(
        UndisposedLocalId,
        "Dispose mounted Lucent component",
        "Mounted Lucent component '{0}' is not disposed on every executable path",
        "Lucent.Interop",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EscapedRoot = new(
        EscapedRootId,
        "Keep mounted Lucent component alive",
        "The native root from Lucent component '{0}' escapes its lexical component lifetime",
        "Lucent.Interop",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DroppedTemporary, RepeatedMount, UndisposedLocal, EscapedRoot];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var generatedComponents = FindGeneratedComponents(start.Compilation);
            if (generatedComponents.Count == 0)
                return;

            start.RegisterOperationBlockStartAction(block =>
            {
                block.RegisterOperationAction(_ => { }, OperationKind.Invocation);
                block.RegisterOperationBlockEndAction(end =>
                {
                    foreach (var operationBlock in end.OperationBlocks)
                    {
                        ControlFlowGraph? graph;
                        try
                        {
                            graph = operationBlock.Parent is IMethodBodyOperation methodBody
                                ? ControlFlowGraph.Create(methodBody)
                                : operationBlock is IBlockOperation blockBody
                                    ? ControlFlowGraph.Create(blockBody)
                                    : null;
                        }
                        catch (ArgumentException)
                        {
                            continue;
                        }
                        if (graph is not null)
                            AnalyzeGraph(end, graph, generatedComponents);
                    }
                });
            });
        });
    }

    private static void AnalyzeGraph(
        OperationBlockAnalysisContext context,
        ControlFlowGraph graph,
        HashSet<INamedTypeSymbol> generatedComponents)
    {
        var incoming = new Dictionary<BasicBlock, FlowState>();
        var pending = new Queue<BasicBlock>();
        var exits = new List<FlowState>();
        var entry = graph.Blocks.First(block => block.Kind == BasicBlockKind.Entry);
        var exit = graph.Blocks.First(block => block.Kind == BasicBlockKind.Exit);
        incoming[entry] = new FlowState();
        pending.Enqueue(entry);

        while (pending.Count > 0)
        {
            var block = pending.Dequeue();
            var state = incoming[block].Clone();
            var transfer = new TransferWalker(context, state, generatedComponents);
            foreach (var operation in block.Operations)
                transfer.Visit(operation);
            if (block.BranchValue is { } branchValue)
                transfer.Visit(branchValue);

            if (Successors(block).Any(successor => ReferenceEquals(successor, exit)) &&
                UnwrapLocalReference(block.BranchValue) is { } returnedRoot &&
                state.RootOwners.TryGetValue(returnedRoot.Local, out var rootOwners))
            {
                foreach (var owner in rootOwners)
                {
                    if (state.UsingLocals.Contains(owner))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            EscapedRoot,
                            block.BranchValue!.Syntax.GetLocation(),
                            owner.Name));
                        break;
                    }
                }
            }

            foreach (var successor in Successors(block))
            {
                if (ReferenceEquals(successor, exit))
                {
                    exits.Add(state.Clone());
                    continue;
                }

                if (!incoming.TryGetValue(successor, out var oldState))
                {
                    incoming[successor] = state.Clone();
                    pending.Enqueue(successor);
                    continue;
                }

                var joined = FlowState.Join(oldState, state);
                if (!joined.Equals(oldState))
                {
                    incoming[successor] = joined;
                    pending.Enqueue(successor);
                }
            }
        }

        foreach (var state in exits)
        {
            foreach (var localEntry in state.Locals)
            {
                var local = localEntry.Key;
                var lifetime = localEntry.Value;
                if (lifetime.Statuses.HasFlag(LifetimeStatus.MountedUndisposed) &&
                    !state.UsingLocals.Contains(local))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UndisposedLocal,
                        lifetime.MountLocation,
                        local.Name));
                }
            }
        }
    }

    private static IEnumerable<BasicBlock> Successors(BasicBlock block)
    {
        if (block.FallThroughSuccessor?.Destination is { } fallThrough)
            yield return fallThrough;
        if (block.ConditionalSuccessor?.Destination is { } conditional &&
            !ReferenceEquals(conditional, block.FallThroughSuccessor?.Destination))
        {
            yield return conditional;
        }
    }

    private static ILocalReferenceOperation? UnwrapLocalReference(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation as ILocalReferenceOperation;
    }

    private static HashSet<INamedTypeSymbol> FindGeneratedComponents(Compilation compilation)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var generatedCodeAttribute = compilation.GetTypeByMetadataName(
            typeof(GeneratedCodeAttribute).FullName!);
        Visit(compilation.Assembly.GlobalNamespace);
        return result;

        void Visit(INamespaceOrTypeSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                if (member is INamedTypeSymbol type)
                {
                    if (generatedCodeAttribute is not null && type.GetAttributes().Any(attribute =>
                            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, generatedCodeAttribute) &&
                            attribute.ConstructorArguments.Length > 0 &&
                            attribute.ConstructorArguments[0].Value is string toolName &&
                            string.Equals(toolName, "Lucent.Compiler", StringComparison.Ordinal)))
                    {
                        result.Add(type);
                    }

                    Visit(type);
                }
                else if (member is INamespaceSymbol childNamespace)
                {
                    Visit(childNamespace);
                }
            }
        }
    }

    private static bool IsGeneratedComponent(
        ITypeSymbol? type,
        HashSet<INamedTypeSymbol> components) =>
        type is INamedTypeSymbol named && components.Contains(named.OriginalDefinition);

    private enum LifetimeStatus
    {
        NotMounted = 1,
        MountedUndisposed = 2,
        MountedDisposed = 4,
    }

    private sealed class Lifetime(LifetimeStatus statuses, Location mountLocation)
    {
        public LifetimeStatus Statuses { get; } = statuses;
        public Location MountLocation { get; } = mountLocation;
    }

    private sealed class FlowState
    {
        public Dictionary<ILocalSymbol, Lifetime> Locals { get; } =
            new(SymbolEqualityComparer.Default);
        public HashSet<ILocalSymbol> UsingLocals { get; } =
            new(SymbolEqualityComparer.Default);
        public Dictionary<ILocalSymbol, HashSet<ILocalSymbol>> RootOwners { get; } =
            new(SymbolEqualityComparer.Default);

        public FlowState Clone()
        {
            var clone = new FlowState();
            foreach (var entry in Locals)
            {
                var local = entry.Key;
                var lifetime = entry.Value;
                clone.Locals[local] = new Lifetime(lifetime.Statuses, lifetime.MountLocation);
            }
            clone.UsingLocals.UnionWith(UsingLocals);
            foreach (var entry in RootOwners)
            {
                var root = entry.Key;
                var owners = entry.Value;
                clone.RootOwners[root] = new HashSet<ILocalSymbol>(
                    owners, SymbolEqualityComparer.Default);
            }
            return clone;
        }

        public static FlowState Join(FlowState left, FlowState right)
        {
            var joined = left.Clone();
            joined.UsingLocals.UnionWith(right.UsingLocals);
            foreach (var entry in right.Locals)
            {
                var local = entry.Key;
                var lifetime = entry.Value;
                if (joined.Locals.TryGetValue(local, out var existing))
                {
                    joined.Locals[local] = new Lifetime(
                        existing.Statuses | lifetime.Statuses,
                        existing.MountLocation);
                }
                else
                {
                    joined.Locals[local] = new Lifetime(
                        lifetime.Statuses, lifetime.MountLocation);
                }
            }

            foreach (var entry in right.RootOwners)
            {
                var root = entry.Key;
                var owners = entry.Value;
                if (!joined.RootOwners.TryGetValue(root, out var existing))
                    joined.RootOwners[root] = new HashSet<ILocalSymbol>(
                        owners, SymbolEqualityComparer.Default);
                else
                    existing.UnionWith(owners);
            }
            return joined;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not FlowState other ||
                !UsingLocals.SetEquals(other.UsingLocals) ||
                Locals.Count != other.Locals.Count ||
                RootOwners.Count != other.RootOwners.Count)
            {
                return false;
            }

            foreach (var entry in Locals)
            {
                var local = entry.Key;
                var lifetime = entry.Value;
                if (!other.Locals.TryGetValue(local, out var otherLifetime) ||
                    lifetime.Statuses != otherLifetime.Statuses)
                {
                    return false;
                }
            }
            foreach (var entry in RootOwners)
            {
                var root = entry.Key;
                var owners = entry.Value;
                if (!other.RootOwners.TryGetValue(root, out var otherOwners) ||
                    !owners.SetEquals(otherOwners))
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode() => 0;
    }

    private sealed class TransferWalker(
        OperationBlockAnalysisContext context,
        FlowState state,
        HashSet<INamedTypeSymbol> generatedComponents) : OperationWalker
    {
        public override void VisitObjectCreation(IObjectCreationOperation operation)
        {
            base.VisitObjectCreation(operation);
            if (!IsGeneratedComponent(operation.Type, generatedComponents))
                return;

            if (GetAssignedLocal(operation) is not { } local)
                return;

            state.Locals[local] = new Lifetime(
                LifetimeStatus.NotMounted,
                operation.Syntax.GetLocation());
            if (operation.Syntax.AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax>()
                .Any(declaration => declaration.UsingKeyword != default))
            {
                state.UsingLocals.Add(local);
            }
        }

        public override void VisitInvocation(IInvocationOperation operation)
        {
            base.VisitInvocation(operation);
            if (!IsGeneratedComponent(operation.Instance?.Type, generatedComponents))
                return;

            if (operation.TargetMethod.Name == "Dispose" &&
                operation.Instance is ILocalReferenceOperation disposeReference &&
                state.Locals.TryGetValue(disposeReference.Local, out var disposable))
            {
                state.Locals[disposeReference.Local] = new Lifetime(
                    (disposable.Statuses & LifetimeStatus.NotMounted) |
                    (disposable.Statuses & LifetimeStatus.MountedDisposed) |
                    (disposable.Statuses.HasFlag(LifetimeStatus.MountedUndisposed)
                        ? LifetimeStatus.MountedDisposed
                        : 0),
                    disposable.MountLocation);
                return;
            }

            if (operation.TargetMethod.Name is not ("Mount" or "MountRoot"))
                return;

            if (operation.Instance is IObjectCreationOperation)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DroppedTemporary, operation.Syntax.GetLocation()));
                return;
            }

            if (operation.Instance is not ILocalReferenceOperation reference)
                return;

            if (!state.Locals.TryGetValue(reference.Local, out var lifetime))
                return;

            if (lifetime.Statuses.HasFlag(LifetimeStatus.MountedUndisposed) ||
                lifetime.Statuses.HasFlag(LifetimeStatus.MountedDisposed))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RepeatedMount,
                    operation.Syntax.GetLocation(),
                    reference.Local.Name));
            }
            state.Locals[reference.Local] = new Lifetime(
                LifetimeStatus.MountedUndisposed,
                operation.Syntax.GetLocation());
            if (operation.TargetMethod.Name != "MountRoot")
                return;

            if (state.UsingLocals.Contains(reference.Local) &&
                operation.Syntax.AncestorsAndSelf().Any(node =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax))
            {
                ReportRootEscape(operation, reference.Local);
            }

            if (GetAssignedLocal(operation) is { } rootLocal)
            {
                if (!state.RootOwners.TryGetValue(rootLocal, out var owners))
                {
                    owners = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                    state.RootOwners[rootLocal] = owners;
                }
                owners.Add(reference.Local);
            }
        }

        public override void VisitReturn(IReturnOperation operation)
        {
            base.VisitReturn(operation);
            var returnedValue = operation.ReturnedValue;
            while (returnedValue is IConversionOperation conversion)
                returnedValue = conversion.Operand;
            if (returnedValue is not ILocalReferenceOperation rootReference ||
                !state.RootOwners.TryGetValue(rootReference.Local, out var owners))
            {
                return;
            }

            foreach (var owner in owners)
            {
                if (state.UsingLocals.Contains(owner))
                {
                    ReportRootEscape(operation, owner);
                    break;
                }
            }
        }

        private void ReportRootEscape(IOperation operation, ILocalSymbol owner) =>
            context.ReportDiagnostic(Diagnostic.Create(
                EscapedRoot,
                operation.Syntax.GetLocation(),
                owner.Name));

        private ILocalSymbol? GetAssignedLocal(IInvocationOperation operation)
        {
            if (operation.Parent is IVariableInitializerOperation initializer &&
                initializer.Parent is IVariableDeclaratorOperation declarator)
            {
                return declarator.Symbol as ILocalSymbol;
            }

            if (operation.Parent is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation target)
            {
                return target.Local;
            }

            return null;
        }

        private static ILocalSymbol? GetAssignedLocal(IObjectCreationOperation operation)
        {
            if (operation.Parent is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation target)
            {
                return target.Local;
            }

            return null;
        }
    }
}
