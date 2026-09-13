namespace Lucent.Core;

/// <summary>Immutable optional accessible name and description overrides for one declared semantic root.</summary>
public sealed record AriaMetadata
{
    /// <summary>Initializes optional accessible metadata overrides.</summary>
    public AriaMetadata(string? name = null, string? description = null)
    {
        if (name is not null && String.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "An accessible name must be nonempty when supplied.",
                nameof(name)
            );
        if (description is not null && String.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                "An accessible description must be nonempty when supplied.",
                nameof(description)
            );
        Name = name;
        Description = description;
    }

    /// <summary>Gets the optional accessible-name override.</summary>
    public string? Name { get; }

    /// <summary>Gets the optional accessible-description override.</summary>
    public string? Description { get; }
}
