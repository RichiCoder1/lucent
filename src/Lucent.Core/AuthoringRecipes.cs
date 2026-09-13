namespace Lucent.Core;

/// <summary>Closed capability root for concise C# recipe authoring.</summary>
/// <remarks>The private protected constructor prevents consumers from inventing a marker that the framework cannot interpret.</remarks>
public abstract class AuthorCapability
{
    private protected AuthorCapability() { }
}

/// <summary>Marks an author recipe whose root accepts style contributions.</summary>
public sealed class StyledCapability : AuthorCapability
{
    internal StyledCapability() { }
}

/// <summary>Marks an author recipe whose root accepts accessibility metadata.</summary>
public sealed class AccessibleCapability : AuthorCapability
{
    internal AccessibleCapability() { }
}

/// <summary>Marks an author recipe whose root accepts style and accessibility contributions.</summary>
public sealed class StyledAccessibleCapability : AuthorCapability
{
    internal StyledAccessibleCapability() { }
}

/// <summary>Immutable authored values handed to an explicitly registered recipe target.</summary>
/// <remarks>
/// The target decides how these values merge with its behavior-owned presentation. No target is
/// selected by walking the recipe's descendants.
/// </remarks>
public readonly struct AuthorRecipeValues
{
    internal AuthorRecipeValues(
        Style? style,
        string? name,
        Func<string>? nameReader,
        string? description,
        Func<string>? descriptionReader
    )
    {
        Style = style;
        Name = name;
        NameReader = nameReader;
        Description = description;
        DescriptionReader = descriptionReader;
    }

    /// <summary>Gets the authored style, when one was supplied.</summary>
    public Style? Style { get; }

    /// <summary>Gets the authored fixed accessible name, when one was supplied.</summary>
    public string? Name { get; }

    /// <summary>Gets the authored live accessible-name reader, when one was supplied.</summary>
    public Func<string>? NameReader { get; }

    /// <summary>Gets the authored fixed accessible description, when one was supplied.</summary>
    public string? Description { get; }

    /// <summary>Gets the authored live accessible-description reader, when one was supplied.</summary>
    public Func<string>? DescriptionReader { get; }

    internal static AuthorRecipeValues From(AuthorRecipeContributions? contributions) =>
        new(
            contributions?.Style,
            contributions?.Aria?.Name,
            contributions?.Aria?.NameReader,
            contributions?.Aria?.Description,
            contributions?.Aria?.DescriptionReader
        );
}

/// <summary>Closed base for an explicit retained authoring target mapping.</summary>
public abstract class AuthorRecipeTarget
{
    private protected AuthorRecipeTarget() { }

    internal abstract void Apply(
        CompositionContext context,
        Element root,
        AuthorRecipeValues values
    );
}

/// <summary>Explicit mapping from one author capability to one retained presentation target.</summary>
/// <typeparam name="TCapability">The closed capability marker accepted by the target.</typeparam>
/// <remarks>
/// Targets are created by <see cref="AuthorRecipe.Target{TCapability}"/> and are required by
/// author recipe factories. A target callback must apply the supplied values to its declared root
/// before mounting that root's content.
/// </remarks>
public sealed class AuthorRecipeTarget<TCapability> : AuthorRecipeTarget
    where TCapability : AuthorCapability
{
    private readonly Action<CompositionContext, Element, AuthorRecipeValues> _apply;

    internal AuthorRecipeTarget(Action<CompositionContext, Element, AuthorRecipeValues> apply)
    {
        AuthorCapabilityRules.Validate<TCapability>();
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    internal override void Apply(
        CompositionContext context,
        Element root,
        AuthorRecipeValues values
    ) => _apply(context, root, values);
}

/// <summary>A single immutable capability-bearing view over one retained component recipe.</summary>
/// <typeparam name="TCapability">One of the closed built-in authoring capability markers.</typeparam>
/// <remarks>
/// The wrapper carries author contributions without adding a retained element. Conversion returns the
/// underlying recipe, so existing mount, deferred, and content ownership remain authoritative.
/// </remarks>
public readonly struct AuthorRecipe<TCapability>
    where TCapability : AuthorCapability
{
    private readonly ComponentRecipe? _recipe;
    private readonly AuthorRecipeTarget<TCapability>? _target;
    private readonly AuthorRecipeContributions? _contributions;

    internal AuthorRecipe(
        ComponentRecipe recipe,
        AuthorRecipeTarget<TCapability> target,
        AuthorRecipeContributions? contributions
    )
    {
        _recipe = recipe;
        _target = target;
        _contributions = contributions;
    }

    /// <summary>Gets the erased retained recipe represented by this wrapper.</summary>
    public ComponentRecipe Recipe => ToComponentRecipe();

    /// <summary>Gets the diagnostic kind of the represented retained recipe.</summary>
    public string Kind => ToComponentRecipe().Kind;

    /// <summary>Gets whether this value contains a valid recipe and a supported capability marker.</summary>
    public bool IsValid =>
        _recipe is not null
        && _target is not null
        && AuthorCapabilityRules.IsSupported<TCapability>();

    /// <summary>Returns a capability-bearing recipe with an explicit local diagnostic name.</summary>
    public AuthorRecipe<TCapability> Named(string name)
    {
        var recipe = ToComponentRecipe();
        return new(recipe.Named(name), _target!, _contributions);
    }

    /// <summary>Returns this recipe after a terminal accessibility metadata group.</summary>
    public AuthorRecipe<TCapability> End => this;

    /// <summary>Converts this wrapper to the existing one-root recipe contract.</summary>
    public static implicit operator ComponentRecipe(AuthorRecipe<TCapability> recipe) =>
        recipe.ToComponentRecipe();

    /// <summary>Converts this wrapper to one content contribution without adding a wrapper element.</summary>
    public static implicit operator ContentRecipe(AuthorRecipe<TCapability> recipe) =>
        (ContentRecipe)recipe.ToComponentRecipe();

    internal AuthorRecipe<TCapability> WithStyle(Style style)
    {
        var recipe = ToComponentRecipe();
        AuthorCapabilityRules.RequireStyled<TCapability>();
        ArgumentNullException.ThrowIfNull(style);
        return new(recipe, _target!, AuthorRecipeContributions.WithStyle(_contributions, style));
    }

    internal AuthorRecipe<TCapability> WithAria(AuthorAriaState aria)
    {
        var recipe = ToComponentRecipe();
        AuthorCapabilityRules.RequireAccessible<TCapability>();
        ArgumentNullException.ThrowIfNull(aria);
        return new(recipe, _target!, AuthorRecipeContributions.WithAria(_contributions, aria));
    }

    internal AuthorAria<TCapability> BeginAria()
    {
        ToComponentRecipe();
        AuthorCapabilityRules.RequireAccessible<TCapability>();
        return new(this, _contributions?.Aria);
    }

    private ComponentRecipe ToComponentRecipe()
    {
        var recipe =
            _recipe
            ?? throw new InvalidOperationException(
                "An author recipe must contain a retained recipe."
            );
        AuthorCapabilityRules.Validate<TCapability>();
        var authored =
            _contributions is null || ReferenceEquals(_contributions, recipe.Authoring)
                ? recipe
                : recipe.WithAuthoring(_contributions);
        return _target is null
            ? throw new InvalidOperationException(
                "An author recipe must declare an explicit retained target."
            )
            : authored.WithAuthoringTarget(_target);
    }
}

/// <summary>Static construction entry points for capability-bearing retained recipes.</summary>
public static class AuthorRecipe
{
    /// <summary>Creates an explicit mapping from a capability to one retained root target.</summary>
    public static AuthorRecipeTarget<TCapability> Target<TCapability>(
        Action<CompositionContext, Element, AuthorRecipeValues> apply
    )
        where TCapability : AuthorCapability => new(apply);

    /// <summary>Creates one retained root with an explicitly registered target.</summary>
    public static AuthorRecipe<TCapability> Create<TCapability>(
        string kind,
        AuthorRecipeTarget<TCapability> target
    )
        where TCapability : AuthorCapability
    {
        ReactiveGraph.ValidateName(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(target);
        AuthorCapabilityRules.Validate<TCapability>();
        var recipe = ComponentRecipe.Create(kind, static (_, _) => { });
        return new(recipe.WithAuthoringTarget(target), target, recipe.Authoring);
    }

    /// <summary>Creates a deferred recipe whose explicitly typed factory runs once per mount.</summary>
    public static AuthorRecipe<TCapability> Defer<TCapability>(
        string kind,
        AuthorRecipeTarget<TCapability> target,
        Func<ReactiveScope, AuthorRecipe<TCapability>> build
    )
        where TCapability : AuthorCapability
    {
        ReactiveGraph.ValidateName(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(build);
        AuthorCapabilityRules.Validate<TCapability>();
        return new(
            ComponentRecipe.Defer(
                kind,
                scope =>
                {
                    var authored = build(scope);
                    return authored.Recipe;
                }
            ),
            target,
            null
        );
    }
}

/// <summary>Immutable accessibility metadata grouped before terminating an author recipe expression.</summary>
/// <typeparam name="TCapability">The capability marker of the source author recipe.</typeparam>
public readonly struct AuthorAria<TCapability>
    where TCapability : AuthorCapability
{
    private readonly AuthorRecipe<TCapability> _recipe;
    private readonly AuthorAriaState? _state;

    internal AuthorAria(AuthorRecipe<TCapability> recipe, AuthorAriaState? state)
    {
        _recipe = recipe;
        _state = state;
    }

    /// <summary>Adds a fixed accessible name.</summary>
    public AuthorAria<TCapability> Name(string name)
    {
        AuthorCapabilityRules.RequireAccessible<TCapability>();
        name = AuthorText.Required(name, nameof(name));
        return new(_recipe, AuthorAriaState.WithName(_state, name));
    }

    /// <summary>Adds an accessible name read while the retained recipe is active.</summary>
    public AuthorAria<TCapability> Name(Func<string> read)
    {
        AuthorCapabilityRules.RequireAccessible<TCapability>();
        ArgumentNullException.ThrowIfNull(read);
        return new(_recipe, AuthorAriaState.WithNameReader(_state, read));
    }

    /// <summary>Adds a fixed accessible description.</summary>
    public AuthorAria<TCapability> Description(string description)
    {
        AuthorCapabilityRules.RequireAccessible<TCapability>();
        description = AuthorText.Required(description, nameof(description));
        return new(_recipe, AuthorAriaState.WithDescription(_state, description));
    }

    /// <summary>Adds an accessible description read while the retained recipe is active.</summary>
    public AuthorAria<TCapability> Description(Func<string> read)
    {
        AuthorCapabilityRules.RequireAccessible<TCapability>();
        ArgumentNullException.ThrowIfNull(read);
        return new(_recipe, AuthorAriaState.WithDescriptionReader(_state, read));
    }

    /// <summary>Returns the original capability-bearing recipe with this immutable metadata group.</summary>
    public AuthorRecipe<TCapability> End =>
        _recipe.WithAria(
            _state
                ?? throw new InvalidOperationException(
                    "An accessibility metadata group must contain a contribution."
                )
        );

    /// <summary>Converts this metadata group to its one-root recipe.</summary>
    public static implicit operator ComponentRecipe(AuthorAria<TCapability> aria) =>
        aria.End.Recipe;

    /// <summary>Converts this metadata group to one content contribution.</summary>
    public static implicit operator ContentRecipe(AuthorAria<TCapability> aria) =>
        (ContentRecipe)aria.End.Recipe;
}

/// <summary>Typed authoring extensions for the styled capability marker.</summary>
public static class StyledAuthorRecipeExtensions
{
    extension(AuthorRecipe<StyledCapability> recipe)
    {
        /// <summary>Adds immutable style contributions to a styled author recipe.</summary>
        public AuthorRecipe<StyledCapability> Style(Style style) => recipe.WithStyle(style);
    }
}

/// <summary>Typed authoring extensions for the accessible capability marker.</summary>
public static class AccessibleAuthorRecipeExtensions
{
    extension(AuthorRecipe<AccessibleCapability> recipe)
    {
        /// <summary>Begins immutable accessibility metadata for an accessible author recipe.</summary>
        public AuthorAria<AccessibleCapability> Aria => recipe.BeginAria();
    }
}

/// <summary>Typed authoring extensions for the combined capability marker.</summary>
public static class StyledAccessibleAuthorRecipeExtensions
{
    extension(AuthorRecipe<StyledAccessibleCapability> recipe)
    {
        /// <summary>Adds immutable style contributions to a styled and accessible author recipe.</summary>
        public AuthorRecipe<StyledAccessibleCapability> Style(Style style) =>
            recipe.WithStyle(style);

        /// <summary>Begins immutable accessibility metadata for a styled and accessible author recipe.</summary>
        public AuthorAria<StyledAccessibleCapability> Aria => recipe.BeginAria();
    }
}

internal static class AuthorCapabilityRules
{
    internal static bool IsSupported<TCapability>()
        where TCapability : AuthorCapability =>
        typeof(TCapability) == typeof(StyledCapability)
        || typeof(TCapability) == typeof(AccessibleCapability)
        || typeof(TCapability) == typeof(StyledAccessibleCapability);

    internal static void Validate<TCapability>()
        where TCapability : AuthorCapability
    {
        if (!IsSupported<TCapability>())
            throw new ArgumentException(
                "The author recipe capability is not one of Lucent's closed built-in markers.",
                nameof(TCapability)
            );
    }

    internal static void RequireStyled<TCapability>()
        where TCapability : AuthorCapability
    {
        Validate<TCapability>();
        if (typeof(TCapability) == typeof(AccessibleCapability))
            throw new InvalidOperationException(
                "This author recipe does not expose a style target."
            );
    }

    internal static void RequireAccessible<TCapability>()
        where TCapability : AuthorCapability
    {
        Validate<TCapability>();
        if (typeof(TCapability) == typeof(StyledCapability))
            throw new InvalidOperationException(
                "This author recipe does not expose an accessibility target."
            );
    }
}

internal static class AuthorText
{
    internal static string Required(string value, string parameterName)
    {
        if (String.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "An author accessibility value is required.",
                parameterName
            );
        return value;
    }
}

internal sealed class AuthorRecipeContributions
{
    private AuthorRecipeContributions(Style? style, AuthorAriaState? aria)
    {
        Style = style;
        Aria = aria;
    }

    internal Style? Style { get; }
    internal AuthorAriaState? Aria { get; }

    internal static AuthorRecipeContributions WithStyle(
        AuthorRecipeContributions? existing,
        Style style
    ) => new(existing?.Style?.With(style) ?? style, existing?.Aria);

    internal static AuthorRecipeContributions WithAria(
        AuthorRecipeContributions? existing,
        AuthorAriaState aria
    ) => new(existing?.Style, AuthorAriaState.Merge(existing?.Aria, aria));
}

internal sealed class AuthorAriaState
{
    private AuthorAriaState(
        string? name,
        Func<string>? nameReader,
        string? description,
        Func<string>? descriptionReader
    )
    {
        Name = name;
        NameReader = nameReader;
        Description = description;
        DescriptionReader = descriptionReader;
    }

    internal string? Name { get; }
    internal Func<string>? NameReader { get; }
    internal string? Description { get; }
    internal Func<string>? DescriptionReader { get; }

    internal static AuthorAriaState WithName(AuthorAriaState? state, string name) =>
        new(name, null, state?.Description, state?.DescriptionReader);

    internal static AuthorAriaState WithNameReader(AuthorAriaState? state, Func<string> read) =>
        new(null, read, state?.Description, state?.DescriptionReader);

    internal static AuthorAriaState WithDescription(AuthorAriaState? state, string description) =>
        new(state?.Name, state?.NameReader, description, null);

    internal static AuthorAriaState WithDescriptionReader(
        AuthorAriaState? state,
        Func<string> read
    ) => new(state?.Name, state?.NameReader, null, read);

    internal static AuthorAriaState Merge(AuthorAriaState? existing, AuthorAriaState next) =>
        MergeFields(existing, next);

    private static AuthorAriaState MergeFields(AuthorAriaState? existing, AuthorAriaState next)
    {
        var name = existing?.Name;
        var nameReader = existing?.NameReader;
        if (next.Name is not null)
        {
            name = next.Name;
            nameReader = null;
        }
        else if (next.NameReader is not null)
        {
            name = null;
            nameReader = next.NameReader;
        }

        var description = existing?.Description;
        var descriptionReader = existing?.DescriptionReader;
        if (next.Description is not null)
        {
            description = next.Description;
            descriptionReader = null;
        }
        else if (next.DescriptionReader is not null)
        {
            description = null;
            descriptionReader = next.DescriptionReader;
        }

        return new(name, nameReader, description, descriptionReader);
    }
}
