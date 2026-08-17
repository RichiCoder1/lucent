using System.Globalization;
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
            StringAssert.Contains(source,
                $"return profile is \"{profiles[0]}\" or \"{profiles[1]}\";",
                $"{app} must reject undocumented quality-capture profile names.");
        }
    }

    [TestMethod]
    public void Canonical_tokens_match_every_example_theme_dictionary_and_adjacent_stylesheet()
    {
        var root = FindRoot();
        var tokens = ParseTokenBlocks(File.ReadAllText(Path.Combine(root, "design", "tokens.css")));
        foreach (var app in new[] { "counter", "todo", "package-pulse", "workbench" })
        {
            var source = File.ReadAllText(Path.Combine(root, "examples", app, "App.cs"));
            foreach (var pair in tokens.Light)
            {
                var key = ToResourceKey(pair.Key);
                Assert.IsTrue(TryReadResource(source, "Light", key, out var actual), $"{app} missing light {key}");
                Assert.AreEqual(Normalize(pair.Value), Normalize(actual), $"{app} light {key}");
            }
            foreach (var pair in tokens.Dark)
            {
                var key = ToResourceKey(pair.Key);
                Assert.IsTrue(TryReadResource(source, "Dark", key, out var actual), $"{app} missing dark {key}");
                Assert.AreEqual(Normalize(pair.Value), Normalize(actual), $"{app} dark {key}");
            }
        }

        foreach (var css in Directory.EnumerateFiles(Path.Combine(root, "examples"), "*.css", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(css);
            foreach (var block in new[] { tokens.Light, tokens.Dark })
                foreach (var pair in block)
                    Assert.IsFalse(Regex.IsMatch(text, $"--{Regex.Escape(pair.Key)}\\s*:", RegexOptions.IgnoreCase),
                        $"{css} redeclares canonical token --{pair.Key}");
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "examples"), "*.*", SearchOption.AllDirectories)
                     .Where(file => Path.GetExtension(file) is ".lui" or ".css"))
        {
            var text = File.ReadAllText(file);
            foreach (var pair in tokens.Light.Values.Concat(tokens.Dark.Values).Where(value => value.StartsWith('#')))
                Assert.IsFalse(text.Contains(pair, StringComparison.OrdinalIgnoreCase),
                    $"{file} contains a canonical palette literal {pair}");
        }
    }

    private static (Dictionary<string, string> Light, Dictionary<string, string> Dark) ParseTokenBlocks(string text)
    {
        var light = ParseBlock(Extract(text, ":root"));
        var dark = ParseBlock(Extract(text, ".theme-dark"));
        foreach (var pair in light) dark.TryAdd(pair.Key, pair.Value);
        return (light, dark);
    }

    private static string Extract(string text, string selector)
    {
        var start = text.IndexOf(selector, StringComparison.Ordinal);
        var open = text.IndexOf('{', start);
        var close = text.IndexOf('}', open);
        return text[open..close];
    }

    private static Dictionary<string, string> ParseBlock(string text) =>
        Regex.Matches(text, @"--(?<name>[a-z0-9-]+)\s*:\s*(?<value>[^;]+)", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Where(match => !match.Groups["name"].Value.StartsWith("lucent-mark-", StringComparison.Ordinal))
            .ToDictionary(match => match.Groups["name"].Value,
                match => match.Groups["value"].Value.Trim(), StringComparer.Ordinal);

    private static string ToResourceKey(string token) =>
        "Lucent." + string.Concat(token.Replace("lucent-", "", StringComparison.Ordinal).Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static bool TryReadResource(string source, string variant, string key, out string value)
    {
        var marker = $"ThemeVariant.{variant}";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        var end = variant == "Light"
            ? source.IndexOf("ThemeVariant.Dark", start + marker.Length, StringComparison.Ordinal)
            : source.IndexOf("};", start + marker.Length, StringComparison.Ordinal);
        if (start < 0) { value = string.Empty; return false; }
        if (end < 0) end = source.Length;
        var match = Regex.Match(source[start..end], $@"\[""{Regex.Escape(key)}""\]\s*=\s*(?<value>[^,}}]+)");
        value = match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
        return match.Success;
    }

    private static string Normalize(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (value.StartsWith('#')) return value;
        if (value.StartsWith("color.parse(\"#") || value.StartsWith("new solidcolorbrush(color.parse(\"#"))
        {
            var hash = value.IndexOf('#');
            return value[hash..].TrimEnd('\"', ')');
        }
        if (value == "colors.white") return "#ffffff";
        if (value.StartsWith("new solidcolorbrush(colors.white")) return "#ffffff";
        var milliseconds = Regex.Match(value, @"frommilliseconds\((?<n>[0-9.]+)");
        if (milliseconds.Success) return milliseconds.Groups["n"].Value + "ms";
        var corner = Regex.Match(value, @"cornerradius\s*\(\s*(?<n>[0-9.]+)");
        if (corner.Success) return corner.Groups["n"].Value + "px";
        var number = Regex.Match(value, @"(?<n>[0-9.]+)d?$");
        return number.Success ? number.Groups["n"].Value + (value.Contains("px") ? "px" : "px") : value;
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
