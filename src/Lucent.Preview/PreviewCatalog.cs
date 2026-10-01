using Lucent.Core;

namespace Lucent.Preview;

/// <summary>Collects explicit closed-generic scenario factories without executing them.</summary>
public sealed class PreviewCatalogBuilder
{
    private readonly List<PreviewScenario> _scenarios = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    /// <summary>Registers typed setup and a compiled root factory under an exact ordinal identifier.</summary>
    public PreviewCatalogBuilder Add<TFixture>(
        PreviewScenarioDescriptor descriptor,
        Func<PreviewSetupContext, CancellationToken, ValueTask<TFixture>> setup,
        Func<TFixture, ApplicationSession, ComponentRecipe> createRoot
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(createRoot);
        if (!_ids.Add(descriptor.Id))
            throw new ArgumentException(
                "A preview scenario identifier is already registered.",
                nameof(descriptor)
            );
        _scenarios.Add(new TypedPreviewScenario<TFixture>(descriptor, setup, createRoot));
        return this;
    }

    /// <summary>Creates an immutable registration snapshot without running any scenario.</summary>
    public PreviewCatalog Build() => new(_scenarios.ToArray());
}

/// <summary>An immutable, inert snapshot of explicit preview registrations.</summary>
public sealed class PreviewCatalog
{
    private readonly Dictionary<string, PreviewScenario> _byId;

    internal PreviewCatalog(PreviewScenario[] scenarios)
    {
        Scenarios = Array.AsReadOnly(scenarios);
        _byId = scenarios.ToDictionary(scenario => scenario.Descriptor.Id, StringComparer.Ordinal);
    }

    /// <summary>The registration-order scenario snapshot; enumeration invokes no author code.</summary>
    public IReadOnlyList<PreviewScenario> Scenarios { get; }

    /// <summary>Gets one scenario using its exact ordinal identifier.</summary>
    public PreviewScenario Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId[id];
    }
}

/// <summary>A registered typed scenario exposed through immutable metadata and explicit launch binding.</summary>
public abstract class PreviewScenario
{
    private protected PreviewScenario(PreviewScenarioDescriptor descriptor) =>
        Descriptor = descriptor;

    /// <summary>The immutable registration metadata.</summary>
    public PreviewScenarioDescriptor Descriptor { get; }

    /// <summary>Registers fresh one-shot startup on the supplied builder, without running setup.</summary>
    public abstract PreviewScenarioBinding Bind(
        LucentApplicationBuilder builder,
        TimeProvider clock,
        CancellationToken cancellationToken = default
    );
}

internal sealed class TypedPreviewScenario<TFixture>(
    PreviewScenarioDescriptor descriptor,
    Func<PreviewSetupContext, CancellationToken, ValueTask<TFixture>> setup,
    Func<TFixture, ApplicationSession, ComponentRecipe> createRoot
) : PreviewScenario(descriptor)
{
    public override PreviewScenarioBinding Bind(
        LucentApplicationBuilder builder,
        TimeProvider clock,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clock);
        var binding = new TypedPreviewScenarioBinding<TFixture>(
            Descriptor,
            clock,
            setup,
            createRoot,
            cancellationToken
        );
        builder
            .SetPurpose(CompositionPurpose.Preview)
            .SetTitle(Descriptor.Title)
            .SetTheme(Descriptor.Presentation.ThemeFactory)
            .OnStart(binding.StartAsync);
        return binding;
    }
}
