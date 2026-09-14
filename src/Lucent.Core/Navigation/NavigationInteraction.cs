using System.Text;

namespace Lucent.Core;

/// <summary>
/// Owns route-facing focus, viewport, command, and semantic state over one navigation session.
/// </summary>
/// <remarks>
/// Push starts with authored viewport defaults and the preferred route target. Replace transfers
/// the departing entry state when its authored targets still exist. Back and Forward restore the
/// explicit state previously captured for the selected journal entry.
/// </remarks>
public sealed class NavigationInteraction : IDisposable
{
    private const int MaximumTargetIdBytes = 128;
    private const int MaximumLabelBytes = 512;
    private readonly ReactiveScope _scope;
    private readonly NavigationSession _session;
    private readonly Signal<string?> _announcement;
    private readonly Signal<long> _announcementGeneration;
    private readonly Signal<FocusReconciliation?> _focusReconciliation;
    private readonly ApplicationCommand _keyboardBackCommand;
    private readonly List<TargetRegistration> _targets = [];
    private readonly Dictionary<long, NavigationEntryInteractionState> _entryStates = [];
    private BoundaryRegistration? _boundary;
    private Departure? _departure;
    private FocusReconciliation? _scheduledFocusReconciliation;
    private IDisposable? _focusReconciliationDispatch;
    private ReactiveEffect? _focusReconciliationEffect;
    private long _focusReconciliationGeneration;
    private long _lastFocusReconciliationGeneration;
    private bool _disposed;

    /// <summary>Creates interaction state owned by <paramref name="owner"/> for one exact session.</summary>
    public NavigationInteraction(
        ReactiveScope owner,
        NavigationSession session,
        string name = "navigation-interaction"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(session);
        ReactiveGraph.ValidateName(name, nameof(name));
        if (!ReferenceEquals(owner.Graph, session.Graph))
            throw new ArgumentException(
                "A navigation interaction and session must belong to the same reactive graph.",
                nameof(session)
            );
        _scope = owner.CreateChild(name);
        _session = session;
        _announcement = _scope.Signal<string?>(null, name + ".announcement");
        _announcementGeneration = _scope.Signal(0L, name + ".announcement-generation");
        _focusReconciliation = _scope.Signal<FocusReconciliation?>(
            null,
            name + ".focus-reconciliation"
        );
        _keyboardBackCommand = CreateBackCommand(
            _scope,
            NavigationOrigin.Keyboard,
            name + ".keyboard-back"
        );
        _scope.OnDispose(DisposeFromOwnerScope);
    }

    /// <summary>Gets the session whose journal remains authoritative.</summary>
    public NavigationSession Session => _session;

    /// <summary>Gets the committed route directly from the authoritative session.</summary>
    public NavigationSnapshot? Current => _session.Current;

    /// <summary>Gets the pending route directly from the authoritative session.</summary>
    public NavigationPending? Pending => _session.Pending;

    /// <summary>Gets the bounded journal directly from the authoritative session.</summary>
    public NavigationJournalSnapshot Journal => _session.Journal;

    /// <summary>Gets the live keyboard Back command installed by the semantic boundary.</summary>
    public ApplicationCommand KeyboardBackCommand => _keyboardBackCommand;

    /// <summary>Gets retained interaction state for one live journal entry.</summary>
    public bool TryGetEntryState(long entryId, out NavigationEntryInteractionState? state)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryId);
        CheckLive();
        return _entryStates.TryGetValue(entryId, out state);
    }

    /// <summary>Routes a typed reference through the session's guarded transaction path.</summary>
    public NavigationOperation Navigate(
        RouteReference reference,
        NavigationHistoryAction history = NavigationHistoryAction.Push,
        NavigationOrigin origin = NavigationOrigin.Application
    )
    {
        CheckLive();
        return _session.Navigate(reference, history, origin);
    }

    /// <summary>Routes Back through the session's guarded transaction path.</summary>
    public NavigationOperation Back(NavigationOrigin origin = NavigationOrigin.Application)
    {
        CheckLive();
        return _session.Back(origin);
    }

    /// <summary>Creates an origin-specific command that reads the live Back state when invoked.</summary>
    public ApplicationCommand CreateBackCommand(
        ReactiveScope owner,
        NavigationOrigin origin,
        string name = "navigation-back"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateCommandOrigin(origin);
        CheckLive();
        ValidateCommandOwner(owner);
        return new ApplicationCommand(
            owner,
            cancellation => Await(_session.Back(origin), cancellation),
            () =>
                !_disposed && !_session.IsDisposed && !_session.IsTerminated && _session.CanGoBack,
            name
        );
    }

    /// <summary>Creates an origin-specific command that resolves its typed target when invoked.</summary>
    public ApplicationCommand CreateNavigateCommand(
        ReactiveScope owner,
        Func<RouteReference> reference,
        NavigationHistoryAction history = NavigationHistoryAction.Push,
        NavigationOrigin origin = NavigationOrigin.Application,
        string name = "navigation-navigate"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(reference);
        if (history is not NavigationHistoryAction.Push and not NavigationHistoryAction.Replace)
            throw new ArgumentOutOfRangeException(nameof(history));
        ValidateCommandOrigin(origin);
        CheckLive();
        ValidateCommandOwner(owner);
        return new ApplicationCommand(
            owner,
            cancellation => Await(_session.Navigate(reference(), history, origin), cancellation),
            () => !_disposed && !_session.IsDisposed && !_session.IsTerminated,
            name
        );
    }

    /// <summary>Releases target registrations and retained entry interaction state.</summary>
    public void Dispose() => _scope.Dispose();

    internal IDisposable RegisterTarget(
        ReactiveScope owner,
        string id,
        string label,
        NavigationTargetKind kind,
        FocusTarget focusTarget,
        ViewportState? viewport
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(focusTarget);
        ValidateText(id, nameof(id), MaximumTargetIdBytes);
        ValidateText(label, nameof(label), MaximumLabelBytes);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        CheckLive();
        if (
            !ReferenceEquals(owner.Graph, _scope.Graph)
            || !ReferenceEquals(focusTarget.Graph, _scope.Graph)
        )
            throw new ArgumentException(
                "A navigation target, focus target, and interaction must belong to the same reactive graph."
            );
        var registration = new TargetRegistration(id, label, kind, focusTarget, viewport);
        _targets.Add(registration);
        owner.OnDispose(registration.Dispose);
        registration.Disposed += RemoveTarget;
        return registration;
    }

    internal void AttachBoundary(BehaviorContext context, string label)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateText(label, nameof(label), MaximumLabelBytes);
        CheckLive();
        if (!ReferenceEquals(context.Composition.Graph, _scope.Graph))
            throw new InvalidOperationException(
                "A navigation boundary and interaction must belong to the same reactive graph."
            );
        if (_boundary is not null)
            throw new InvalidOperationException(
                "A navigation interaction can have one live semantic boundary."
            );
        _boundary = new BoundaryRegistration(context, context.Composition, context.Identity);
        context.OnDispose(() => _boundary = null);
        context.RegisterCommandScope();
        context.OnKey(route =>
        {
            if (!new KeyChord(Key.Left, KeyModifiers.Alt).Matches(route.Command))
                return;
            _ = _keyboardBackCommand.TryExecute();
            route.Handled = true;
        });
        context.BindSemantics(() =>
        {
            _ = _announcementGeneration.Value;
            var builder = SemanticDeclaration.Create(SemanticRole.Group, label);
            if (_announcement.Value is { } announcement)
                builder.Description(announcement).Announcement(SemanticAnnouncement.Polite);
            return builder.Build();
        });
    }

    // Called by the retained outlet immediately before it replaces its committed branch.
    internal void BeforePublish(
        NavigationPublication publication,
        RouteOutletSnapshot committedOutlet
    )
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(committedOutlet);
        CheckLive();
        SupersedeFocusReconciliation();
        var active = ResolveTargets(committedOutlet, failOnDuplicate: true);
        var focused = _boundary?.Composition.Input.FocusedElement;
        var state = Capture(active, focused);
        if (publication.Previous is { } previous)
            _entryStates[previous.EntryId] = state;
        _departure = new Departure(
            focused,
            focused is { } identity ? DeepestContainingLevel(identity, committedOutlet) : null,
            committedOutlet.Levels.Select(level => level.ElementId).ToHashSet(),
            state
        );
    }

    // Called before a staged outlet branch replaces the committed branch.
    internal void ValidatePublish(RouteOutletSnapshot candidateOutlet)
    {
        ArgumentNullException.ThrowIfNull(candidateOutlet);
        CheckLive();
        _ = ResolveTargets(candidateOutlet, failOnDuplicate: true);
    }

    // Called by the retained outlet after the replacement branch and outlet snapshot are committed.
    internal void AfterPublish(
        NavigationPublication publication,
        RouteOutletSnapshot committedOutlet
    )
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(committedOutlet);
        CheckLive();
        var reconciliationGeneration = SupersedeFocusReconciliation();
        var active = ResolveTargets(committedOutlet, failOnDuplicate: true);
        NavigationEntryInteractionState? desired = publication.History switch
        {
            NavigationHistoryAction.Replace => _departure?.State,
            NavigationHistoryAction.Back or NavigationHistoryAction.Forward =>
                _entryStates.GetValueOrDefault(publication.Current.EntryId),
            _ => null,
        };
        if (desired is not null)
        {
            foreach (var position in desired.Viewports)
                if (
                    active.TryGetValue(position.TargetId, out var target)
                    && target.Viewport is { IsDisposed: false } viewport
                )
                    viewport.Offset = position.Offset;
            _entryStates[publication.Current.EntryId] = desired;
        }
        else if (publication.History == NavigationHistoryAction.Push)
        {
            ResetFreshViewports(active, committedOutlet, _departure);
        }

        var departure = _departure;
        var shouldMoveFocus = ShouldMoveFocus(departure, committedOutlet, publication.History);
        if (shouldMoveFocus)
            Focus(desired?.FocusTargetId, active);

        Prune(publication.Journal);
        if (publication.OperationId != 0)
        {
            var announcement = Preferred(active)?.Label ?? publication.Current.DefinitionId.Value;
            _announcement.Value = announcement;
            _announcementGeneration.Value = checked(_announcementGeneration.Value + 1);
        }
        _departure = null;
        if (shouldMoveFocus || departure?.FocusedLevelId is not null)
            ScheduleFocusReconciliation(
                publication,
                committedOutlet,
                desired?.FocusTargetId,
                reconciliationGeneration
            );
    }

    private void ScheduleFocusReconciliation(
        NavigationPublication publication,
        RouteOutletSnapshot committedOutlet,
        string? requestedTargetId,
        long generation
    )
    {
        if (_boundary is null || committedOutlet.Levels.Count == 0)
            return;
        _scheduledFocusReconciliation = new(
            generation,
            publication.Current.EntryId,
            committedOutlet,
            requestedTargetId
        );
        _focusReconciliationDispatch ??= _scope.Post(PublishFocusReconciliation);
    }

    private long SupersedeFocusReconciliation()
    {
        return _focusReconciliationGeneration = checked(_focusReconciliationGeneration + 1);
    }

    private void PublishFocusReconciliation()
    {
        _focusReconciliationDispatch = null;
        var request = _scheduledFocusReconciliation;
        _scheduledFocusReconciliation = null;
        if (request is null || request.Generation != _focusReconciliationGeneration || _disposed)
            return;
        _focusReconciliation.Value = request;
        _focusReconciliationEffect ??= _scope.Effect(
            ApplyFocusReconciliation,
            _scope.Name + ".focus-reconciliation-effect"
        );
    }

    private void ApplyFocusReconciliation()
    {
        var request = _focusReconciliation.Value;
        if (
            request is null
            || request.Generation != _focusReconciliationGeneration
            || request.Generation == _lastFocusReconciliationGeneration
            || _disposed
        )
            return;
        _lastFocusReconciliationGeneration = request.Generation;
        var current = _scope.Graph.Untracked(() => _session.Current);
        if (_session.IsDisposed || _session.IsTerminated || current?.EntryId != request.EntryId)
            return;
        var active = ResolveTargets(request.Outlet, failOnDuplicate: true);
        var focused = _boundary?.Composition.Input.FocusedElement;
        if (focused is { } identity && _boundary?.Composition.Find(identity) is not null)
            return;
        Focus(request.RequestedTargetId, active);
    }

    private Dictionary<string, TargetRegistration> ResolveTargets(
        RouteOutletSnapshot outlet,
        bool failOnDuplicate
    )
    {
        var result = new Dictionary<string, TargetRegistration>(StringComparer.Ordinal);
        if (_boundary is null || outlet.Levels.Count == 0)
            return result;
        foreach (var registration in _targets.ToArray())
        {
            if (registration.IsDisposed)
                continue;
            var identity = _boundary.Composition.Input.FocusTargetIdentity(
                registration.FocusTarget
            );
            if (identity is null || !BelongsToCommittedBranch(identity.Value, outlet))
                continue;
            if (result.TryAdd(registration.Id, registration))
                continue;
            if (failOnDuplicate)
                throw new InvalidOperationException(
                    "A committed navigation branch contains duplicate authored target identifiers."
                );
        }
        return result;
    }

    private bool BelongsToCommittedBranch(ElementIdentity identity, RouteOutletSnapshot outlet)
    {
        var composition = _boundary!.Composition;
        var target = composition.Find(identity);
        if (target is null)
            return false;
        foreach (var level in outlet.Levels)
        {
            var root = composition.Find(new ElementIdentity(composition.Epoch, level.ElementId));
            if (root is not null && root.IsAncestorOf(target))
                return true;
        }
        return false;
    }

    private NavigationEntryInteractionState Capture(
        IReadOnlyDictionary<string, TargetRegistration> active,
        ElementIdentity? focused
    )
    {
        string? focusedId = null;
        var viewports = new List<NavigationViewportPosition>();
        foreach (var pair in active.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var registration = pair.Value;
            if (
                focused is { } identity
                && _boundary!.Composition.Input.FocusTargetIdentity(registration.FocusTarget)
                    == identity
            )
                focusedId = registration.Id;
            if (registration.Viewport is { IsDisposed: false } viewport)
                viewports.Add(new(registration.Id, viewport.Offset));
        }
        return new(focusedId, viewports);
    }

    private void ResetFreshViewports(
        IReadOnlyDictionary<string, TargetRegistration> active,
        RouteOutletSnapshot next,
        Departure? departure
    )
    {
        long? terminalLevel = next.Levels.Count == 0 ? null : next.Levels[^1].ElementId;
        foreach (var registration in active.Values)
        {
            if (registration.Viewport is not { IsDisposed: false } viewport)
                continue;
            var identity = _boundary!.Composition.Input.FocusTargetIdentity(
                registration.FocusTarget
            );
            var level = identity is null ? null : DeepestContainingLevel(identity.Value, next);
            if (
                departure is null
                || level is null
                || level == terminalLevel
                || !departure.LevelIds.Contains(level.Value)
            )
                viewport.Offset = registration.DefaultViewportOffset;
        }
    }

    private bool ShouldMoveFocus(
        Departure? departure,
        RouteOutletSnapshot next,
        NavigationHistoryAction history
    )
    {
        if (_boundary is null)
            return false;
        if (departure is null || departure.Focused is null)
            return true;
        if (departure.FocusedLevelId is null)
            return false;
        if (!next.Levels.Any(level => level.ElementId == departure.FocusedLevelId.Value))
            return true;
        return history == NavigationHistoryAction.Push
            && next.Levels.Count != 0
            && departure.FocusedLevelId == next.Levels[^1].ElementId;
    }

    private long? DeepestContainingLevel(ElementIdentity identity, RouteOutletSnapshot outlet)
    {
        var composition = _boundary!.Composition;
        var target = composition.Find(identity);
        if (target is null)
            return null;
        long? result = null;
        foreach (var level in outlet.Levels)
        {
            var root = composition.Find(new ElementIdentity(composition.Epoch, level.ElementId));
            if (root is not null && root.IsAncestorOf(target))
                result = level.ElementId;
        }
        return result;
    }

    private static void Focus(
        string? requestedId,
        IReadOnlyDictionary<string, TargetRegistration> active
    )
    {
        if (requestedId is not null && active.TryGetValue(requestedId, out var requested))
        {
            requested.FocusTarget.Request();
            return;
        }
        Preferred(active)?.FocusTarget.Request();
    }

    private static TargetRegistration? Preferred(
        IReadOnlyDictionary<string, TargetRegistration> active
    ) =>
        active
            .Values.OrderBy(target => target.Kind)
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private void Prune(NavigationJournalSnapshot journal)
    {
        var live = journal.Entries.Select(entry => entry.EntryId).ToHashSet();
        foreach (
            var entryId in _entryStates.Keys.Where(entryId => !live.Contains(entryId)).ToArray()
        )
            _entryStates.Remove(entryId);
    }

    private static async Task Await(
        NavigationOperation operation,
        CancellationToken cancellationToken
    )
    {
        using var registration = cancellationToken.Register(() => Cancel(operation));
        _ = await operation.Completion.ConfigureAwait(false);
    }

    private static void Cancel(NavigationOperation operation)
    {
        if (operation.Completion.IsCompleted)
            return;
        try
        {
            operation.Cancel();
        }
        catch (ObjectDisposedException) when (operation.Completion.IsCompleted) { }
    }

    private static void ValidateCommandOrigin(NavigationOrigin origin)
    {
        if (!Enum.IsDefined(origin) || origin == NavigationOrigin.Redirect)
            throw new ArgumentOutOfRangeException(nameof(origin));
    }

    private void ValidateCommandOwner(ReactiveScope owner)
    {
        if (!ReferenceEquals(owner.Graph, _scope.Graph))
            throw new ArgumentException(
                "A navigation command and interaction must belong to the same reactive graph.",
                nameof(owner)
            );
    }

    private static void ValidateText(string value, string parameterName, int maximumBytes)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || Encoding.UTF8.GetByteCount(value) > maximumBytes
            || value.Any(char.IsControl)
        )
            throw new ArgumentException(
                "The value must be nonempty, bounded UTF-8 text without control characters.",
                parameterName
            );
    }

    private void RemoveTarget(TargetRegistration target)
    {
        target.Disposed -= RemoveTarget;
        _targets.Remove(target);
    }

    private void CheckLive()
    {
        _scope.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_disposed || _scope.IsDisposed, this);
    }

    private void DisposeFromOwnerScope()
    {
        if (_disposed)
            return;
        _disposed = true;
        _focusReconciliationDispatch?.Dispose();
        _focusReconciliationDispatch = null;
        _scheduledFocusReconciliation = null;
        _focusReconciliationEffect = null;
        foreach (var target in _targets.ToArray())
            target.Dispose();
        _targets.Clear();
        _entryStates.Clear();
        _boundary = null;
        _departure = null;
    }

    private sealed record BoundaryRegistration(
        BehaviorContext Context,
        Composition Composition,
        ElementIdentity Identity
    );

    private sealed record Departure(
        ElementIdentity? Focused,
        long? FocusedLevelId,
        IReadOnlySet<long> LevelIds,
        NavigationEntryInteractionState State
    );

    private sealed record FocusReconciliation(
        long Generation,
        long EntryId,
        RouteOutletSnapshot Outlet,
        string? RequestedTargetId
    );

    private sealed class TargetRegistration(
        string id,
        string label,
        NavigationTargetKind kind,
        FocusTarget focusTarget,
        ViewportState? viewport
    ) : IDisposable
    {
        private bool _disposed;

        internal string Id { get; } = id;
        internal string Label { get; } = label;
        internal NavigationTargetKind Kind { get; } = kind;
        internal FocusTarget FocusTarget { get; } = focusTarget;
        internal ViewportState? Viewport { get; } = viewport;
        internal ScrollOffset DefaultViewportOffset { get; } = viewport?.Offset ?? default;
        internal bool IsDisposed => _disposed;
        internal event Action<TargetRegistration>? Disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Disposed?.Invoke(this);
        }
    }
}

public static partial class Components
{
    /// <summary>Creates a labeled semantic boundary for one retained navigation outlet.</summary>
    [LucentComponent]
    public static ComponentRecipe NavigationBoundary(
        [DefaultContent] ComponentContent content,
        NavigationInteraction interaction,
        string label = "Navigation"
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(interaction);
        return ComponentRecipe.Create(
            "navigation-boundary",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    author: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.MainGrow, 1f)
                );
                root.AttachBehaviors(new NavigationBoundaryBehavior(interaction, label));
                context.Mount(
                    root,
                    ComponentContent.Create([Context.Provide(interaction, content)])
                );
            }
        );
    }

    /// <summary>
    /// Associates an existing control focus target and optional viewport with one stable authored route target.
    /// </summary>
    [LucentComponent]
    public static ComponentRecipe NavigationTarget(
        [DefaultContent] ComponentContent content,
        NavigationInteraction interaction,
        string id,
        string label,
        FocusTarget focusTarget,
        NavigationTargetKind kind = NavigationTargetKind.Useful,
        ViewportState? viewport = null
    )
    {
        content = Content(content);
        ArgumentNullException.ThrowIfNull(interaction);
        return ComponentRecipe.Create(
            "navigation-target",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    author: Style
                        .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                        .Set(LayoutProperties.MainGrow, 1f)
                );
                _ = interaction.RegisterTarget(root.Scope, id, label, kind, focusTarget, viewport);
                context.Mount(root, content);
            }
        );
    }
}

internal sealed class NavigationBoundaryBehavior(NavigationInteraction interaction, string label)
    : Behavior
{
    public override string Name => "navigation-boundary";

    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context) =>
        interaction.AttachBoundary(context, label);
}
