namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a source-colored image with alternative text or explicit decorative intent.</summary>
    [LucentComponent]
    public static ComponentRecipe Image(
        ImageSource source,
        string? alternativeText = null,
        bool decorative = false,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateImageLabel(alternativeText, decorative);
        return ImageRecipe(
            () => source,
            alternativeText is null ? null : () => alternativeText,
            decorative,
            false,
            style
        );
    }

    /// <summary>Creates an image whose source and accessible name follow typed readers.</summary>
    [LucentComponent]
    public static ComponentRecipe Image(
        Func<ImageSource> source,
        Func<string>? alternativeText = null,
        bool decorative = false,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!decorative && alternativeText is null)
            throw new ArgumentException(
                "Image requires alternativeText or decorative: true.",
                nameof(alternativeText)
            );
        if (decorative && alternativeText is not null)
            throw new ArgumentException(
                "A decorative image cannot also have alternative text.",
                nameof(alternativeText)
            );
        return ImageRecipe(source, alternativeText, decorative, false, style);
    }

    /// <summary>Creates a 16-DIP monochrome icon using inherited TextColor. An optional label makes it meaningful.</summary>
    [LucentComponent]
    public static ComponentRecipe Icon(
        ImageSource source,
        string? label = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateImageLabel(label, label is null);
        return ImageRecipe(
            () => source,
            label is null ? null : () => label,
            label is null,
            true,
            style
        );
    }

    /// <summary>Creates an icon whose source and optional accessible label follow typed readers.</summary>
    [LucentComponent]
    public static ComponentRecipe Icon(
        Func<ImageSource> source,
        Func<string>? label = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        return ImageRecipe(source, label, label is null, true, style);
    }

    private static ComponentRecipe ImageRecipe(
        Func<ImageSource> source,
        Func<string>? label,
        bool decorative,
        bool icon,
        Style? style
    ) =>
        ComponentRecipe.Create(
            icon ? "icon" : "image",
            (context, root) =>
            {
                var initial =
                    source()
                    ?? throw new ArgumentException(
                        "An image source reader returned null.",
                        nameof(source)
                    );
                var name = label?.Invoke();
                ValidateImageLabel(name, decorative);
                Controls.Image(root, context.Theme, initial, name, decorative, icon, style);
                var binding = root.Scope.Own(new ImageBinding(root, initial));
                root.Image = binding;
                _ = root.Scope.Effect(
                    () =>
                    {
                        var next =
                            source()
                            ?? throw new ArgumentException(
                                "An image source reader returned null.",
                                nameof(source)
                            );
                        var nextName = label?.Invoke();
                        ValidateImageLabel(nextName, decorative);
                        root.UpdateControl(ImageProperties.Source, next);
                        binding.SetSource(next);
                        if (!decorative)
                            root.UpdateControlSemantics(new(SemanticRole.Image, nextName!));
                    },
                    root.Name + ".image-content"
                );
            }
        );

    private static void ValidateImageLabel(string? label, bool decorative)
    {
        if (decorative ? label is not null : string.IsNullOrWhiteSpace(label))
            throw new ArgumentException(
                "Image requires nonempty alternative text or explicit decorative intent, but not both.",
                nameof(label)
            );
    }
}

internal static partial class Controls
{
    internal static void Image(
        Element element,
        ThemeContext theme,
        ImageSource source,
        string? label,
        bool decorative,
        bool icon,
        Style? style
    )
    {
        var defaults = Style.Empty.Set(ImageProperties.Source, source);
        if (icon)
            defaults = defaults
                .Set(LayoutProperties.Width, 16f)
                .Set(LayoutProperties.Height, 16f)
                .Set(ImageProperties.ColorMode, ImageColorMode.Monochrome);
        if (decorative)
            element.Present(theme, defaults, style);
        else
            ConfigureSemantic(element, theme, defaults, style, new(SemanticRole.Image, label!));
    }
}
