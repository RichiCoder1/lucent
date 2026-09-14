using System.Collections.ObjectModel;

namespace Lucent.Core;

/// <summary>
/// Creates recipe-backed retained route outlets over one <see cref="NavigationSession"/>.
/// </summary>
/// <remarks>
/// The outlet deliberately receives one synchronous recipe for each generated route level.
/// Route definitions supply the closed route-context providers; application composition supplies
/// the level recipe. A root outlet owns the session participant. A nested outlet consumes the
/// private cursor provided by its parent level and never attaches another session participant.
/// </remarks>
public static class RouteOutlet
{
    private static readonly ComponentRequirementSource NavigationSource = new(
        "NavigationSession",
        "Lucent.Core.NavigationSession",
        "<framework>",
        1,
        1
    );

    private static readonly ComponentRequirementSource CursorSource = new(
        "RouteOutletCursor",
        "Lucent.Core.RouteOutletCursor",
        "<framework>",
        1,
        1
    );

    private static readonly ComponentRequirementPlan<NavigationSession> RootRequirements =
        ComponentRequirements.Context<NavigationSession>(NavigationSource);

    private static readonly ComponentRequirementPlan<ChildInputs> ChildRequirements =
        ComponentRequirements
            .Context<RouteOutletCursor>(CursorSource)
            .AndContext<NavigationSession>(NavigationSource)
            .Select(values => new ChildInputs(values.Previous, values.Value));

    /// <summary>
    /// Creates a root outlet recipe. The recipe requires the exact nearest
    /// <see cref="NavigationSession"/> provider at its mount site.
    /// </summary>
    public static ComponentRecipe Create(
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, ComponentRecipe> levelFactory,
        RouteOutletHandle? handle = null,
        string? name = null,
        RouteOutletOptions? options = null
    )
    {
        ValidateInputs(descriptors, levelFactory);
        var kind = name ?? "route-outlet";
        return ComponentRecipe.Defer(
            kind,
            RootRequirements,
            (owner, session) =>
                MountedRecipe(
                    kind,
                    owner,
                    session,
                    descriptors,
                    levelFactory,
                    cursor: null,
                    handle,
                    options
                )
        );
    }

    /// <summary>
    /// Creates a nested outlet recipe. It consumes the exact cursor provided by its matched
    /// parent route level and verifies that the supplied session is the same session.
    /// </summary>
    public static ComponentRecipe CreateChild(
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, ComponentRecipe> levelFactory,
        RouteOutletHandle? handle = null,
        string? name = null
    )
    {
        ValidateInputs(descriptors, levelFactory);
        var kind = name ?? "route-outlet";
        return ComponentRecipe.Defer(
            kind,
            ChildRequirements,
            (owner, inputs) =>
                MountedRecipe(
                    kind,
                    owner,
                    inputs.Value,
                    descriptors,
                    levelFactory,
                    inputs.Previous,
                    handle,
                    options: null
                )
        );
    }

    private static ComponentRecipe MountedRecipe(
        string kind,
        ReactiveScope owner,
        NavigationSession session,
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, ComponentRecipe> levelFactory,
        RouteOutletCursor? cursor,
        RouteOutletHandle? handle,
        RouteOutletOptions? options
    ) =>
        ComponentRecipe.Create(
            kind,
            (context, host) =>
            {
                // The outlet is a transparent retained host, but it still participates in the
                // surrounding authored layout so a published route root receives its bounds.
                host.Present(
                    context.Theme,
                    author: Style.Empty.Axis(LayoutAxis.Column).MainGrow(1)
                );
                var outlet = new RouteOutletMount(
                    session,
                    descriptors,
                    levelFactory,
                    host,
                    cursor,
                    handle,
                    cursor?.Owner.Options ?? options
                );
                owner.OnDispose(outlet.Dispose);
                if (handle is not null)
                    handle.Attach(outlet);
                outlet.Initialize();
            }
        );

    private static void ValidateInputs(
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, ComponentRecipe> levelFactory
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(levelFactory);
    }

    private sealed record ChildInputs(RouteOutletCursor Previous, NavigationSession Value);
}

/// <summary>Internal retained state for one route level and its optional nested outlet.</summary>
internal sealed class RouteOutletBuildNode
{
    internal RouteOutletBuildNode(
        int level,
        RouteLevelDescriptor definition,
        RouteOutletLevelKey key,
        Element? root,
        MountContext? context,
        RouteOutletCursor? cursor,
        RouteContextLiveState live
    )
    {
        Level = level;
        Definition = definition;
        Key = key;
        Root = root;
        Context = context;
        Cursor = cursor;
        Live = live ?? throw new ArgumentNullException(nameof(live));
    }

    internal int Level { get; }
    internal RouteLevelDescriptor Definition { get; }
    internal RouteOutletLevelKey Key { get; }
    internal Element? Root { get; set; }
    internal MountContext? Context { get; set; }
    internal RouteOutletCursor? Cursor { get; set; }
    internal RouteContextLiveState Live { get; }
    internal RouteOutletMount? ChildOutlet { get; set; }

    internal bool IsNew => Context is not null;

    internal void DisposeProvisional()
    {
        ChildOutlet?.DisposeProvisional();
        ChildOutlet = null;
        if (Context is { } context)
        {
            Context = null;
            context.Dispose();
        }
        Live.Clear();
    }
}

/// <summary>Owns a level's generated definition and its canonical owned captures.</summary>
internal sealed class RouteOutletLevelKey
{
    private readonly RouteValue[] _values;

    internal RouteOutletLevelKey(RouteDefinitionId definition, IReadOnlyList<RouteValue> values)
    {
        Definition = definition;
        _values = values.ToArray();
    }

    internal RouteDefinitionId Definition { get; }

    internal bool Matches(RouteOutletLevelKey other)
    {
        if (!Equals(Definition, other.Definition) || _values.Length != other._values.Length)
            return false;
        for (var index = 0; index < _values.Length; index++)
            if (_values[index] != other._values[index])
                return false;
        return true;
    }
}

/// <summary>One root participant stage containing every nested outlet candidate.</summary>
internal sealed class RouteOutletStage : NavigationStage
{
    private readonly RouteOutletMount _root;
    private bool _applied;
    private bool _retirementCompleted;
    private readonly List<Element> _retired = [];

    internal RouteOutletStage(RouteOutletMount root)
    {
        _root = root;
    }

    internal IReadOnlyList<Element> Retired => _retired;

    internal void AddRetired(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _retired.Add(element);
    }

    internal void Apply(NavigationPublication publication)
    {
        if (_applied)
            throw new InvalidOperationException("A route outlet stage was published twice.");
        _root.PublishStaged(this, publication);
        _applied = true;
    }

    internal void Retire()
    {
        if (_retirementCompleted)
            return;
        List<Exception>? errors = null;
        foreach (var element in _retired.ToArray())
            if (!element.IsDisposed)
                try
                {
                    element.Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
                finally
                {
                    if (element.IsDisposed)
                        _retired.Remove(element);
                }
            else
                _retired.Remove(element);
        _retirementCompleted = _retired.Count == 0;
        Composition.ThrowAll(errors, "Route outlet retirement failed.");
    }

    protected override void DisposeCore()
    {
        List<Exception>? errors = null;
        try
        {
            _root.DiscardStaged();
        }
        catch (Exception exception)
        {
            errors = [exception];
        }
        try
        {
            Retire();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        Composition.ThrowAll(errors, "Route outlet stage cleanup failed.");
    }
}

/// <summary>One root or nested route outlet controller.</summary>
internal sealed class RouteOutletMount : INavigationTransactionParticipant, IDisposable
{
    private readonly RouteDescriptorSet _descriptors;
    private readonly Func<RouteLevelDescriptor, ComponentRecipe> _levelFactory;
    private readonly Element _host;
    private readonly RouteOutletCursor? _cursor;
    private readonly RouteOutletHandle? _handle;
    private readonly RouteOutletOptions? _options;
    private readonly NavigationInteraction? _interaction;
    private readonly Composition _composition;
    private readonly bool _isRoot;
    private readonly int _startLevel;
    private IDisposable? _registration;
    private RouteOutletBuildNode? _active;
    private RouteOutletBuildNode? _staged;
    private RouteOutletSnapshot _snapshot = new(0, null, null, []);
    private bool _initialized;
    private bool _disposed;
    private long _revision;

    internal RouteOutletMount(
        NavigationSession session,
        RouteDescriptorSet descriptors,
        Func<RouteLevelDescriptor, ComponentRecipe> levelFactory,
        Element host,
        RouteOutletCursor? cursor,
        RouteOutletHandle? handle,
        RouteOutletOptions? options
    )
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
        _levelFactory = levelFactory ?? throw new ArgumentNullException(nameof(levelFactory));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _cursor = cursor;
        _handle = handle;
        _options = options;
        _interaction = cursor is null ? options?.Interaction : null;
        _composition = host.Composition;
        _isRoot = cursor is null;
        _startLevel = cursor?.StartLevel ?? 0;
        if (!ReferenceEquals(Session.RouteTable, descriptors.Table))
            throw new ArgumentException(
                "A route outlet descriptor set must use the session's exact RouteTable instance.",
                nameof(descriptors)
            );
        if (cursor is not null && !ReferenceEquals(cursor.Session, Session))
            throw new InvalidOperationException(
                "A nested route outlet cannot combine a cursor and another navigation session."
            );
        if (_interaction is not null && !ReferenceEquals(_interaction.Session, Session))
            throw new InvalidOperationException(
                "A route outlet interaction must belong to the outlet's exact navigation session."
            );
    }

    internal NavigationSession Session { get; }

    internal RouteOutletOptions? Options => _options;

    internal RouteOutletSnapshot Snapshot => _snapshot;

    internal static void RegisterChild(RouteOutletBuildNode parent, RouteOutletMount child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        if (parent.ChildOutlet is not null && !ReferenceEquals(parent.ChildOutlet, child))
            throw new InvalidOperationException(
                "A retained route level can contain one nested RouteOutlet only."
            );
        parent.ChildOutlet = child;
    }

    internal void Initialize()
    {
        _composition.CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            throw new InvalidOperationException("A route outlet was initialized twice.");
        if (_cursor is not null)
            _cursor.Claim(this);
        _initialized = true;
        if (!_isRoot)
            return;

        _registration = Session.AttachParticipant(this);
        var current = Session.Current;
        if (current is null)
            return;

        RouteOutletStage? stage = null;
        try
        {
            stage = PrepareInitial(current);
            var publication = new NavigationPublication(
                0,
                0,
                null,
                current,
                Session.Journal,
                NavigationHistoryAction.Replace,
                NavigationOrigin.Application
            );
            stage.Apply(publication);
            stage.Retire();
            UpdateSnapshot(current, 0);
            _interaction?.AfterPublish(publication, _snapshot);
        }
        catch
        {
            stage?.Dispose();
            throw;
        }
    }

    public async ValueTask<NavigationPreparationResult> PrepareAsync(
        NavigationPrepareRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepare = _options?.Prepare;
        if (prepare is null)
            return NavigationPreparationResult.Allow;

        var target = request.Target;
        var publicRequest = new RouteOutletPreparationRequest(
            request.OperationId,
            request.Generation,
            request.Current,
            target,
            request.History,
            request.Origin,
            request.Phase,
            request.RedirectCount
        );
        var definition =
            request.Phase == NavigationPreparationPhase.Leave
                ? request.Current is null
                    ? null
                    : _descriptors.GetDefinition(request.Current.Match)
                : _descriptors.GetDefinition(target.Match);
        if (definition is null)
            return NavigationPreparationResult.Allow;

        if (request.Phase == NavigationPreparationPhase.Leave)
        {
            for (var index = definition.Branch.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await prepare(
                        definition.Branch[index],
                        publicRequest,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(result);
                if (result.Kind != NavigationPreparationResultKind.Allow)
                    return result;
            }
        }
        else
        {
            foreach (var level in definition.Branch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await prepare(level, publicRequest, cancellationToken)
                    .ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(result);
                if (result.Kind != NavigationPreparationResultKind.Allow)
                    return result;
            }
        }

        return NavigationPreparationResult.Allow;
    }

    public NavigationStage Stage(NavigationStageRequest request)
    {
        _composition.CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isRoot)
            throw new InvalidOperationException("Only the root route outlet may stage navigation.");
        _ = _descriptors.GetDefinition(request.Target.Match);
        var stage = new RouteOutletStage(this);
        try
        {
            _ = PrepareFor(request.Target, stage);
            return stage;
        }
        catch
        {
            DiscardStaged();
            throw;
        }
    }

    public void Publish(NavigationStage stage, NavigationPublication publication)
    {
        _composition.CheckThread();
        if (stage is not RouteOutletStage outletStage)
            throw new ArgumentException(
                "The stage belongs to another navigation participant.",
                nameof(stage)
            );
        outletStage.Apply(publication);
        UpdateSnapshot(publication.Current, publication.Generation);
        _interaction?.AfterPublish(publication, _snapshot);
    }

    public void Retire(NavigationRetirement retirement)
    {
        _composition.CheckThread();
        // The most recent stage has already swapped all roots. Retirement is deliberately
        // separate so observers see the new Current before old owned roots are released.
        if (_lastPublishedStage is { } stage)
        {
            _lastPublishedStage = null;
            stage.Retire();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _composition.CheckThread();
        _disposed = true;
        _cursor?.Release(this);
        try
        {
            _registration?.Dispose();
        }
        finally
        {
            _registration = null;
            try
            {
                DiscardStaged();
            }
            finally
            {
                var active = _active;
                _active = null;
                if (active is not null && active.Root is { IsDisposed: false } root)
                    root.Dispose();
            }
        }
        _handle?.Detach(this);
    }

    internal void DisposeProvisional()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cursor?.Release(this);
        DiscardStaged();
        _active = null;
        _snapshot = new RouteOutletSnapshot(_revision, null, null, []);
        _handle?.Detach(this);
    }

    private RouteOutletStage PrepareInitial(NavigationSnapshot target)
    {
        var stage = new RouteOutletStage(this);
        try
        {
            _ = PrepareFor(target, stage);
            return stage;
        }
        catch
        {
            DiscardStaged();
            throw;
        }
    }

    private RouteOutletBuildNode? PrepareFor(NavigationSnapshot target, RouteOutletStage stage)
    {
        var definition = _descriptors.GetDefinition(target.Match);
        var branch = definition.Branch;
        if (_startLevel > branch.Count)
            throw new InvalidOperationException(
                "A nested route outlet cursor points beyond the matched route branch."
            );
        var result =
            branch.Count == _startLevel ? null : PrepareNode(definition, target, stage, _active);
        _staged = result;
        return result;
    }

    private RouteOutletBuildNode PrepareNode(
        RouteDefinitionDescriptor terminal,
        NavigationSnapshot target,
        RouteOutletStage stage,
        RouteOutletBuildNode? active
    )
    {
        var match = target.Match;
        var definition = terminal.Branch[_startLevel];
        var key = CreateKey(definition, match);
        RouteOutletBuildNode node;
        if (active is not null && active.Key.Matches(key))
        {
            node = active;
        }
        else
        {
            node = MountNode(target, definition);
        }

        var hasChild = terminal.Branch.Count > _startLevel + 1;
        if (node.ChildOutlet is null)
        {
            if (hasChild)
                throw new InvalidOperationException(
                    "A matched route branch requires a nested RouteOutlet at level "
                        + _startLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "."
                );
        }
        else
        {
            _ = node.ChildOutlet.PrepareFor(target, stage);
        }
        _staged = node;
        return node;
    }

    private RouteOutletBuildNode MountNode(
        NavigationSnapshot target,
        RouteLevelDescriptor definition
    )
    {
        var match = target.Match;
        var key = CreateKey(definition, match);
        // The cursor is created before the recipe is mounted so a nested outlet can consume it
        // while the candidate recipe is still unattached.
        var placeholder = new RouteOutletBuildNode(
            _startLevel,
            definition,
            key,
            root: null,
            context: null,
            cursor: null,
            live: new RouteContextLiveState(target)
        );
        var liveOwner = _host.Scope.CreateChild("route-context-live");
        MountContext? mount = null;
        try
        {
            placeholder.Live.Attach(liveOwner);
            var cursor = new RouteOutletCursor(this, _startLevel + 1, placeholder);
            var recipe = _levelFactory(definition);
            ArgumentNullException.ThrowIfNull(recipe);
            recipe = definition.ProvideContext(
                match,
                Context.Provide(cursor, recipe),
                placeholder.Live
            );
            var environment =
                _host.MountEnvironment
                ?? throw new InvalidOperationException(
                    "A route outlet host has no inherited mount environment."
                );
            mount = new MountContext(_composition, _host, environment: environment);
            var root = mount.Run(() => recipe.Mount(mount));
            mount.Validate(root);
            placeholder.Root = root;
            placeholder.Context = mount;
            placeholder.Cursor = cursor;
            root.Scope.OnDispose(() =>
            {
                placeholder.Live.Clear();
                liveOwner.Dispose();
            });
            cursor.ReplaceParent(placeholder);
            return placeholder;
        }
        catch (Exception exception)
        {
            List<Exception>? cleanupFailures = null;
            try
            {
                mount?.Dispose();
            }
            catch (Exception cleanup)
            {
                cleanupFailures = [cleanup];
            }
            try
            {
                placeholder.Live.Clear();
                liveOwner.Dispose();
            }
            catch (Exception cleanup)
            {
                (cleanupFailures ??= []).Add(cleanup);
            }
            if (cleanupFailures is { Count: > 0 })
                throw new AggregateException(new[] { exception }.Concat(cleanupFailures));
            throw;
        }
    }

    private static RouteOutletLevelKey CreateKey(
        RouteLevelDescriptor definition,
        RouteMatch match
    ) => new(definition.Id, definition.OwnedCaptureSlots.Select(match.GetValue).ToArray());

    internal void PublishStaged(RouteOutletStage stage, NavigationPublication publication)
    {
        _interaction?.BeforePublish(publication, _snapshot);
        if (_interaction is not null)
        {
            var attached = new List<(Element Host, Element Root)>();
            try
            {
                AttachStagedForValidation(attached);
                _interaction.ValidatePublish(
                    CreateStagedSnapshot(publication.Current, publication.Generation)
                );
            }
            finally
            {
                for (var index = attached.Count - 1; index >= 0; index--)
                    attached[index].Host.Detach(attached[index].Root);
            }
        }
        PublishNode(stage);
        _lastPublishedStage = stage;
    }

    private void AttachStagedForValidation(List<(Element Host, Element Root)> attached)
    {
        var next = _staged;
        if (next is null)
            return;
        if (!ReferenceEquals(next, _active))
        {
            _host.Attach(next.Root!);
            attached.Add((_host, next.Root!));
        }
        next.ChildOutlet?.AttachStagedForValidation(attached);
    }

    private RouteOutletSnapshot CreateStagedSnapshot(NavigationSnapshot current, long generation)
    {
        var levels = new List<RouteOutletLevelSnapshot>();
        RouteOutletMount? outlet = this;
        while (outlet is not null)
        {
            var node = outlet._staged ?? outlet._active;
            if (node is null)
                break;
            levels.Add(new(node.Level, node.Definition.Id, node.Root!.Id));
            outlet = node.ChildOutlet;
        }
        return new RouteOutletSnapshot(
            generation == 0 ? checked(_revision + 1) : generation,
            current.EntryId,
            current.DefinitionId,
            levels
        );
    }

    private RouteOutletStage? _lastPublishedStage;

    private void PublishNode(RouteOutletStage stage)
    {
        var previous = _active;
        var next = _staged;
        if (next is not null && !ReferenceEquals(next, previous))
        {
            if (previous?.Root is { IsDisposed: false } previousRoot)
            {
                _host.Detach(previousRoot);
                stage.AddRetired(previousRoot);
            }
            _host.Attach(next.Root!);
            if (next.Context is { } context)
            {
                context.Complete();
                context.Dispose();
                next.Context = null;
            }
        }
        else if (next is null && previous?.Root is { IsDisposed: false } previousRoot)
        {
            _host.Detach(previousRoot);
            stage.AddRetired(previousRoot);
        }
        _active = next;
        _staged = null;
        _active?.ChildOutlet?.PublishNode(stage);
    }

    internal void DiscardStaged()
    {
        var staged = _staged;
        _staged = null;
        if (staged is null)
            return;
        if (ReferenceEquals(staged, _active))
        {
            staged.ChildOutlet?.DiscardStaged();
            return;
        }
        staged.DisposeProvisional();
    }

    private void UpdateSnapshot(NavigationSnapshot current, long generation)
    {
        _revision = generation == 0 ? checked(_revision + 1) : generation;
        var levels = new List<RouteOutletLevelSnapshot>();
        for (var node = _active; node is not null; node = node.ChildOutlet?._active)
            levels.Add(new(node.Level, node.Definition.Id, node.Root!.Id));
        _snapshot = new RouteOutletSnapshot(
            _revision,
            current.EntryId,
            current.DefinitionId,
            levels
        );
        _handle?.Update(_snapshot);
        _active?.ChildOutlet?.UpdateSnapshot(current, generation);
        _active?.Live.Update(current, _active.ChildOutlet?.Snapshot);
    }
}
