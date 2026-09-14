using System.Collections;
using System.Runtime.CompilerServices;

namespace Lucent.Core;

/// <summary>A reusable capability that creates one retained root when mounted.</summary>
/// <remarks>Each mount owns exactly one stable root. A recipe is neither a runtime template instance nor a rerender function; use <see cref="ContentRecipe"/> when contributing below an existing root.</remarks>
public sealed class ComponentRecipe
{
    private readonly Action<MountContext, Element> _content;
    private readonly DeferredRecipe? _deferred;
    private readonly ContextProvider? _provider;
    private readonly ComponentServiceBinding? _serviceBinding;
    private readonly ComponentRecipe? _wrapped;
    private readonly string? _name;
    private readonly AuthorRecipeContributions? _authoring;
    private readonly AuthorRecipeTarget? _authoringTarget;

    private ComponentRecipe(
        string kind,
        Action<MountContext, Element> content,
        string? name = null,
        DeferredRecipe? deferred = null,
        ContextProvider? provider = null,
        ComponentServiceBinding? serviceBinding = null,
        ComponentRecipe? wrapped = null,
        AuthorRecipeContributions? authoring = null,
        AuthorRecipeTarget? authoringTarget = null
    )
    {
        Kind = kind;
        _content = content;
        _name = name;
        _deferred = deferred;
        _provider = provider;
        _serviceBinding = serviceBinding;
        _wrapped = wrapped;
        _authoring = authoring;
        _authoringTarget = authoringTarget;
    }

    private ComponentRecipe(string kind, Func<ReactiveScope, ComponentRecipe> deferred)
        : this(kind, static (_, _) => { }, deferred: new PlainDeferredRecipe(deferred)) { }

    private ComponentRecipe(string kind, DeferredRecipe deferred)
        : this(kind, static (_, _) => { }, deferred: deferred) { }

    /// <summary>The diagnostic kind used by unnamed mounts.</summary>
    public string Kind { get; }

    /// <summary>Creates a recipe whose root is allocated by the framework.</summary>
    public static ComponentRecipe Create(string kind, Action<MountContext, Element> content)
    {
        ReactiveGraph.ValidateName(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(content);
        return new ComponentRecipe(kind, content);
    }

    /// <summary>Creates a recipe whose setup and authored recipe construction run once for each mount.</summary>
    /// <remarks>The returned recipe is applied to this recipe's single root; deferred composition never adds a wrapper element.</remarks>
    public static ComponentRecipe Defer(string kind, Func<ReactiveScope, ComponentRecipe> build)
    {
        ReactiveGraph.ValidateName(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(build);
        return new ComponentRecipe(kind, build);
    }

    /// <summary>Creates a recipe whose declared values resolve at each mount before setup begins.</summary>
    public static ComponentRecipe Defer<TValues>(
        string kind,
        ComponentRequirementPlan<TValues> requirements,
        Func<ReactiveScope, TValues, ComponentRecipe> build
    )
    {
        ReactiveGraph.ValidateName(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(build);
        return new ComponentRecipe(kind, new RequiredDeferredRecipe<TValues>(requirements, build));
    }

    /// <summary>Returns this recipe with an explicit local diagnostic name.</summary>
    public ComponentRecipe Named(string name)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        return Copy(name: name, replaceName: true);
    }

    /// <summary>Converts one root recipe into one content contribution.</summary>
    public static implicit operator ContentRecipe(ComponentRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new ContentRecipe((context, parent) => context.Mount(parent, recipe));
    }

    internal AuthorRecipeContributions? Authoring => _authoring;

    internal ComponentRecipe WithAuthoring(AuthorRecipeContributions authoring)
    {
        ArgumentNullException.ThrowIfNull(authoring);
        if (ReferenceEquals(authoring, _authoring))
            return this;
        return Copy(authoring: authoring, replaceAuthoring: true);
    }

    internal ComponentRecipe WithAuthoringTarget(AuthorRecipeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_authoringTarget is not null && !ReferenceEquals(target, _authoringTarget))
            throw new InvalidOperationException(
                "A retained recipe cannot map two different authoring targets."
            );
        if (ReferenceEquals(target, _authoringTarget))
            return this;
        return Copy(authoringTarget: target, replaceAuthoringTarget: true);
    }

    internal ComponentRecipe WithContextProvider<T>(T value, ContextProviderSource source) =>
        new(
            Kind,
            static (_, _) => { },
            provider: new ContextProvider<T>(value, source),
            wrapped: this
        );

    internal ComponentRecipe WithServiceBinding(ComponentServiceBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new ComponentRecipe(
            Kind,
            static (_, _) => { },
            serviceBinding: binding,
            wrapped: this
        );
    }

    internal Element Mount(MountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Environment.CheckMountAdmission();
        if (_deferred is null && _provider is null && _serviceBinding is null)
        {
            var directRoot = context.RecipeElement(Kind, _name);
            directRoot.SetMountEnvironment(context.Environment, []);
            ApplyAuthoring(context, directRoot);
            _content(context, directRoot);
            return directRoot;
        }
        var environment = context.Environment;
        ReactiveScope? owner = null;
        string? preferredName = null;
        var seen = new HashSet<ComponentRecipe>();
        var layers = new List<ComponentRecipe>();
        List<ContextRequirementDiagnostic>? requirements = null;
        var current = this;
        while (true)
        {
            if (!seen.Add(current))
                throw new InvalidOperationException(
                    "A deferred or transparent recipe returned a recursive recipe."
                );
            layers.Add(current);
            preferredName ??= current._name;
            if (current._serviceBinding is not null)
            {
                environment = environment.Attach(current._serviceBinding, context.Parent);
                current = current._wrapped!;
                continue;
            }
            if (current._provider is not null)
            {
                environment = current._provider.Apply(environment);
                current = current._wrapped!;
                continue;
            }
            if (current._deferred is not null)
            {
                var described = current._deferred.DescribeRequirements();
                if (described.Length != 0)
                    (requirements ??= []).AddRange(described);
                current = current._deferred.Build(
                    context,
                    environment,
                    current.Kind,
                    current._name,
                    ref owner
                );
                ArgumentNullException.ThrowIfNull(current);
                continue;
            }
            break;
        }

        var resolved = current;
        var authoring = resolved._authoring;
        var target = resolved._authoringTarget;
        for (var index = layers.Count - 2; index >= 0; index--)
        {
            var layer = layers[index];
            authoring = MergeAuthoring(authoring, layer._authoring);
            target = MergeAuthoringTargets(target, layer._authoringTarget);
        }
        if (authoring is not null && !ReferenceEquals(authoring, resolved._authoring))
            resolved = resolved.WithAuthoring(authoring);
        if (target is not null && !ReferenceEquals(target, resolved._authoringTarget))
            resolved = resolved.WithAuthoringTarget(target);
        var priorEnvironment = context.EnterEnvironment(environment);
        try
        {
            var root = context.RecipeElement(resolved.Kind, preferredName ?? resolved._name, owner);
            root.SetMountEnvironment(environment, requirements is null ? [] : [.. requirements]);
            resolved.ApplyAuthoring(context, root);
            resolved.Apply(context, root);
            return root;
        }
        finally
        {
            context.RestoreEnvironment(priorEnvironment);
        }
    }

    private static AuthorRecipeContributions? MergeAuthoring(
        AuthorRecipeContributions? existing,
        AuthorRecipeContributions? next
    )
    {
        if (next is null)
            return existing;
        if (existing is null)
            return next;
        if (ReferenceEquals(existing, next))
            return existing;
        var merged = existing;
        if (next.Style is not null)
            merged = AuthorRecipeContributions.WithStyle(merged, next.Style);
        if (next.Aria is not null)
            merged = AuthorRecipeContributions.WithAria(merged, next.Aria);
        return merged;
    }

    private static AuthorRecipeTarget? MergeAuthoringTargets(
        AuthorRecipeTarget? existing,
        AuthorRecipeTarget? next
    )
    {
        if (next is null)
            return existing;
        if (existing is null || ReferenceEquals(existing, next))
            return next;
        throw new InvalidOperationException(
            "A retained recipe cannot map two different authoring targets."
        );
    }

    private ComponentRecipe Copy(
        string? name = null,
        bool replaceName = false,
        AuthorRecipeContributions? authoring = null,
        bool replaceAuthoring = false,
        AuthorRecipeTarget? authoringTarget = null,
        bool replaceAuthoringTarget = false
    ) =>
        new(
            Kind,
            _content,
            replaceName ? name : _name,
            _deferred,
            _provider,
            _serviceBinding,
            _wrapped,
            replaceAuthoring ? authoring : _authoring,
            replaceAuthoringTarget ? authoringTarget : _authoringTarget
        );

    private void ApplyAuthoring(MountContext context, Element root)
    {
        _authoringTarget?.Apply(context, root, AuthorRecipeValues.From(_authoring));
    }

    internal void ApplyToRoot(MountContext context, Element root)
    {
        ApplyAuthoring(context, root);
        Apply(context, root);
    }

    internal void Apply(MountContext context, Element root)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);
        if (_deferred is not null || _provider is not null || _serviceBinding is not null)
            throw new InvalidOperationException(
                "Deferred and transparent recipes must be resolved before mounting."
            );
        _content(context, root);
    }

    private abstract class DeferredRecipe
    {
        internal virtual ContextRequirementDiagnostic[] DescribeRequirements() => [];

        internal abstract ComponentRecipe Build(
            MountContext context,
            MountEnvironment environment,
            string kind,
            string? name,
            ref ReactiveScope? owner
        );

        protected static ReactiveScope Owner(
            MountContext context,
            string kind,
            string? name,
            ref ReactiveScope? owner
        ) => owner ??= context.BeginDeferredScope(kind, name);
    }

    private sealed class PlainDeferredRecipe(Func<ReactiveScope, ComponentRecipe> build)
        : DeferredRecipe
    {
        internal override ComponentRecipe Build(
            MountContext context,
            MountEnvironment environment,
            string kind,
            string? name,
            ref ReactiveScope? owner
        )
        {
            var mountOwner = Owner(context, kind, name, ref owner);
            return context.RunDeferred(mountOwner, () => build(mountOwner));
        }
    }

    private sealed class RequiredDeferredRecipe<TValues>(
        ComponentRequirementPlan<TValues> requirements,
        Func<ReactiveScope, TValues, ComponentRecipe> build
    ) : DeferredRecipe
    {
        internal override ContextRequirementDiagnostic[] DescribeRequirements() =>
            requirements.Describe();

        internal override ComponentRecipe Build(
            MountContext context,
            MountEnvironment environment,
            string kind,
            string? name,
            ref ReactiveScope? owner
        )
        {
            var values = requirements.Resolve(environment, context.Parent);
            var mountOwner = Owner(context, kind, name, ref owner);
            return context.RunDeferred(mountOwner, () => build(mountOwner, values));
        }
    }

    private abstract class ContextProvider
    {
        internal abstract MountEnvironment Apply(MountEnvironment environment);
    }

    private sealed class ContextProvider<T>(T value, ContextProviderSource source) : ContextProvider
    {
        internal override MountEnvironment Apply(MountEnvironment environment) =>
            environment.Provide(value, source);
    }
}

/// <summary>A reusable contribution of zero or more retained entries below an existing root.</summary>
/// <remarks>Content recipes are immutable. A <see cref="ComponentContent"/> group commits them in declaration order without adding a wrapper element.</remarks>
public sealed class ContentRecipe
{
    private readonly Action<MountContext, Element> _mount;

    internal ContentRecipe(Action<MountContext, Element> mount) => _mount = mount;

    /// <summary>Creates a retained conditional contribution.</summary>
    public static ContentRecipe When(string name, Func<bool> active, ComponentRecipe content)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(content);
        return new ContentRecipe(
            (context, parent) => _ = context.When(parent, name, active, content.Mount)
        );
    }

    /// <summary>Creates one retained branch selected by one reactive evaluation.</summary>
    public static ContentRecipe Switch(string name, Func<ConditionalChoice> select)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(select);
        return new ContentRecipe((context, parent) => _ = context.Switch(parent, name, select));
    }

    /// <summary>Creates a retained keyed contribution.</summary>
    public static ContentRecipe ForEach<TKey, TItem>(
        string name,
        Func<IEnumerable<TItem>> source,
        Func<TItem, TKey> key,
        Func<CurrentItem<TItem>, ComponentRecipe> content
    )
        where TKey : notnull
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        return new ContentRecipe(
            (context, parent) =>
                _ = context.ForEach(
                    parent,
                    name,
                    source,
                    key,
                    (item, child) =>
                    {
                        var recipe = content(item);
                        ArgumentNullException.ThrowIfNull(recipe);
                        return recipe.Mount(child);
                    }
                )
        );
    }

    internal void Mount(MountContext context, Element parent) => _mount(context, parent);
}

/// <summary>The selected retained conditional branch and its recipe.</summary>
public readonly record struct ConditionalChoice(int Branch, ComponentRecipe? Recipe)
{
    internal ConditionalPayload? Payload { get; init; }

    /// <summary>Creates a retained branch whose mounted recipe can read its current payload.</summary>
    public static ConditionalChoice Create<T>(
        int branch,
        T value,
        Func<CurrentItem<T>, ComponentRecipe> factory
    )
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ConditionalChoice(branch, null)
        {
            Payload = new ConditionalPayload<T>(value, factory),
        };
    }
}

internal abstract class ConditionalPayload
{
    internal abstract Type ValueType { get; }

    internal abstract MountedConditionalPayload Mount(ReactiveScope owner, string name);
}

internal sealed class ConditionalPayload<T>(T value, Func<CurrentItem<T>, ComponentRecipe> factory)
    : ConditionalPayload
{
    internal T Value { get; } = value;
    internal override Type ValueType => typeof(T);

    internal override MountedConditionalPayload Mount(ReactiveScope owner, string name)
    {
        var scope = owner.CreateChild(name);
        try
        {
            var current = scope.CurrentItemForFramework(Value, name + ".value");
            var recipe = owner.Graph.Untracked(() => factory(current));
            ArgumentNullException.ThrowIfNull(recipe);
            return new MountedConditionalPayload<T>(scope, current, recipe);
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            try
            {
                scope.Dispose();
            }
            catch (Exception cleanup)
            {
                errors.Add(cleanup);
            }
            Composition.ThrowAll(errors, "Conditional payload factory failed.");
            throw;
        }
    }
}

internal abstract class MountedConditionalPayload : IDisposable
{
    internal abstract Type ValueType { get; }
    internal abstract ComponentRecipe Recipe { get; }
    internal abstract void Stage(ConditionalPayload payload);
    internal abstract void Notify();
    public abstract void Dispose();
}

internal sealed class MountedConditionalPayload<T>(
    ReactiveScope scope,
    CurrentItem<T> current,
    ComponentRecipe recipe
) : MountedConditionalPayload
{
    internal override Type ValueType => typeof(T);
    internal override ComponentRecipe Recipe { get; } = recipe;

    internal override void Stage(ConditionalPayload payload)
    {
        if (payload is not ConditionalPayload<T> typed)
            throw new InvalidOperationException(
                "A retained conditional branch cannot change its current payload type."
            );
        current.Stage(typed.Value);
    }

    internal override void Notify() => current.Notify();

    public override void Dispose() => scope.Dispose();
}

/// <summary>An immutable ordered group of content contributions.</summary>
[CollectionBuilder(typeof(ComponentContent), nameof(Create))]
public sealed class ComponentContent : IReadOnlyList<ContentRecipe>
{
    private readonly ContentRecipe[] _recipes;

    private ComponentContent(ContentRecipe[] recipes) => _recipes = recipes;

    /// <summary>An empty content group.</summary>
    public static ComponentContent Empty { get; } = new([]);

    /// <summary>Gets the number of ordered content contributions.</summary>
    public int Count => _recipes.Length;

    /// <summary>Gets the content contribution at the specified declaration-order index.</summary>
    public ContentRecipe this[int index] => _recipes[index];

    /// <summary>Snapshots ordered content for C# collection expressions.</summary>
    public static ComponentContent Create(ReadOnlySpan<ContentRecipe> recipes)
    {
        foreach (var recipe in recipes)
            ArgumentNullException.ThrowIfNull(recipe);
        return recipes.Length == 0 ? Empty : new ComponentContent(recipes.ToArray());
    }

    /// <summary>Returns an enumerator over content contributions in declaration order.</summary>
    public IEnumerator<ContentRecipe> GetEnumerator() =>
        ((IEnumerable<ContentRecipe>)_recipes).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
