namespace Lucent.Core;

/// <summary>Creates transparent exact-type context providers for retained content.</summary>
public static class Context
{
    /// <summary>Provides one stable borrowed value to the supplied root recipe and its descendants.</summary>
    public static ComponentRecipe Provide<T>(T value, ComponentRecipe content) =>
        Provide(value, ContextProviderSource.Manual<T>(), content);

    /// <summary>Provides one stable borrowed value with generated source metadata.</summary>
    public static ComponentRecipe Provide<T>(
        T value,
        ContextProviderSource source,
        ComponentRecipe content
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        return content.WithContextProvider(value, source);
    }

    /// <summary>Provides one stable borrowed value to every contribution in the supplied content.</summary>
    public static ContentRecipe Provide<T>(T value, ComponentContent content) =>
        Provide(value, ContextProviderSource.Manual<T>(), content);

    /// <summary>Provides one stable borrowed value with generated source metadata to ordered content.</summary>
    public static ContentRecipe Provide<T>(
        T value,
        ContextProviderSource source,
        ComponentContent content
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        return new ContentRecipe(
            (context, parent) => context.MountProvided(parent, value, source, content)
        );
    }
}
