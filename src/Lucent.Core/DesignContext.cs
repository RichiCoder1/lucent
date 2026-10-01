namespace Lucent.Core;

/// <summary>The immutable purpose selected when a composition is created.</summary>
public enum CompositionPurpose
{
    /// <summary>Normal application execution, including headless tests by default.</summary>
    Application = 0,

    /// <summary>Explicit design-preview execution.</summary>
    Preview = 1,
}

/// <summary>An immutable description of a component's composition purpose.</summary>
public readonly record struct DesignContext
{
    /// <summary>Creates a context for one supported composition purpose.</summary>
    public DesignContext(CompositionPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        Purpose = purpose;
    }

    /// <summary>The immutable purpose inherited from the originating composition.</summary>
    public CompositionPurpose Purpose { get; }

    /// <summary>Whether this composition was explicitly created for a preview.</summary>
    public bool IsDesignMode => Purpose == CompositionPurpose.Preview;
}
