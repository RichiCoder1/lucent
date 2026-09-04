using System.Runtime.ExceptionServices;

namespace Lucent.Core;

/// <summary>A platform adapter that runs one caller-owned Lucent composition and theme.</summary>
public interface IApplicationHost
{
    /// <summary>Runs the host synchronously until the application exits.</summary>
    /// <param name="title">The nonblank title for the application's top-level window.</param>
    /// <param name="composition">The caller-owned composition to present.</param>
    /// <param name="theme">The caller-owned theme context to synchronize with platform settings.</param>
    /// <returns>The process exit code selected by the host.</returns>
    int Run(string title, Composition composition, ThemeContext theme);
}

/// <summary>Collects reusable configuration snapshots for <see cref="LucentApplication"/>.</summary>
public sealed class LucentApplicationBuilder
{
    private string _title = "Lucent";
    private Func<ThemeAppearance, Theme> _themeFactory = DefaultTheme;
    private IApplicationHost? _host;

    internal LucentApplicationBuilder() { }

    /// <summary>Sets the top-level window title for subsequently built applications.</summary>
    public LucentApplicationBuilder SetTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title = title;
        return this;
    }

    /// <summary>Sets the appearance-oriented theme factory for subsequently built applications.</summary>
    public LucentApplicationBuilder SetTheme(Func<ThemeAppearance, Theme> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _themeFactory = factory;
        return this;
    }

    /// <summary>Sets the platform host for subsequently built applications.</summary>
    public LucentApplicationBuilder UseHost(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        return this;
    }

    /// <summary>Snapshots the current configuration into a one-shot application.</summary>
    /// <exception cref="InvalidOperationException">No host has been selected.</exception>
    public LucentApplication Build() =>
        new(
            _title,
            _themeFactory,
            _host ?? throw new InvalidOperationException("An application host must be selected.")
        );

    private static Theme DefaultTheme(ThemeAppearance appearance) =>
        appearance.Contrast == ThemeContrast.High ? ControlThemes.HighContrast
        : appearance.ColorScheme == ThemeColorScheme.Dark ? ControlThemes.Dark
        : ControlThemes.Light;
}

/// <summary>Owns the portable lifecycle for one Lucent component recipe.</summary>
public sealed class LucentApplication
{
    private readonly string _title;
    private readonly Func<ThemeAppearance, Theme> _themeFactory;
    private readonly IApplicationHost _host;
    private int _started;

    internal LucentApplication(
        string title,
        Func<ThemeAppearance, Theme> themeFactory,
        IApplicationHost host
    )
    {
        _title = title;
        _themeFactory = themeFactory;
        _host = host;
    }

    /// <summary>Creates a reusable application builder with the title "Lucent" and standard control themes.</summary>
    public static LucentApplicationBuilder CreateBuilder() => new();

    /// <summary>Creates, mounts, hosts, and deterministically releases one component graph.</summary>
    /// <param name="root">The stable root component recipe to mount.</param>
    /// <returns>The exit code returned by the configured host.</returns>
    /// <exception cref="InvalidOperationException">This application instance has already run.</exception>
    public int Run(ComponentRecipe root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A built application can run only once.");

        Composition? composition = null;
        var errors = new List<Exception>();
        var exitCode = 0;
        try
        {
            var graph = new ReactiveGraph();
            composition = new Composition(graph, "application");
            var initialAppearance = ThemeAppearance.Light;
            var theme = new ThemeContext(
                composition.Root.Scope,
                RequireTheme(_themeFactory(initialAppearance)),
                appearance: initialAppearance
            );
            var appliedAppearance = initialAppearance;
            _ = composition.Root.Scope.Effect(
                () =>
                {
                    var appearance = theme.Appearance;
                    if (appearance == appliedAppearance)
                        return;
                    var next = RequireTheme(_themeFactory(appearance));
                    appliedAppearance = appearance;
                    theme.Theme = next;
                },
                "application-theme"
            );
            _ = composition.Mount(composition.Root, theme, root);
            exitCode = _host.Run(_title, composition, theme);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (composition is not null)
        {
            try
            {
                composition.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        ThrowAll(errors);
        return exitCode;
    }

    private static Theme RequireTheme(Theme? theme) =>
        theme
        ?? throw new InvalidOperationException("The application theme factory returned null.");

    private static void ThrowAll(List<Exception> errors)
    {
        if (errors.Count == 0)
            return;
        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Application run and cleanup failed.", errors);
    }
}
