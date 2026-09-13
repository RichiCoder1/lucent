namespace Lucent.Core;

/// <summary>Closed target capabilities recorded for generated authoring property helpers.</summary>
[Flags]
public enum StyleAuthoringCapabilities
{
    /// <summary>No authoring target.</summary>
    None = 0,

    /// <summary>The retained presentation target accepts the property.</summary>
    Styled = 1,

    /// <summary>The retained semantic target accepts the property.</summary>
    Accessible = 2,
}

/// <summary>Same-name input forms that a generated authoring property may expose.</summary>
[Flags]
public enum StyleAuthoringInputForms
{
    /// <summary>No input form.</summary>
    None = 0,

    /// <summary>A snapshot value.</summary>
    Value = 1,

    /// <summary>A live value reader.</summary>
    Reader = 2,

    /// <summary>A theme token.</summary>
    Token = 4,
}

/// <summary>Marks a static Core property container as the source of generated authoring metadata.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StylePropertyGroupAttribute : Attribute
{
    /// <summary>Gets or sets the default target capabilities for fields in the group.</summary>
    public StyleAuthoringCapabilities Capabilities { get; set; } =
        StyleAuthoringCapabilities.Styled;

    /// <summary>Gets or sets the default input forms for fields in the group.</summary>
    public StyleAuthoringInputForms InputForms { get; set; } =
        StyleAuthoringInputForms.Value
        | StyleAuthoringInputForms.Reader
        | StyleAuthoringInputForms.Token;

    /// <summary>Gets or sets whether fields in the group invalidate painting by default.</summary>
    public bool PaintInvalidating { get; set; } = true;
}

/// <summary>Overrides generated authoring metadata for one public static property field.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class StylePropertyAttribute : Attribute
{
    /// <summary>Gets or sets the canonical C# authoring name; the field name is used by default.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets source-compatible C# authoring aliases.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets target capability flags.</summary>
    public StyleAuthoringCapabilities Capabilities { get; set; } =
        StyleAuthoringCapabilities.Styled;

    /// <summary>Gets or sets same-name input forms.</summary>
    public StyleAuthoringInputForms InputForms { get; set; } =
        StyleAuthoringInputForms.Value
        | StyleAuthoringInputForms.Reader
        | StyleAuthoringInputForms.Token;

    /// <summary>Gets or sets an explicit presentation target identity.</summary>
    public string? StyleTarget { get; set; }

    /// <summary>Gets or sets an explicit semantic target identity.</summary>
    public string? SemanticTarget { get; set; }

    /// <summary>Gets or sets whether this property accepts a transition policy.</summary>
    public bool TransitionEligible { get; set; }

    /// <summary>Gets or sets whether changes to this property invalidate painting.</summary>
    public bool PaintInvalidating { get; set; } = true;
}

/// <summary>Typed shorthand for the public property groups; custom properties use <see cref="Style.Set{T}(Property{T}, T)"/>.</summary>
public static partial class StyleFluency
{
    /// <summary>Returns a style with uniform padding in logical pixels.</summary>
    /// <remarks>The generated <c>Padding</c> family retains the typed <see cref="Insets"/>,
    /// reader, and token overloads; this scalar convenience expands to all four edges.</remarks>
    public static Style Padding(this Style style, float value) =>
        style.Set(LayoutProperties.Padding, Insets.Uniform(value));

    /// <summary>Returns a styled recipe with uniform padding in logical pixels.</summary>
    public static AuthorRecipe<StyledCapability> Padding(
        this AuthorRecipe<StyledCapability> recipe,
        float value
    ) => recipe.Style(Style.Empty.Padding(value));

    /// <summary>Returns a styled and accessible recipe with uniform padding in logical pixels.</summary>
    public static AuthorRecipe<StyledAccessibleCapability> Padding(
        this AuthorRecipe<StyledAccessibleCapability> recipe,
        float value
    ) => recipe.Style(Style.Empty.Padding(value));
}
