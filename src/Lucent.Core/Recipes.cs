using System.Collections;
using System.Runtime.CompilerServices;

namespace Lucent.Core;

/// <summary>A reusable capability that creates one retained root when mounted.</summary>
/// <remarks>Each mount owns exactly one stable root. A recipe is neither a runtime template instance nor a rerender function; use <see cref="ContentRecipe"/> when contributing below an existing root.</remarks>
public sealed class ComponentRecipe
{
    private readonly Action<CompositionContext, Element> _content;
    private readonly string? _name;

    private ComponentRecipe(
        string kind,
        Action<CompositionContext, Element> content,
        string? name = null
    )
    {
        Kind = kind;
        _content = content;
        _name = name;
    }

    /// <summary>The diagnostic kind used by unnamed mounts.</summary>
    public string Kind { get; }

    /// <summary>Creates a recipe whose root is allocated by the framework.</summary>
    public static ComponentRecipe Create(string kind, Action<CompositionContext, Element> content)
    {
        ReactiveGraph.ValidateName(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(content);
        return new ComponentRecipe(kind, content);
    }

    /// <summary>Returns this recipe with an explicit local diagnostic name.</summary>
    public ComponentRecipe Named(string name)
    {
        ReactiveGraph.ValidateName(name, nameof(name));
        return new ComponentRecipe(Kind, _content, name);
    }

    /// <summary>Converts one root recipe into one content contribution.</summary>
    public static implicit operator ContentRecipe(ComponentRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new ContentRecipe((context, parent) => context.Mount(parent, recipe));
    }

    internal Element Mount(CompositionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = context.RecipeElement(Kind, _name);
        _content(context, root);
        return root;
    }
}

/// <summary>A reusable contribution of zero or more retained entries below an existing root.</summary>
/// <remarks>Content recipes are immutable. A <see cref="ComponentContent"/> group commits them in declaration order without adding a wrapper element.</remarks>
public sealed class ContentRecipe
{
    private readonly Action<CompositionContext, Element> _mount;

    internal ContentRecipe(Action<CompositionContext, Element> mount) => _mount = mount;

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

    internal void Mount(CompositionContext context, Element parent) => _mount(context, parent);
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
