namespace Lucent.Core;

/// <summary>An application-owned asynchronous action with reactive availability and completion state.</summary>
/// <remarks>
/// The supplied <see cref="ReactiveScope"/> owns cancellation and completion delivery. A command accepts
/// at most one execution at a time; failures are exposed through <see cref="Error"/> on the graph owner
/// instead of escaping a platform input callback.
/// </remarks>
public sealed class ApplicationCommand : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<long> _generation;
    private readonly Derived<bool> _enabled;
    private readonly AsyncValue<bool> _execution;
    private readonly Signal<bool> _started;

    /// <summary>Creates a command whose asynchronous work and cancellation belong to <paramref name="owner"/>.</summary>
    public ApplicationCommand(
        ReactiveScope owner,
        Func<CancellationToken, Task> execute,
        Func<bool>? enabled = null,
        string name = "application-command"
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(execute);
        ReactiveGraph.ValidateName(name, nameof(name));
        _scope = owner.CreateChild(name);
        try
        {
            _generation = _scope.Signal(0L, name + ".generation");
            _started = _scope.Signal(false, name + ".started");
            _enabled = _scope.Derived(() => enabled?.Invoke() ?? true, name + ".enabled");
            _execution = _scope.Async(
                async cancellation =>
                {
                    _ = _generation.Value;
                    await execute(cancellation).ConfigureAwait(false);
                    return true;
                },
                false,
                name + ".execution"
            );
        }
        catch
        {
            _scope.Dispose();
            throw;
        }
    }

    /// <summary>Gets whether a new execution can be accepted now.</summary>
    public bool IsEnabled => _enabled.Value && !IsBusy;

    /// <summary>Gets whether the accepted execution is still pending.</summary>
    public bool IsBusy => _started.Value && _execution.IsPending;

    /// <summary>Gets the most recent execution failure after the owner drains posted completion work.</summary>
    public Exception? Error => _started.Value ? _execution.Error : null;

    /// <summary>
    /// Starts one execution when enabled and idle. Returns false without starting work when disabled or busy.
    /// </summary>
    public bool TryExecute()
    {
        if (!IsEnabled)
            return false;
        checked
        {
            _generation.Value++;
        }
        _started.Value = true;
        _ = _execution.IsPending;
        return true;
    }

    /// <summary>Cancels pending work and releases command state from its owning graph.</summary>
    public void Dispose() => _scope.Dispose();
}

/// <summary>A portable exact-match keyboard gesture.</summary>
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers)
{
    /// <summary>Creates a Control-modified gesture.</summary>
    public static KeyChord Ctrl(Key key) => new(key, KeyModifiers.Control);

    /// <summary>Creates a platform-Meta-modified gesture.</summary>
    public static KeyChord Meta(Key key) => new(key, KeyModifiers.Meta);

    internal void Validate()
    {
        new KeyCommand(KeyCommandKind.Down, Key, Modifiers).Validate();
    }

    internal bool Matches(KeyCommand command) =>
        command.Kind == KeyCommandKind.Down
        && !command.IsRepeat
        && command.Key == Key
        && command.Modifiers == Modifiers;
}

/// <summary>Associates one exact keyboard gesture with an application command.</summary>
public readonly record struct CommandBinding(ApplicationCommand Command, KeyChord Chord)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Command);
        Chord.Validate();
    }
}

/// <summary>An immutable command table installed by a command scope.</summary>
public sealed class CommandBindings : IReadOnlyList<CommandBinding>
{
    private readonly CommandBinding[] _bindings;

    /// <summary>Snapshots bindings and rejects ambiguous duplicate gestures.</summary>
    public CommandBindings(IEnumerable<CommandBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings.ToArray();
        foreach (var binding in _bindings)
            binding.Validate();
        var duplicate = _bindings
            .GroupBy(binding => binding.Chord)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                "A command scope cannot bind the same key chord more than once.",
                nameof(bindings)
            );
    }

    /// <summary>An empty command table.</summary>
    public static CommandBindings Empty { get; } = new([]);

    /// <summary>Gets the number of bindings.</summary>
    public int Count => _bindings.Length;

    /// <summary>Gets a binding by declaration order.</summary>
    public CommandBinding this[int index] => _bindings[index];

    /// <summary>Enumerates bindings in declaration order.</summary>
    public IEnumerator<CommandBinding> GetEnumerator() =>
        ((IEnumerable<CommandBinding>)_bindings).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

/// <summary>Key behavior installed on a component subtree; nearest matching scopes consume their chord.</summary>
internal sealed class CommandScopeBehavior(CommandBindings bindings) : Behavior
{
    public override string Name => "command-scope";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        context.SetSemantics(new(SemanticRole.Group, "Command scope"));
        context.OnKey(route =>
        {
            foreach (var binding in bindings)
            {
                if (!binding.Chord.Matches(route.Command))
                    continue;
                _ = binding.Command.TryExecute();
                // A declared nearest binding owns its chord even while disabled or busy.
                route.Handled = true;
                return;
            }
        });
    }
}
