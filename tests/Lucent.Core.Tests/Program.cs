using System.Reflection;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lucent.Core;

if (args.Length == 0)
{
    if (ReactiveContracts.Run() != 0 || CompositionContracts.Run() != 0) return 1;
    return PresentationContracts.Run();
}
if (args.Length != 1)
    return 2;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var provider = new TypeNameProvider();
var violations = new List<string>();
foreach (var handle in metadata.TypeReferences)
{
    var referenced = provider.GetTypeFromReference(metadata, handle, 0);
    if (IsRuntimeDiscoveryType(referenced))
        violations.Add($"Forbidden Core runtime discovery type: {referenced}");
}
foreach (var handle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(handle);
    if ((type.Attributes & TypeAttributes.VisibilityMask) is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
        continue;
    foreach (var exposed in ExposedTypes(metadata, type, provider))
    {
        if (IsForbidden(exposed))
            violations.Add($"Forbidden Core public API type: {exposed}");
    }
}
foreach (var violation in violations.Distinct(StringComparer.Ordinal)) Console.Error.WriteLine(violation);
return violations.Count == 0 ? 0 : 1;

static IEnumerable<string> ExposedTypes(MetadataReader metadata, TypeDefinition type, TypeNameProvider provider)
{
    yield return provider.Name(metadata, type.Namespace, type.Name);
    if (!type.BaseType.IsNil) yield return provider.FromHandle(metadata, type.BaseType);
    foreach (var implementation in type.GetInterfaceImplementations())
        yield return provider.FromHandle(metadata, metadata.GetInterfaceImplementation(implementation).Interface);
    foreach (var handle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.MemberAccessMask) is not (MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem)) continue;
        var signature = method.DecodeSignature(provider, genericContext: null);
        yield return signature.ReturnType;
        foreach (var parameter in signature.ParameterTypes) yield return parameter;
    }
    foreach (var handle in type.GetProperties())
    {
        var property = metadata.GetPropertyDefinition(handle);
        var accessors = property.GetAccessors();
        if (!IsExposed(accessors.Getter) && !IsExposed(accessors.Setter)) continue;
        var signature = property.DecodeSignature(provider, genericContext: null);
        yield return signature.ReturnType;
        foreach (var parameter in signature.ParameterTypes) yield return parameter;
    }
    foreach (var handle in type.GetFields())
    {
        var field = metadata.GetFieldDefinition(handle);
        if ((field.Attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public)
            yield return field.DecodeSignature(provider, genericContext: null);
    }
    foreach (var handle in type.GetEvents())
    {
        var @event = metadata.GetEventDefinition(handle);
        var accessors = @event.GetAccessors();
        if (IsExposed(accessors.Adder) || IsExposed(accessors.Remover) || IsExposed(accessors.Raiser))
            yield return provider.FromHandle(metadata, @event.Type);
    }

    bool IsExposed(MethodDefinitionHandle handle)
    {
        if (handle.IsNil) return false;
        var access = metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
        return access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }
}

static bool IsForbidden(string type) => type.Split('|').Any(part =>
    new[] { "SDL3", "SkiaSharp", "Windows.Win32", "Microsoft.Windows.CsWin32", "Lucent.Platform.Windows" }
        .Any(prefix => part.StartsWith(prefix, StringComparison.Ordinal)));

static bool IsRuntimeDiscoveryType(string type) => type is "System.Reflection.Assembly" or "System.Reflection.MemberInfo" or "System.Reflection.MethodInfo" or "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo" or "System.ComponentModel.TypeDescriptor" or "System.ComponentModel.PropertyDescriptor"
    || new[] { "System.Dynamic", "System.Linq.Expressions", "System.Runtime.Loader", "System.Text.Json", "System.Xml" }.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal));

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string Name(MetadataReader reader, StringHandle @namespace, StringHandle name) => reader.GetString(@namespace) is { Length: > 0 } ns ? ns + "." + reader.GetString(name) : reader.GetString(name);
    public string FromHandle(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
        _ => string.Empty
    };
    public string GetArrayType(string elementType, ArrayShape shape) => elementType;
    public string GetByReferenceType(string elementType) => elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "|" + string.Join('|', typeArguments);
    public string GetGenericMethodParameter(object? genericContext, int index) => string.Empty;
    public string GetGenericTypeParameter(object? genericContext, int index) => string.Empty;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType;
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => string.Empty;
    public string GetSZArrayType(string elementType) => elementType;
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) { var type = reader.GetTypeDefinition(handle); return Name(reader, type.Namespace, type.Name); }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind is HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name)
            : Name(reader, type.Namespace, type.Name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

internal static class ReactiveContracts
{
    public static int Run()
    {
        try
        {
            BranchesBatchesAndReentrancy();
            FailureRecoveryAndOwnership();
            AsyncOwnershipAndThreading();
            LifetimeRelease();
            var first = EquivalentDump();
            Assert(first == EquivalentDump(), "Reactive dumps differ for equivalent live graphs.");
            Assert(!first.Contains("value=", StringComparison.OrdinalIgnoreCase) && !first.Contains("secret", StringComparison.OrdinalIgnoreCase), "Dump exposed values or errors.");
            Console.WriteLine("Lucent.Core reactive contracts: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Lucent.Core reactive contracts: FAIL: " + exception.Message);
            return 1;
        }
    }

    private static void BranchesBatchesAndReentrancy()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("branches");
        var selectLeft = scope.Signal(true, "select-left");
        var left = scope.Signal(1, "left");
        var right = scope.Signal(10, "right");
        var unrelated = scope.Signal(0, "unrelated");
        var selected = scope.Derived(() => selectLeft.Value ? left.Value : right.Value, "selected");
        var runs = 0;
        _ = scope.Effect(() => { _ = selected.Value; runs++; }, "selected-effect");
        graph.Drain();
        unrelated.Value++;
        right.Value++;
        graph.Drain();
        Assert(runs == 1, "Unrelated write scheduled work.");
        selectLeft.Value = false;
        graph.Drain();
        left.Value++;
        graph.Drain();
        Assert(runs == 2, "Branch replacement retained old dependency.");
        right.Value++;
        graph.Drain();
        Assert(runs == 3, "Branch replacement lost active dependency.");

        var order = new List<string>();
        var first = scope.Signal(0, "first");
        var second = scope.Signal(0, "second");
        _ = scope.Effect(() => order.Add("first:" + first.Value), "first-effect");
        _ = scope.Effect(() => order.Add("second:" + second.Value), "second-effect");
        graph.Drain(); order.Clear();
        graph.Batch(() => { second.Value = 1; graph.Batch(() => { first.Value = 1; graph.Drain(); first.Value = 2; }); second.Value = 2; });
        Assert(order.SequenceEqual(["second:2", "first:2"]), "Nested batch did not flush once deterministically.");

        var loop = scope.Signal(0, "loop");
        var loopRuns = 0;
        _ = scope.Effect(() => { var value = loop.Value; loopRuns++; if (value < 2) loop.Value = value + 1; }, "convergent");
        graph.Drain();
        Assert(loopRuns == 3 && loop.Value == 2, "First-run reentrant write was not rescheduled.");
        loop.Value = 0;
        graph.Drain();
        Assert(loopRuns == 6 && loop.Value == 2, "Later reentrant write was not rescheduled.");

        var batch = scope.Signal(0, "batch");
        _ = scope.Effect(() => { if (batch.Value == 1) throw new InvalidOperationException("drain"); }, "batch-effect");
        graph.Drain();
        try
        {
            graph.Batch(() => { batch.Value = 1; throw new ArgumentException("body"); });
            throw new InvalidOperationException("Expected batch failure.");
        }
        catch (AggregateException exception)
        {
            Assert(exception.InnerExceptions.Any(error => error is ArgumentException) && exception.InnerExceptions.Any(error => error is AggregateException), "Batch masked a body or drain failure.");
        }
        batch.Value = 2;
        graph.Drain();
    }

    private static void FailureRecoveryAndOwnership()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("failures");
        var guard = scope.Signal(true, "self-guard");
        Derived<int>? self = null;
        self = scope.Derived(() => guard.Value ? self!.Value : 7, "self");
        var recovered = 0;
        _ = scope.Effect(() => { _ = self.Value; recovered++; }, "self-consumer");
        ExpectCycle(graph.Drain, "self", "self");
        guard.Value = false;
        graph.Drain();
        Assert(recovered == 1, "Self-cycle consumer did not recover without a manual pull.");

        var mutualGuard = scope.Signal(true, "mutual-guard");
        Derived<int>? first = null;
        Derived<int>? second = null;
        first = scope.Derived(() => mutualGuard.Value ? second!.Value : 3, "first");
        second = scope.Derived(() => first!.Value, "second");
        var mutualRecovered = 0;
        _ = scope.Effect(() => { _ = first.Value; mutualRecovered++; }, "mutual-consumer");
        ExpectCycle(graph.Drain, "first", "second", "first");
        mutualGuard.Value = false;
        graph.Drain();
        Assert(mutualRecovered == 1, "Mutual-cycle consumer did not recover without a manual pull.");

        var prior = scope.Signal(1, "prior");
        var observed = scope.Signal(1, "observed");
        var fail = scope.Signal(false, "fail");
        var flaky = scope.Derived(() => { if (fail.Value) { _ = observed.Value; throw new InvalidOperationException("expected"); } return prior.Value; }, "flaky");
        var flakyRuns = 0;
        _ = scope.Effect(() => { _ = flaky.Value; flakyRuns++; }, "flaky-consumer");
        graph.Drain();
        fail.Value = true; ExpectAggregate(graph.Drain);
        prior.Value++; ExpectAggregate(graph.Drain);
        observed.Value++; ExpectAggregate(graph.Drain);
        fail.Value = false; graph.Drain();
        var afterSuccess = flakyRuns;
        observed.Value++; graph.Drain();
        Assert(flakyRuns == afterSuccess, "Successful callback did not replace failed dependencies exactly.");

        var effectPrior = scope.Signal(1, "effect-prior");
        var effectObserved = scope.Signal(1, "effect-observed");
        var effectFail = scope.Signal(false, "effect-fail");
        var effectRuns = 0;
        _ = scope.Effect(() =>
        {
            effectRuns++;
            _ = effectPrior.Value;
            if (effectFail.Value) { _ = effectObserved.Value; throw new InvalidOperationException("expected effect failure"); }
        }, "flaky-effect");
        graph.Drain();
        effectFail.Value = true; ExpectAggregate(graph.Drain);
        effectPrior.Value++; ExpectAggregate(graph.Drain);
        effectObserved.Value++; ExpectAggregate(graph.Drain);
        effectFail.Value = false; graph.Drain();
        var effectAfterSuccess = effectRuns;
        effectObserved.Value++; graph.Drain();
        Assert(effectRuns == effectAfterSuccess, "Successful effect did not replace failed dependencies exactly.");

        ReactiveEffect? victim = null;
        var dispose = scope.Signal(0, "dispose");
        var victimSource = scope.Signal(0, "victim-source");
        var victimRuns = 0;
        _ = scope.Effect(() => { if (dispose.Value != 0) victim!.Dispose(); }, "disposer");
        victim = scope.Effect(() => { _ = victimSource.Value; victimRuns++; }, "victim");
        graph.Drain();
        graph.Batch(() => { dispose.Value = 1; victimSource.Value = 1; });
        Assert(victimRuns == 1 && !graph.Dump().Contains("victim\"", StringComparison.Ordinal), "Disposed effect remained queued or dumped.");

        var child = scope.CreateChild("manual-child");
        var manual = child.Signal(1, "manual-node");
        manual.Dispose(); child.Dispose();
        Assert(!graph.Dump().Contains("manual-", StringComparison.Ordinal), "Manual child/node disposal remained active.");
        var before = graph.Dump();
        ExpectArgument(() => graph.Derived<int>(null!, "ghost-derived"));
        ExpectArgument(() => scope.Effect(null!, "ghost-effect"));
        ExpectArgument(() => graph.Async<int>(null!, "ghost-async"));
        Assert(graph.Dump() == before, "Invalid callback registered a ghost node.");
    }
    private static void AsyncOwnershipAndThreading()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("async");
        var source = scope.Signal(1, "source");
        var work = new List<TaskCompletionSource<int>>();
        var cancelled = 0;
        var value = scope.Async(token =>
        {
            _ = source.Value;
            token.Register(() => cancelled++);
            var next = new TaskCompletionSource<int>();
            work.Add(next);
            return next.Task;
        }, 10, "latest");
        Assert(value.Value == 10 && value.IsPending, "Async stale/pending state failed.");
        source.Value = 2;
        Assert(value.IsPending && cancelled == 1, "Async dependency cancellation failed.");
        CompleteOnWorker(work[0], 1); graph.Drain();
        Assert(value.Value == 10 && value.IsPending, "Stale cancellation-ignoring result committed.");
        CompleteOnWorker(work[1], 20); graph.Drain();
        Assert(value.Value == 20 && !value.IsPending, "Latest result did not commit.");
        source.Value = 3; _ = value.IsPending;
        source.Value = 4; _ = value.IsPending;
        CompleteOnWorker(work[2], 30); graph.Drain();
        Assert(value.Value == 20 && value.IsPending, "Late cancelled generation committed.");
        CompleteOnWorker(work[3], 40); graph.Drain();
        Assert(value.Value == 40 && !value.IsPending, "Replacement generation failed.");

        var sync = scope.Async<int>(_ => throw new InvalidOperationException("secret"), "sync");
        _ = sync.Value;
        Assert(sync.Error is InvalidOperationException && !sync.IsPending, "Sync loader failure remained pending.");
        var nullTask = scope.Async<int>(_ => null!, "null-task");
        _ = nullTask.Value;
        Assert(nullTask.Error is InvalidOperationException && !nullTask.IsPending, "Null task remained pending.");
        var canceledWork = new TaskCompletionSource<int>();
        var canceled = scope.Async(_ => canceledWork.Task, 6, "cancelled");
        _ = canceled.Value;
        Task.Run(canceledWork.SetCanceled).GetAwaiter().GetResult(); graph.Drain();
        Assert(canceled.IsCancelled && !canceled.IsPending && canceled.Value == 6, "Current cancellation lost stale state.");

        var wrongGraph = new ReactiveGraph();
        var wrongWork = new TaskCompletionSource<int>();
        var wrong = wrongGraph.Async(_ => wrongWork.Task, 0, "wrong-thread");
        _ = wrong.Value;
        var rejected = Task.Run(() =>
        {
            wrongWork.SetResult(9);
            try { wrongGraph.Drain(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        Assert(rejected && wrong.Value == 0 && wrong.IsPending, "Wrong-thread commit was accepted.");
        wrongGraph.Drain();
        Assert(wrong.Value == 9 && !wrong.IsPending, "UI-thread recovery after rejected commit failed.");

        var throwing = scope.CreateChild("throwing-scope");
        var never = new TaskCompletionSource<int>();
        _ = throwing.Async(token => { token.Register(() => throw new InvalidOperationException("cancel")); return never.Task; }, "throwing-load").Value;
        var cleanupRuns = 0;
        throwing.OnDispose(() => { cleanupRuns++; throw new InvalidOperationException("cleanup"); });
        var cleanupErrors = CaptureAggregate(throwing.Dispose);
        Assert(Flatten(cleanupErrors).Any(error => error.Message == "cancel") && Flatten(cleanupErrors).Any(error => error.Message == "cleanup"), "Throwing cancellation/cleanup did not preserve both failures.");
        Assert(cleanupRuns == 1 && !graph.Dump().Contains("throwing-", StringComparison.Ordinal), "Throwing cancellation/cleanup broke active graph cleanup.");

        var fanoutSource = scope.Signal(0, "fanout-source");
        var fanoutWork = new TaskCompletionSource<int>();
        var fanoutAsync = scope.Async(token => { _ = fanoutSource.Value; token.Register(() => throw new InvalidOperationException("fanout-cancel")); return fanoutWork.Task; }, "fanout-async");
        _ = fanoutAsync.Value;
        var fanoutRuns = 0;
        _ = scope.Effect(() => { _ = fanoutSource.Value; fanoutRuns++; }, "fanout-sibling");
        graph.Drain();
        var fanoutErrors = CaptureAggregate(() => fanoutSource.Value = 1);
        Assert(Flatten(fanoutErrors).Any(error => error.Message == "fanout-cancel") && fanoutRuns == 1, "Throwing cancellation did not report while preserving sibling scheduling.");
        graph.Drain();
        Assert(fanoutRuns == 2, "Throwing cancellation aborted invalidation fan-out.");

        var reentrantSource = scope.Signal(0, "reentrant-async-source");
        var reentrant = scope.Async(token =>
        {
            var current = reentrantSource.Value;
            token.Register(() => throw new InvalidOperationException("reentrant-cancel"));
            if (current == 0) reentrantSource.Value = 1;
            return Task.FromResult(9);
        }, 0, "reentrant-async");
        var reentrantErrors = CaptureAggregate(() => _ = reentrant.Value);
        Assert(Flatten(reentrantErrors).Any(error => error.Message == "reentrant-cancel") && reentrantSource.Value == 1, "Reentrant async cancellation failure was swallowed.");
        Assert(reentrant.Value == 0 && reentrant.IsPending, "Reentrant async generation did not restart deterministically.");
        graph.Drain();
        Assert(reentrant.Value == 9 && !reentrant.IsPending, "Reentrant async replacement did not commit.");

        var laterSource = scope.Signal(0, "later-reentrant-source");
        var mutateLater = false;
        var later = scope.Async(token =>
        {
            var current = laterSource.Value;
            token.Register(() => throw new InvalidOperationException("later-reentrant-cancel"));
            if (mutateLater) laterSource.Value = current + 1;
            return Task.FromResult(current);
        }, -1, "later-reentrant");
        _ = later.Value;
        graph.Drain();
        Assert(later.Value == 0 && !later.IsPending, "Initial later-reentrant generation did not complete.");
        mutateLater = true;
        laterSource.Value = 1;
        var laterErrors = CaptureAggregate(() => _ = later.Value);
        Assert(Flatten(laterErrors).Any(error => error.Message == "later-reentrant-cancel") && laterSource.Value == 2, "Replacement-generation cancellation failure was swallowed.");
        mutateLater = false;
        Assert(later.Value == 0 && later.IsPending, "Invalidated replacement generation did not restart.");
        graph.Drain();
        Assert(later.Value == 2 && !later.IsPending, "Later replacement generation did not commit.");

        var faultedWork = new TaskCompletionSource<int>();
        var faulted = scope.Async(_ => faultedWork.Task, 4, "faulted-task");
        _ = faulted.Value;
        Task.Run(() => faultedWork.SetException(new InvalidOperationException("async-fault"))).GetAwaiter().GetResult();
        graph.Drain();
        Assert(faulted.Error?.Message == "async-fault" && faulted.Value == 4 && !faulted.IsPending, "Faulted async task did not retain stale state and expose error.");
        Assert(Task.Run(() => { try { source.Value = 99; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Wrong-thread mutation was accepted.");
    }

    private static void LifetimeRelease()
    {
        var disposed = DisposedScopePayload();
        var queued = QueuedEffectPayload();
        var posted = PostedPayload();
        var never = NeverLoadPayload();
        ForceGc();
        Assert(!disposed.Payload.IsAlive && !queued.Payload.IsAlive && !posted.Payload.IsAlive && !never.Payload.IsAlive, "Disposed graph ownership retained a payload.");
        GC.KeepAlive(disposed.Root);
        GC.KeepAlive(queued.Root);
        GC.KeepAlive(posted.Root);
        GC.KeepAlive(never.Root);
        GC.KeepAlive(never.Producer);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static LifetimeProbe DisposedScopePayload()
    {
        var graph = new ReactiveGraph();
        var scope = graph.CreateScope("released-scope");
        var payload = new Payload();
        var weak = new WeakReference(payload);
        _ = scope.Derived(() => payload, "released-node");
        scope.Dispose();
        Assert(!graph.Dump().Contains("released-", StringComparison.Ordinal), "Disposed scope/node remained in dump.");
        return new LifetimeProbe(weak, graph);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static LifetimeProbe QueuedEffectPayload()
    {
        var graph = new ReactiveGraph();
        var payload = new Payload();
        var weak = new WeakReference(payload);
        var effect = graph.Effect(() => GC.KeepAlive(payload), "queued-release");
        effect.Dispose();
        return new LifetimeProbe(weak, graph);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static LifetimeProbe PostedPayload()
    {
        var graph = new ReactiveGraph();
        var source = new TaskCompletionSource<Payload>();
        var async = graph.Async(_ => source.Task, "posted-release");
        _ = async.Value;
        var payload = new Payload();
        var weak = new WeakReference(payload);
        Task.Run(() => source.SetResult(payload)).GetAwaiter().GetResult();
        async.Dispose();
        Assert(!graph.Dump().Contains("posted-release", StringComparison.Ordinal), "Disposed posted async remained active.");
        return new LifetimeProbe(weak, graph);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static LifetimeProbe NeverLoadPayload()
    {
        var graph = new ReactiveGraph();
        var source = new TaskCompletionSource<int>();
        var payload = new Payload();
        var weak = new WeakReference(payload);
        var async = graph.Async(_ => { GC.KeepAlive(payload); return source.Task; }, "never-release");
        _ = async.Value;
        async.Dispose();
        return new LifetimeProbe(weak, graph, source);
    }

    private static void ForceGc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static string EquivalentDump()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("dump");
        var source = scope.Signal(1, "source");
        var derived = scope.Derived(() => source.Value + 1, "derived");
        _ = scope.Effect(() => _ = derived.Value, "consumer");
        graph.Drain();
        return graph.Dump();
    }
    private static void CompleteOnWorker(TaskCompletionSource<int> completion, int value) => Task.Run(() => completion.SetResult(value)).GetAwaiter().GetResult();
    private static void ExpectAggregate(Action action) { try { action(); throw new InvalidOperationException("Expected aggregate failure."); } catch (AggregateException) { } }
    private static AggregateException CaptureAggregate(Action action) { try { action(); throw new InvalidOperationException("Expected aggregate failure."); } catch (AggregateException exception) { return exception; } }
    private static IEnumerable<Exception> Flatten(AggregateException exception) => exception.Flatten().InnerExceptions;
    private static void ExpectCycle(Action action, params string[] path)
    {
        var errors = Flatten(CaptureAggregate(action));
        var cycle = errors.OfType<ReactiveCycleException>().SingleOrDefault() ?? throw new InvalidOperationException("Expected a reactive cycle.");
        Assert(cycle.Message == "Reactive cycle: " + string.Join(" -> ", path), "Reactive cycle path was not exact.");
    }
    private static void ExpectArgument(Action action) { try { action(); throw new InvalidOperationException("Expected argument failure."); } catch (ArgumentNullException) { } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed record LifetimeProbe(WeakReference Payload, object Root, object? Producer = null);
    private sealed class Payload;
}

internal static class CompositionContracts
{
    public static int Run()
    {
        try
        {
            ConditionalIdentityAndCleanup();
            KeyedIdentityRollbackAndCleanup();
            DepartedFacetsAndLateAsync();
            FailureAndDisposalSafety();
            FactoryGuardsAndJointFailures();
            PublicFactoryStructuralGuards();
            ManualScopeDisposalRetiresEntries();
            KeyedFactoryTransactionsAndReentrancy();
            var first = EquivalentDump();
            Assert(first == EquivalentDump(), "Composition dumps differ for equivalent active trees.");
            Assert(!first.Contains("secret", StringComparison.OrdinalIgnoreCase) && !first.Contains("value=", StringComparison.OrdinalIgnoreCase), "Composition dump exposed application values.");
            ReleasedPayload();
            ReleasedOwnershipIdentities();
            Console.WriteLine("Lucent.Core composition contracts: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Lucent.Core composition contracts: FAIL: " + exception.Message);
            return 1;
        }
    }

    private static void ConditionalIdentityAndCleanup()
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(false, "conditional-active");
        using var composition = new Composition(graph, "conditional-root");
        var cleanup = 0;
        var region = composition.When(composition.Root, "conditional-region", () => active.Value, context =>
        {
            var child = context.Element("conditional-child");
            _ = context.Child(child, "conditional-grandchild");
            child.Scope.OnDispose(() => cleanup++);
            return child;
        });
        graph.Drain();
        Assert(region.Active is null, "Inactive conditional created content.");
        active.Value = true; graph.Drain();
        var child = region.Active!;
        var scope = child.Scope;
        region.Update(true);
        Assert(ReferenceEquals(child, region.Active) && ReferenceEquals(scope, region.Active!.Scope) && child.Children.Count == 1, "Unchanged conditional branch replaced identity or provisional structure.");
        active.Value = false; graph.Drain();
        Assert(region.Active is null && child.IsDisposed && cleanup == 1, "Conditional departure did not dispose exactly once.");
        active.Value = true; graph.Drain();
        Assert(region.Active is not null && !ReferenceEquals(child, region.Active), "Conditional remount reused departed identity.");
    }

    private static void KeyedIdentityRollbackAndCleanup()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { "a", "b" }, "keyed-rows");
        using var composition = new Composition(graph, "keyed-root");
        var cleanup = new Dictionary<string, int>();
        var region = composition.ForEach(composition.Root, "keyed-region", () => rows.Value, value => value, (value, context) =>
        {
            var row = context.Element("keyed-row");
            row.Scope.OnDispose(() => cleanup[value] = cleanup.GetValueOrDefault(value) + 1);
            return row;
        });
        graph.Drain();
        var a = region.Items[0];
        var b = region.Items[1];
        rows.Value = ["b", "a", "c"]; graph.Drain();
        var c = region.Items[2];
        Assert(region.Items.Select(item => item.Id).SequenceEqual([b.Id, a.Id, c.Id]) && ReferenceEquals(region.Items[0].Scope, b.Scope), "Same-key reorder replaced an element or scope.");
        rows.Value = ["c", "d", "a"]; graph.Drain();
        Assert(b.IsDisposed && cleanup.GetValueOrDefault("b") == 1 && ReferenceEquals(region.Items[0], c) && ReferenceEquals(region.Items[2], a), "Keyed removal/addition was not exact.");

        var dump = composition.Dump();
        var identities = region.Items.ToArray();
        rows.Value = ["c", "c", "a"];
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == dump && region.Items.SequenceEqual(identities), "Duplicate keys mutated the live tree.");

        rows.Value = ["a"];
        graph.Drain();
        Assert(c.IsDisposed && cleanup.GetValueOrDefault("c") == 1 && cleanup.GetValueOrDefault("d") == 1, "Departed keyed entries were not cleaned once.");
    }

    private static void DepartedFacetsAndLateAsync()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1, 2 }, "facet-rows");
        var tick = graph.Signal(0, "facet-tick");
        var work = new Dictionary<int, TaskCompletionSource<int>> { [1] = new(), [2] = new() };
        using var composition = new Composition(graph, "facet-root");
        var cleanup = 0;
        var cancelled = 0;
        var region = composition.ForEach(composition.Root, "facet-region", () => rows.Value, value => value, (value, context) =>
        {
            var row = context.Element("facet-row");
            _ = row.Scope.Effect(() => _ = tick.Value, "facet-subscription");
            var pending = row.Scope.Async(token =>
            {
                token.Register(() => cancelled++);
                return work[value].Task;
            }, "facet-async");
            _ = pending.IsPending;
            row.Scope.OnDispose(() => cleanup++); // future focus
            row.Scope.OnDispose(() => cleanup++); // future capture
            row.Scope.OnDispose(() => cleanup++); // future semantics/scene
            return row;
        });
        graph.Drain();
        var departed = region.Items[0];
        var retained = region.Items[1];
        rows.Value = [2];
        graph.Drain();
        Task.Run(() => work[1].SetResult(42)).GetAwaiter().GetResult();
        graph.Drain();
        Assert(departed.IsDisposed && ReferenceEquals(region.Items.Single(), retained) && cancelled == 1 && cleanup == 3 && composition.Dump().Split('\n').Count(line => line.Contains("facet-row", StringComparison.Ordinal)) == 1, "Departed facets were retained or async work committed.");
    }

    private static void FailureAndDisposalSafety()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { "kept" }, "failure-rows");
        using var composition = new Composition(graph, "failure-root");
        var provisionalCleanup = 0;
        var throwingCleanup = 0;
        var region = composition.ForEach(composition.Root, "failure-region", () => rows.Value, value => value, (value, context) =>
        {
            var row = context.Element("failure-row");
            if (value == "broken")
            {
                row.Scope.OnDispose(() => provisionalCleanup++);
                throw new InvalidOperationException("factory");
            }
            if (value == "throwing")
            {
                row.Scope.OnDispose(() => throwingCleanup++);
                row.Scope.OnDispose(() => throw new InvalidOperationException("cleanup"));
            }
            return row;
        });
        graph.Drain();
        var kept = region.Items.Single();
        rows.Value = ["kept", "broken"];
        ExpectAggregate(graph.Drain);
        Assert(ReferenceEquals(region.Items.Single(), kept) && provisionalCleanup == 1, "Factory failure changed live keyed state or leaked provisional ownership.");

        rows.Value = ["throwing"];
        graph.Drain();
        Assert(region.Items.Single().IsDisposed == false && kept.IsDisposed && throwingCleanup == 0, "Keyed cleanup did not commit before reporting cleanup failure.");
        rows.Value = [];
        ExpectAggregate(graph.Drain);
        Assert(throwingCleanup == 1 && region.Items.Count == 0, "Throwing cleanup stopped later cleanup or left a live entry.");

        var disposalGraph = new ReactiveGraph();
        var trigger = disposalGraph.Signal(false, "dispose-during-factory");
        var disposal = new Composition(disposalGraph, "dispose-root");
        _ = disposal.When(disposal.Root, "dispose-region", () => trigger.Value, context =>
        {
            var child = context.Element("dispose-child");
            disposal.Dispose();
            return child;
        });
        disposalGraph.Drain();
        trigger.Value = true;
        ExpectAggregate(disposalGraph.Drain);
        Assert(disposal.IsDisposed, "Disposal during a factory left the composition active.");
    }

    private static void FactoryGuardsAndJointFailures()
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(false, "invalid-parent-active");
        using var composition = new Composition(graph, "invalid-parent-root");
        var unrelated = composition.Child(composition.Root, "unrelated");
        var region = composition.When(composition.Root, "invalid-parent-region", () => active.Value, context =>
        {
            var root = context.Element("provisional");
            _ = context.Child(composition.Root, "ghost");
            return root;
        });
        graph.Drain();
        var before = composition.Dump();
        active.Value = true;
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && unrelated.Children.Count == 0 && region.Active is null, "A factory attached an unrelated live parent before validation.");
        Assert(composition.Root.Children is not List<Element>, "Children exposed the mutable backing list.");
        var writable = (IList<Element>)composition.Root.Children;
        ExpectNotSupported(writable.Clear);
        Assert(composition.Dump() == before, "A mutable Children view bypassed ownership.");

        var disposedGraph = new ReactiveGraph();
        var disposedActive = disposedGraph.Signal(false, "disposed-context-active");
        using var disposedComposition = new Composition(disposedGraph, "disposed-context-root");
        var contextUsedAfterDispose = false;
        var disposedRegion = disposedComposition.When(disposedComposition.Root, "disposed-context-region", () => disposedActive.Value, context =>
        {
            var root = context.Element("disposed-context-child");
            context.Dispose();
            ExpectDisposed(() => context.Element("late-root"));
            ExpectDisposed(() => context.Child(root, "late-child"));
            contextUsedAfterDispose = true;
            return root;
        });
        disposedGraph.Drain();
        disposedActive.Value = true;
        ExpectAggregate(disposedGraph.Drain);
        Assert(contextUsedAfterDispose && disposedRegion.Active is null && disposedRegion.Region.Children.Count == 0, "Disposed factory context committed content.");

        var rootGraph = new ReactiveGraph();
        var rootActive = rootGraph.Signal(false, "disposed-root-active");
        using var rootComposition = new Composition(rootGraph, "disposed-root-composition");
        var rootRegion = rootComposition.When(rootComposition.Root, "disposed-root-region", () => rootActive.Value, context =>
        {
            var root = context.Element("disposed-root-child");
            root.Scope.Dispose();
            return root;
        });
        rootGraph.Drain();
        rootActive.Value = true;
        ExpectAggregate(rootGraph.Drain);
        Assert(rootRegion.Active is null && rootRegion.Region.Children.Count == 0, "Disposed conditional root committed content.");

        var keyedGraph = new ReactiveGraph();
        var rows = keyedGraph.Signal(new[] { 1 }, "disposed-keyed-rows");
        using var keyedComposition = new Composition(keyedGraph, "disposed-keyed-root");
        var keyed = keyedComposition.ForEach(keyedComposition.Root, "disposed-keyed-region", () => rows.Value, value => value, (_, context) =>
        {
            var root = context.Element("disposed-keyed-child");
            root.Scope.Dispose();
            return root;
        });
        ExpectAggregate(keyedGraph.Drain);
        Assert(keyed.Items.Count == 0, "Disposed keyed root committed content.");

        var descendantGraph = new ReactiveGraph();
        var descendantActive = descendantGraph.Signal(false, "disposed-descendant-active");
        using var descendantComposition = new Composition(descendantGraph, "disposed-descendant-root");
        var descendantRegion = descendantComposition.When(descendantComposition.Root, "disposed-descendant-region", () => descendantActive.Value, context =>
        {
            var root = context.Element("disposed-descendant-child");
            context.Child(root, "disposed-descendant-leaf").Scope.Dispose();
            return root;
        });
        descendantGraph.Drain();
        descendantActive.Value = true;
        ExpectAggregate(descendantGraph.Drain);
        Assert(descendantRegion.Active is null && descendantRegion.Region.Children.Count == 0, "Disposed factory descendants committed content.");

        var conditionalGraph = new ReactiveGraph();
        var conditionalActive = conditionalGraph.Signal(false, "joint-conditional-active");
        using var conditionalComposition = new Composition(conditionalGraph, "joint-conditional-root");
        _ = conditionalComposition.When(conditionalComposition.Root, "joint-conditional-region", () => conditionalActive.Value, context =>
        {
            var root = context.Element("joint-conditional-child");
            root.Scope.OnDispose(() => throw new InvalidOperationException("conditional-cleanup"));
            throw new InvalidOperationException("conditional-factory");
        });
        conditionalGraph.Drain();
        conditionalActive.Value = true;
        ExpectErrors(CaptureAggregate(conditionalGraph.Drain), "conditional-factory", "conditional-cleanup");

        var failureGraph = new ReactiveGraph();
        var failureRows = failureGraph.Signal(Array.Empty<int>(), "joint-keyed-rows");
        using var failureComposition = new Composition(failureGraph, "joint-keyed-root");
        var failureRegion = failureComposition.ForEach(failureComposition.Root, "joint-keyed-region", () => failureRows.Value, value => value, (value, context) =>
        {
            var root = context.Element("joint-keyed-child");
            if (value == 1)
            {
                root.Scope.OnDispose(() => throw new InvalidOperationException("keyed-provisional-cleanup"));
                return root;
            }
            root.Scope.OnDispose(() => throw new InvalidOperationException("keyed-current-cleanup"));
            throw new InvalidOperationException("keyed-factory");
        });
        failureGraph.Drain();
        failureRows.Value = [1, 2];
        ExpectErrors(CaptureAggregate(failureGraph.Drain), "keyed-factory", "keyed-provisional-cleanup", "keyed-current-cleanup");
        Assert(failureRegion.Items.Count == 0, "Failed keyed factory left provisional content live.");
    }

    private static void PublicFactoryStructuralGuards()
    {
        AssertPublicFactoryGuard(false, "child", composition => composition.Child(composition.Root, "escaped-child"));
        AssertPublicFactoryGuard(false, "when", composition => composition.When(composition.Root, "escaped-when", () => false, context => context.Element("escaped-when-child")));
        AssertPublicFactoryGuard(false, "foreach", composition => composition.ForEach(composition.Root, "escaped-foreach", Array.Empty<int>, value => value, (_, context) => context.Element("escaped-foreach-child")));
        AssertPublicFactoryGuard(true, "child", composition => composition.Child(composition.Root, "escaped-child"));
        AssertPublicFactoryGuard(true, "when", composition => composition.When(composition.Root, "escaped-when", () => false, context => context.Element("escaped-when-child")));
        AssertPublicFactoryGuard(true, "foreach", composition => composition.ForEach(composition.Root, "escaped-foreach", Array.Empty<int>, value => value, (_, context) => context.Element("escaped-foreach-child")));
    }

    private static void AssertPublicFactoryGuard(bool keyed, string api, Action<Composition> bypass)
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(false, "factory-guard-active-" + api + keyed);
        var rows = graph.Signal(Array.Empty<int>(), "factory-guard-rows-" + api + keyed);
        using var composition = new Composition(graph, "factory-guard-root-" + api + keyed);
        var cleanup = 0;
        Element region;
        Func<CompositionContext, Element> content = context =>
        {
            var root = context.Element("factory-guard-child-" + api + keyed);
            root.Scope.OnDispose(() => cleanup++);
            bypass(composition);
            return root;
        };
        ConditionalRegion? conditional = null;
        KeyedRegion<int, int>? keyedRegion = null;
        if (keyed)
            keyedRegion = composition.ForEach(composition.Root, "factory-guard-keyed-" + api, () => rows.Value, value => value, (_, context) => content(context));
        else
            conditional = composition.When(composition.Root, "factory-guard-conditional-" + api, () => active.Value, content);
        graph.Drain();
        region = keyed ? keyedRegion!.Region : conditional!.Region;
        var before = composition.Dump();
        if (keyed) rows.Value = [1]; else active.Value = true;
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && region.Children.Count == 0 && cleanup == 1 &&
            (keyed ? keyedRegion!.Items.Count == 0 : conditional!.Active is null), $"Captured public {api} bypassed { (keyed ? "keyed" : "conditional") } factory rollback.");
    }

    private static void ManualScopeDisposalRetiresEntries()
    {
        var graph = new ReactiveGraph();
        var active = graph.Signal(true, "manual-scope-conditional-active");
        using var composition = new Composition(graph, "manual-scope-conditional-root");
        var cleanup = 0;
        var region = composition.When(composition.Root, "manual-scope-conditional-region", () => active.Value, context =>
        {
            var root = context.Element("manual-scope-conditional-child");
            root.Scope.OnDispose(() => cleanup++);
            return root;
        });
        graph.Drain();
        var retired = region.Active!;
        retired.Scope.Dispose();
        Assert(retired.IsDisposed && region.Active is null && region.Region.Children.Count == 0 && cleanup == 1 &&
            !composition.Dump().Contains("manual-scope-conditional-child", StringComparison.Ordinal), "Disposed conditional scope left active tree or cache state.");
        region.Update(true);
        Assert(region.Active is not null && !ReferenceEquals(retired, region.Active) && !region.Active.Scope.IsDisposed, "Conditional update reused a disposed scope.");

        var keyedGraph = new ReactiveGraph();
        var rows = keyedGraph.Signal(new[] { 1 }, "manual-scope-keyed-rows");
        using var keyedComposition = new Composition(keyedGraph, "manual-scope-keyed-root");
        var keyed = keyedComposition.ForEach(keyedComposition.Root, "manual-scope-keyed-region", () => rows.Value, value => value, (_, context) => context.Element("manual-scope-keyed-child"));
        keyedGraph.Drain();
        var keyedRetired = keyed.Items.Single();
        keyedRetired.Scope.Dispose();
        Assert(keyedRetired.IsDisposed && keyed.Items.Count == 0 && keyed.Region.Children.Count == 0 &&
            !keyedComposition.Dump().Contains("manual-scope-keyed-child", StringComparison.Ordinal), "Disposed keyed scope left tree or cache state.");
        keyed.Update([1]);
        Assert(keyed.Items.Count == 1 && !ReferenceEquals(keyedRetired, keyed.Items[0]) && !keyed.Items[0].Scope.IsDisposed, "Same-key update reused a disposed keyed scope.");

        var probe = RetiredScopePayload();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!probe.Payload.IsAlive, "Rooted composition retained a scope-disposed element.");
        GC.KeepAlive(probe.Root);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ScopeProbe RetiredScopePayload()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "manual-scope-release-rows");
        var composition = new Composition(graph, "manual-scope-release-root");
        var keyed = composition.ForEach(composition.Root, "manual-scope-release-region", () => rows.Value, value => value, (_, context) => context.Element("manual-scope-release-child"));
        graph.Drain();
        var retired = keyed.Items.Single();
        var payload = new WeakReference(retired);
        retired.Scope.Dispose();
        keyed.Update([1]);
        return new ScopeProbe(payload, composition);
    }

    private static void KeyedFactoryTransactionsAndReentrancy()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(Array.Empty<int>(), "keyed-transaction-rows");
        using var composition = new Composition(graph, "keyed-transaction-root");
        Element? firstRoot = null;
        Element? firstLeaf = null;
        var cleanup = 0;
        var region = composition.ForEach(composition.Root, "keyed-transaction-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-transaction-child");
            root.Scope.OnDispose(() => cleanup++);
            if (value == 1)
            {
                firstRoot = root;
                firstLeaf = context.Child(root, "keyed-transaction-leaf");
            }
            else if (value == 2) firstLeaf!.Scope.Dispose();
            else firstRoot!.Scope.Dispose();
            return root;
        });
        graph.Drain();
        var before = composition.Dump();
        rows.Value = [1, 2, 3];
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && region.Items.Count == 0 && region.Region.Children.Count == 0 && cleanup == 3,
            "Later keyed factories left a disposed provisional root or descendant, or leaked rollback cleanup.");

        var probe = RetainedEntryRollback();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!probe.Retained.IsAlive && !probe.Provisional.IsAlive, "Failed keyed rollback retained an entry or provisional root.");
        GC.KeepAlive(probe.Root);

        var orderingProbe = KeyedOrderingRollback();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(orderingProbe.Payloads.All(payload => !payload.IsAlive), "Failed keyed ordering retained a completed provisional context.");
        GC.KeepAlive(orderingProbe.Root);

        var reentrantGraph = new ReactiveGraph();
        var reentrantRows = reentrantGraph.Signal(new[] { 1 }, "keyed-reentrant-rows");
        using var reentrantComposition = new Composition(reentrantGraph, "keyed-reentrant-root");
        var reentrant = reentrantComposition.ForEach(reentrantComposition.Root, "keyed-reentrant-region", () => reentrantRows.Value,
            value => value, (_, context) => context.Element("keyed-reentrant-child"));
        reentrantGraph.Drain();
        var departed = reentrant.Items.Single();
        departed.Scope.OnDispose(() => reentrant.Update([1]));
        departed.Scope.Dispose();
        var remounted = reentrant.Items.Single();
        Assert(!ReferenceEquals(departed, remounted) && !remounted.IsDisposed && !remounted.Scope.IsDisposed &&
            reentrant.Region.Children.SequenceEqual([remounted]) &&
            reentrantComposition.Dump().Split('\n').Count(line => line.Contains("keyed-reentrant-child", StringComparison.Ordinal)) == 1,
            "Reentrant same-key cleanup left keyed cache, tree, or dump inconsistent.");
        reentrant.Update([1]);
        Assert(ReferenceEquals(remounted, reentrant.Items.Single()), "An old disposed entry removed its same-key remount.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static KeyedRollbackProbe RetainedEntryRollback()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "keyed-retained-rows");
        var composition = new Composition(graph, "keyed-retained-root");
        Element? retained = null;
        WeakReference? retainedWeak = null;
        WeakReference? provisionalWeak = null;
        var region = composition.ForEach(composition.Root, "keyed-retained-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-retained-child");
            if (value == 1)
            {
                retained = root;
                retainedWeak = new WeakReference(root);
            }
            else
            {
                provisionalWeak = new WeakReference(root);
                retained!.Scope.Dispose();
                retained = null;
            }
            return root;
        });
        graph.Drain();
        rows.Value = [1, 2];
        ExpectAggregate(graph.Drain);
        Assert(region.Items.Count == 0 && region.Region.Children.Count == 0 &&
            !composition.Dump().Contains("keyed-retained-child", StringComparison.Ordinal), "Disposed retained entry or provisional root remained live after rollback.");
        return new KeyedRollbackProbe(retainedWeak!, provisionalWeak!, composition);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static KeyPreparationProbe KeyedOrderingRollback()
    {
        var graph = new ReactiveGraph();
        var first = new MutableKey(1);
        var second = new MutableKey(2);
        var rows = graph.Signal(new[] { first, second }, "keyed-ordering-rows");
        var weak = new List<WeakReference>();
        var cleanup = 0;
        var composition = new Composition(graph, "keyed-ordering-root");
        var region = composition.ForEach(composition.Root, "keyed-ordering-region", () => rows.Value, value => value, (value, context) =>
        {
            var root = context.Element("keyed-ordering-child");
            weak.Add(new WeakReference(root));
            root.Scope.OnDispose(() => cleanup++);
            if (ReferenceEquals(value, second)) first.Hash = 3;
            return root;
        });
        var before = composition.Dump();
        ExpectAggregate(graph.Drain);
        Assert(composition.Dump() == before && region.Items.Count == 0 && region.Region.Children.Count == 0 && cleanup == 2,
            "Keyed ordering failure committed or retained provisional content.");
        return new KeyPreparationProbe(weak, composition);
    }

    private static void ReleasedPayload()
    {
        var probe = RemovedPayload();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(probe.Payloads.All(payload => !payload.IsAlive), "Departed composition ownership retained a facet payload after forced GC.");
        GC.KeepAlive(probe.Root);
    }

    private static void ReleasedOwnershipIdentities()
    {
        var probe = ReleasedIdentities();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!probe.Element.IsAlive && !probe.Conditional.IsAlive && !probe.Keyed.IsAlive, "Active scopes retained manually disposed ownership identities.");
        GC.KeepAlive(probe.Root);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PayloadProbe RemovedPayload()
    {
        var graph = new ReactiveGraph();
        var rows = graph.Signal(new[] { 1 }, "release-rows");
        var weak = new List<WeakReference>();
        var composition = new Composition(graph, "release-root");
        _ = composition.ForEach(composition.Root, "release-region", () => rows.Value, value => value, (item, context) =>
        {
            var effectPayload = new Payload();
            weak.Add(new WeakReference(effectPayload));
            var row = context.Element("release-row");
            _ = row.Scope.Effect(() => GC.KeepAlive(effectPayload), "release-effect");
            var asyncPayload = new Payload();
            weak.Add(new WeakReference(asyncPayload));
            var pending = row.Scope.Async(_ => Task.FromResult(asyncPayload), "release-async");
            _ = pending.Value;
            var genericPayload = new Payload();
            weak.Add(new WeakReference(genericPayload));
            row.Scope.OnDispose(() => GC.KeepAlive(genericPayload));
            return row;
        });
        graph.Drain();
        rows.Value = [];
        graph.Drain();
        return new PayloadProbe(weak, composition);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IdentityProbe ReleasedIdentities()
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "identity-root");
        var element = composition.Child(composition.Root, "manual-element");
        var elementWeak = new WeakReference(element);
        element.Dispose();

        var conditional = composition.When(composition.Root, "manual-conditional", () => false, context => context.Element("unused"));
        graph.Drain();
        var conditionalWeak = new WeakReference(conditional);
        conditional.Dispose();

        var keyed = composition.ForEach(composition.Root, "manual-keyed", Array.Empty<int>, value => value, (_, context) => context.Element("unused-item"));
        graph.Drain();
        var keyedWeak = new WeakReference(keyed);
        keyed.Dispose();
        return new IdentityProbe(elementWeak, conditionalWeak, keyedWeak, composition);
    }

    private static string EquivalentDump()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "dump-root");
        var header = composition.Child(composition.Root, "dump-header");
        _ = composition.Child(header, "dump-title");
        return composition.Dump();
    }

    private static void ExpectAggregate(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected aggregate failure."); }
        catch (AggregateException) { }
    }

    private static AggregateException CaptureAggregate(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected aggregate failure."); }
        catch (AggregateException exception) { return exception; }
    }

    private static void ExpectErrors(AggregateException exception, params string[] messages)
    {
        var actual = exception.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
        Assert(actual.SequenceEqual(messages), "Expected exact errors: " + string.Join(", ", messages) + "; actual: " + string.Join(", ", actual));
    }

    private static void ExpectDisposed(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected disposed failure."); }
        catch (ObjectDisposedException) { }
    }

    private static void ExpectNotSupported(Action action)
    {
        try { action(); throw new InvalidOperationException("Expected immutable view failure."); }
        catch (NotSupportedException) { }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record PayloadProbe(IReadOnlyList<WeakReference> Payloads, object Root);
    private sealed record IdentityProbe(WeakReference Element, WeakReference Conditional, WeakReference Keyed, object Root);
    private sealed record ScopeProbe(WeakReference Payload, object Root);
    private sealed record KeyedRollbackProbe(WeakReference Retained, WeakReference Provisional, object Root);
    private sealed record KeyPreparationProbe(IReadOnlyList<WeakReference> Payloads, object Root);
    private sealed class MutableKey(int hash)
    {
        internal int Hash { get; set; } = hash;
        public override int GetHashCode() => Hash;
    }
    private sealed class Payload;
}
