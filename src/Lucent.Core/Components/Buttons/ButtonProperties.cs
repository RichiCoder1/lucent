namespace Lucent.Core;

/// <summary>The stock visual emphasis of a button, independent of its action and accessibility role.</summary>
public enum ButtonRole
{
    /// <summary>An accent-filled primary action. This is the default.</summary>
    Primary,

    /// <summary>A bordered surface for an ordinary supporting action.</summary>
    Secondary,

    /// <summary>An action with no resting fill or border.</summary>
    Quiet,

    /// <summary>An error-colored action for a destructive operation.</summary>
    Destructive,
}

/// <summary>Style properties shared by text, composed, and icon buttons.</summary>
[StylePropertyGroup]
public static class ButtonProperties
{
    /// <summary>Chooses stock button emphasis without changing its semantics or invocation.</summary>
    /// <remarks>Minimal presentation clears resting fills and borders for every role while retaining
    /// hover, pressed, disabled, and keyboard-focus feedback. Author styles may override stock paint.
    /// Selection belongs to Selectable and selection controls: selected fill takes precedence over
    /// hover, pressed feedback takes precedence over selected fill, and focus remains independently visible.</remarks>
    [StyleProperty(Name = "ButtonRole")]
    public static readonly Property<ButtonRole> Role = new("button-role", ButtonRole.Primary);
}
