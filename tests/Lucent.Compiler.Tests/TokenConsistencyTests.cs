using System.Text.RegularExpressions;

namespace Lucent.Compiler.Tests;

[TestClass]
public sealed class TokenConsistencyTests
{
    [TestMethod]
    public void Example_windows_embed_and_apply_the_reviewed_lucent_mark()
    {
        var root = FindRoot();
        foreach (var app in new[] { "counter", "todo", "package-pulse", "workbench" })
        {
            var project = Directory.EnumerateFiles(Path.Combine(root, "examples", app), "*.csproj").Single();
            var projectText = File.ReadAllText(project);
            StringAssert.Contains(projectText, "lucent-icon-32.png");
            StringAssert.Contains(projectText, "AvaloniaResource");

            var appSource = File.ReadAllText(Path.Combine(root, "examples", app, "App.cs"));
            StringAssert.Contains(appSource, "ApplyWindowIcon(window)");
            StringAssert.Contains(appSource, "AssetLoader.Open");
            StringAssert.Contains(appSource, "new WindowIcon(stream)");
        }

        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            var png = Path.Combine(root, "design", "rendered", $"lucent-icon-{size}.png");
            Assert.IsTrue(File.Exists(png), $"Missing reviewed {size}px mark asset.");
            var dimensions = ReadPngDimensions(png);
            Assert.AreEqual(size, dimensions.Width);
            Assert.AreEqual(size, dimensions.Height);
        }
    }

    [TestMethod]
    public void Example_capture_profiles_are_explicitly_whitelisted()
    {
        var root = FindRoot();
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["counter"] = ["counter-light", "counter-dark-focus"],
            ["todo"] = ["todo-light-populated", "todo-dark-empty"],
            ["package-pulse"] = ["pulse-dark-results", "pulse-light-error"],
            ["workbench"] = ["workbench-light-shell", "workbench-dark-palette"],
        };

        foreach (var (app, profiles) in expected)
        {
            var source = File.ReadAllText(Path.Combine(root, "examples", app, "App.cs"));
            StringAssert.Contains(source, $"return profile is \"{profiles[0]}\" or \"{profiles[1]}\";");
        }
    }

    [TestMethod]
    public void Examples_install_the_shared_shadcn_theme_without_legacy_visual_tokens()
    {
        var root = FindRoot();
        var theme = ReadThemeResources(root);
        var tokens = ReadTokens(Path.Combine(root, "design", "tokens.css"));
        CollectionAssert.AreEquivalent(theme.Light.Keys.ToArray(), tokens.Light.Keys.ToArray());
        CollectionAssert.AreEquivalent(theme.Dark.Keys.ToArray(), tokens.Dark.Keys.ToArray());
        foreach (var (key, value) in tokens.Light)
            Assert.AreEqual(value, theme.Light[key], $"light {key}");
        foreach (var (key, value) in tokens.Dark)
            Assert.AreEqual(value, theme.Dark[key], $"dark {key}");

        foreach (var app in new[] { "counter", "todo", "package-pulse", "workbench" })
        {
            var directory = Path.Combine(root, "examples", app);
            var source = File.ReadAllText(Path.Combine(directory, "App.cs"));
            var fluent = source.IndexOf("new FluentTheme()", StringComparison.Ordinal);
            var shadcn = source.IndexOf("new Lucent.Themes.Shadcn.ShadcnTheme()", StringComparison.Ordinal);
            Assert.IsTrue(fluent >= 0 && shadcn > fluent, $"{app} must install Fluent before ShadcnTheme.");
            Assert.IsFalse(source.Contains("[\"Lucent.", StringComparison.Ordinal));

            var project = File.ReadAllText(Directory.EnumerateFiles(directory, "*.csproj").Single());
            StringAssert.Contains(project, "Lucent.Themes.Shadcn");
            Assert.IsFalse(project.Contains("Lucent.Styles.Utilities", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("LucentStyles", StringComparison.Ordinal));

            foreach (var css in Directory.EnumerateFiles(directory, "*.css"))
            {
                var text = File.ReadAllText(css);
                Assert.IsFalse(text.Contains("Lucent.", StringComparison.Ordinal), $"{css} retains a legacy visual resource.");
                var keys = Regex.Matches(text, "resource\\(\\\"(?<key>Shadcn\\.[^\"]+)\\\"\\)")
                    .Select(match => match.Groups["key"].Value)
                    .Distinct(StringComparer.Ordinal);
                Assert.IsTrue(keys.Any(), $"{css} must use Shadcn semantic resources.");
                foreach (var key in keys)
                    Assert.IsTrue(theme.Light.ContainsKey(key) && theme.Dark.ContainsKey(key),
                        $"{css} references missing Shadcn resource {key}.");
            }
        }
    }

    private static (Dictionary<string, string> Light, Dictionary<string, string> Dark) ReadTokens(string path)
    {
        var text = File.ReadAllText(path);
        return (ReadTokenBlock(text, ":root"), ReadTokenBlock(text, ".theme-dark"));
    }

    private static Dictionary<string, string> ReadTokenBlock(string text, string selector)
    {
        var start = text.IndexOf(selector, StringComparison.Ordinal);
        var open = text.IndexOf('{', start);
        var close = text.IndexOf('}', open);
        return Regex.Matches(text[open..close], "--(?<name>shadcn-[a-z-]+):\\s*(?<value>#[0-9a-f]+)", RegexOptions.IgnoreCase)
            .ToDictionary(match => "Shadcn." + string.Concat(match.Groups["name"].Value[7..]
                    .Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])),
                match => NormalizeCssColor(match.Groups["value"].Value), StringComparer.Ordinal);
    }

    private static (Dictionary<string, string> Light, Dictionary<string, string> Dark) ReadThemeResources(string root)
    {
        var text = File.ReadAllText(Path.Combine(root, "src", "Lucent.Themes.Shadcn", "ShadcnTheme.axaml"));
        return (ReadThemeBlock(text, "Light"), ReadThemeBlock(text, "Dark"));
    }

    private static Dictionary<string, string> ReadThemeBlock(string text, string variant)
    {
        var start = text.IndexOf($"x:Key=\"{variant}\"", StringComparison.Ordinal);
        var end = text.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        return Regex.Matches(text[start..end], "x:Key=\"(?<key>Shadcn\\.[^\"]+)\" Color=\"(?<value>#[0-9A-F]+)\"", RegexOptions.IgnoreCase)
            .ToDictionary(match => match.Groups["key"].Value,
                match => match.Groups["value"].Value.ToUpperInvariant(), StringComparer.Ordinal);
    }

    private static string NormalizeCssColor(string value)
    {
        value = value.ToUpperInvariant();
        return value.Length == 9
            ? $"#{value[7..9]}{value[1..7]}"
            : value;
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        Assert.IsTrue(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "design", "tokens.css")))
            directory = directory.Parent!;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
