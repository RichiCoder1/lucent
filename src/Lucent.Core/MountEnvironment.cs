namespace Lucent.Core;

internal sealed class MountEnvironment
{
    private readonly ContextFrame? _contexts;
    private readonly ThemeContext? _theme;
    private ContextProviderDiagnostic[]? _diagnostics;

    private MountEnvironment(
        Composition composition,
        ThemeContext? theme,
        ContextFrame? contexts,
        ComponentServiceBinding? services
    )
    {
        Composition = composition;
        _theme = theme;
        _contexts = contexts;
        Services = services;
    }

    internal Composition Composition { get; }
    internal ComponentServiceBinding? Services { get; }

    internal static MountEnvironment CreateRoot(Composition composition, ThemeContext? theme)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return new MountEnvironment(composition, theme, null, null);
    }

    internal MountEnvironment Provide<T>(T value, ContextProviderSource source)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(source);
        if (typeof(T) == typeof(ThemeContext))
            throw new InvalidOperationException(
                "ThemeContext is framework-controlled; use the explicit theme-taking mount APIs."
            );
        if (source.ExactType != typeof(T))
            throw new ArgumentException(
                $"Provider metadata declares exact type '{source.TypeName}' that conflicts with the supplied generic value type.",
                nameof(source)
            );
        long? shadowedOwnerId = null;
        for (var frame = _contexts; frame is not null; frame = frame.Parent)
            if (ReferenceEquals(frame.Identity, ContextIdentity<T>.Value))
            {
                shadowedOwnerId = frame.OwnerId;
                break;
            }
        return new MountEnvironment(
            Composition,
            _theme,
            new ContextFrame(
                ContextIdentity<T>.Value,
                value,
                _contexts,
                source,
                Composition.NextContextProviderId(),
                shadowedOwnerId
            ),
            Services
        );
    }

    internal MountEnvironment WithTheme(ThemeContext? theme)
    {
        if (ReferenceEquals(theme, _theme))
            return this;
        return new MountEnvironment(Composition, theme, _contexts, Services);
    }

    internal MountEnvironment BorrowForPopup(Composition popup, ThemeContext theme)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(theme);
        if (ReferenceEquals(popup, Composition))
            throw new ArgumentException(
                "A popup mount environment requires a distinct composition.",
                nameof(popup)
            );
        if (!ReferenceEquals(popup.Graph, Composition.Graph))
            throw new InvalidOperationException(
                "A popup mount environment must remain on its owner's reactive graph."
            );

        if (Services is not null)
            popup.Root.Scope.Own(Services.BorrowForPopup(popup));
        return new MountEnvironment(popup, theme, _contexts, Services);
    }

    internal ContextProviderDiagnostic[] DescribeProviders()
    {
        if (_diagnostics is not null)
            return _diagnostics;
        if (_contexts is null)
            return _diagnostics = [];
        List<ContextProviderDiagnostic>? providers = null;
        for (var frame = _contexts; frame is not null; frame = frame.Parent)
            if (frame.Source is { } source)
                (providers ??= []).Add(
                    new(frame.OwnerId, source.TypeName, source.Location, frame.ShadowedOwnerId)
                );
        if (providers is null)
            return _diagnostics = [];
        providers.Reverse();
        return _diagnostics = [.. providers];
    }

    internal MountEnvironment Attach(ComponentServiceBinding binding, Element parent)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(parent);
        if (Services is not null)
            throw new InvalidOperationException(
                "A mount environment already has an application service binding."
            );
        binding.ClaimMount(Composition, parent);
        return new MountEnvironment(Composition, _theme, _contexts, binding);
    }

    internal T RequireContext<T>(ComponentRequirementSource source, Element parent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parent);
        for (var frame = _contexts; frame is not null; frame = frame.Parent)
            if (ReferenceEquals(frame.Identity, ContextIdentity<T>.Value))
                return (T)frame.Value;
        var available = DescribeProviders();
        var diagnostic =
            available.Length == 0
                ? "none"
                : string.Join(
                    ", ",
                    available.Select(provider =>
                        $"'{provider.TypeName}' owner={provider.OwnerId} source={provider.SourceLocation}"
                    )
                );
        throw new InvalidOperationException(
            $"Requirement '{source.Member}' for exact type '{source.TypeName}' at {source.Location} has no context provider at mount {DescribeMountPath(parent)}. Available exact providers: {diagnostic}."
        );
    }

    internal T RequireService<T>(ComponentRequirementSource source, Element parent)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parent);
        var mountPath = DescribeMountPath(parent);
        var binding = Services;
        if (binding is null)
            throw new InvalidOperationException(
                $"Requirement '{source.Member}' for exact type '{source.TypeName}' at {source.Location} has no application service binding at mount {mountPath}."
            );
        return binding.Resolve<T>(source, mountPath);
    }

    internal ThemeContext Theme =>
        _theme ?? throw ComponentRequirementSource.FrameworkTheme.Missing("context provider");

    internal void CheckMountAdmission() => Services?.CheckMountAdmission();

    private static string DescribeMountPath(Element parent)
    {
        var lineage = new Stack<Element>();
        for (Element? current = parent; current is not null; current = current.Parent)
            lineage.Push(current);
        return string.Join("/", lineage.Select(element => element.Name + "#" + element.Id));
    }

    private sealed class ContextFrame(
        object identity,
        object value,
        ContextFrame? parent,
        ContextProviderSource? source = null,
        long ownerId = 0,
        long? shadowedOwnerId = null
    )
    {
        internal object Identity { get; } = identity;
        internal object Value { get; } = value;
        internal ContextFrame? Parent { get; } = parent;
        internal ContextProviderSource? Source { get; } = source;
        internal long OwnerId { get; } = ownerId;
        internal long? ShadowedOwnerId { get; } = shadowedOwnerId;
    }
}

internal static class ContextIdentity<T>
{
    internal static readonly object Value = new();
}
