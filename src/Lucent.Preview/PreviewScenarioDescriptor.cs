using System.Globalization;
using Lucent.Core;

namespace Lucent.Preview;

/// <summary>The explicit authored origin of a preview scenario, without filesystem evaluation.</summary>
public sealed class PreviewSource
{
    /// <summary>Records the supplied project, document and component identities.</summary>
    public PreviewSource(string project, string document, string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        Project = project;
        Document = document;
        Component = component;
    }

    /// <summary>The explicitly supplied source project identity.</summary>
    public string Project { get; }

    /// <summary>The explicitly supplied source document identity.</summary>
    public string Document { get; }

    /// <summary>The explicitly supplied compiled component identity.</summary>
    public string Component { get; }
}

/// <summary>Immutable presentation inputs for a launch; density is fixture data, not a Core switch.</summary>
public sealed class PreviewPresentation
{
    /// <summary>Snapshots presentation values without evaluating the theme factory or clock.</summary>
    public PreviewPresentation(
        LayoutViewport viewport,
        ThemeAppearance appearance,
        Func<ThemeAppearance, Theme> themeFactory,
        float density,
        CultureInfo culture,
        CultureInfo uiCulture,
        DateTimeOffset initialTime
    )
    {
        viewport.Validate();
        appearance.Validate();
        ArgumentNullException.ThrowIfNull(themeFactory);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(uiCulture);
        if (!float.IsFinite(density) || density <= 0)
            throw new ArgumentOutOfRangeException(nameof(density));
        Viewport = viewport;
        Appearance = appearance;
        ThemeFactory = themeFactory;
        Density = density;
        Culture = CultureInfo.ReadOnly((CultureInfo)culture.Clone());
        UICulture = CultureInfo.ReadOnly((CultureInfo)uiCulture.Clone());
        InitialTime = initialTime;
    }

    /// <summary>The logical viewport and physical scale.</summary>
    public LayoutViewport Viewport { get; }

    /// <summary>The explicit color-scheme and contrast appearance.</summary>
    public ThemeAppearance Appearance { get; }

    /// <summary>The explicitly supplied theme factory; its captured data remains author-owned.</summary>
    public Func<ThemeAppearance, Theme> ThemeFactory { get; }

    /// <summary>The positive density input supplied to fixture code.</summary>
    public float Density { get; }

    /// <summary>The read-only snapshot of formatting culture.</summary>
    public CultureInfo Culture { get; }

    /// <summary>The read-only snapshot of UI culture.</summary>
    public CultureInfo UICulture { get; }

    /// <summary>The initial instant used by the caller when creating a fresh controlled clock.</summary>
    public DateTimeOffset InitialTime { get; }
}

/// <summary>Immutable catalog metadata; constructing it never executes scenario setup or a root factory.</summary>
public sealed class PreviewScenarioDescriptor
{
    /// <summary>Records one stable ordinal identifier, title, source and presentation.</summary>
    public PreviewScenarioDescriptor(
        string id,
        string title,
        PreviewSource source,
        PreviewPresentation presentation
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(presentation);
        Id = id;
        Title = title;
        Source = source;
        Presentation = presentation;
    }

    /// <summary>The stable, ordinal, case-sensitive scenario identifier.</summary>
    public string Id { get; }

    /// <summary>The explicit launch title.</summary>
    public string Title { get; }

    /// <summary>The explicitly supplied source origin.</summary>
    public PreviewSource Source { get; }

    /// <summary>The immutable presentation input snapshot.</summary>
    public PreviewPresentation Presentation { get; }
}
