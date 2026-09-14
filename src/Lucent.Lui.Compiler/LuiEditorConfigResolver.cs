using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lucent.Lui.Compiler;

/// <summary>Optional declaration-order policy selected for LUI linting.</summary>
public enum LuiDeclarationOrder
{
    /// <summary>No declaration-order convention is enforced.</summary>
    None,

    /// <summary>Components precede named styles.</summary>
    ComponentFirst,

    /// <summary>Named styles precede components.</summary>
    StylesFirst,
}

/// <summary>A configuration problem associated with an authored <c>.editorconfig</c> line.</summary>
public sealed class LuiEditorConfigDiagnostic
{
    internal LuiEditorConfigDiagnostic(string id, string message, string filePath, int line)
    {
        Id = id;
        Message = message;
        FilePath = filePath;
        Line = line;
    }

    /// <summary>Stable diagnostic identifier.</summary>
    public string Id { get; }

    /// <summary>Human-readable explanation of the invalid configuration.</summary>
    public string Message { get; }

    /// <summary>Absolute path of the configuration file.</summary>
    public string FilePath { get; }

    /// <summary>One-based line containing the invalid configuration.</summary>
    public int Line { get; }
}

/// <summary>An immutable EditorConfig input supplied by a host that cannot read project files directly.</summary>
public sealed class LuiEditorConfigSnapshot
{
    /// <summary>Creates a snapshot for an absolute <c>.editorconfig</c> path.</summary>
    public LuiEditorConfigSnapshot(string path, string source)
    {
        if (String.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
            throw new ArgumentException(
                "An absolute .editorconfig path is required.",
                nameof(path)
            );
        if (
            !System
                .IO.Path.GetFileName(path)
                .Equals(ConfigurationName, StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException("The snapshot path must name .editorconfig.", nameof(path));
        Path = System.IO.Path.GetFullPath(path);
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private const string ConfigurationName = ".editorconfig";

    /// <summary>Absolute configuration path used for ancestry and relative-section matching.</summary>
    public string Path { get; }

    /// <summary>Authored configuration text captured by the host.</summary>
    public string Source { get; }
}

/// <summary>Effective LUI source policy and the evidence used to resolve it.</summary>
public sealed class LuiEditorConfigResolution
{
    internal LuiEditorConfigResolution(
        LuiFormattingOptions options,
        LuiDeclarationOrder declarationOrder,
        IReadOnlyList<string> configurationFiles,
        IReadOnlyList<LuiEditorConfigDiagnostic> diagnostics,
        IReadOnlyDictionary<string, ReportDiagnostic> diagnosticSeverities
    )
    {
        Options = options;
        DeclarationOrder = declarationOrder;
        ConfigurationFiles = configurationFiles;
        Diagnostics = diagnostics;
        DiagnosticSeverities = diagnosticSeverities;
    }

    /// <summary>Formatting options after canonical defaults and matching sections are applied.</summary>
    public LuiFormattingOptions Options { get; }

    /// <summary>Optional lint-only declaration-order policy.</summary>
    public LuiDeclarationOrder DeclarationOrder { get; }

    /// <summary>Configuration files considered, ordered from outermost to innermost.</summary>
    public IReadOnlyList<string> ConfigurationFiles { get; }

    /// <summary>Errors that prevent callers from treating this resolution as usable.</summary>
    public IReadOnlyList<LuiEditorConfigDiagnostic> Diagnostics { get; }

    /// <summary>Explicit per-file severities for LUI diagnostics, keyed by diagnostic ID.</summary>
    public IReadOnlyDictionary<string, ReportDiagnostic> DiagnosticSeverities { get; }

    /// <summary>Whether every supported setting was resolved successfully.</summary>
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>Resolves the small, dependency-free <c>.editorconfig</c> surface supported by LUI tooling.</summary>
public static class LuiEditorConfigResolver
{
    private const string ConfigurationName = ".editorconfig";
    private const string IndentStyle = "indent_style";
    private const string IndentSize = "indent_size";
    private const string TabWidth = "tab_width";
    private const string MaximumLineLength = "max_line_length";
    private const string EndOfLine = "end_of_line";
    private const string DeclarationOrder = "lucent_lui_declaration_order";
    private const string SectionMatchSentinel = "lucent_lui_internal_section_match";

    private static readonly HashSet<string> SupportedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        IndentStyle,
        IndentSize,
        TabWidth,
        MaximumLineLength,
        EndOfLine,
        DeclarationOrder,
    };

    /// <summary>Resolves source formatting and lint policy for an absolute or relative <c>.lui</c> path.</summary>
    public static LuiEditorConfigResolution Resolve(string sourcePath)
    {
        if (String.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A source path is required.", nameof(sourcePath));

        var absoluteSourcePath = Path.GetFullPath(sourcePath);
        var diagnostics = new List<LuiEditorConfigDiagnostic>();
        var configurations = Discover(absoluteSourcePath, diagnostics);
        return Resolve(absoluteSourcePath, configurations, diagnostics);
    }

    /// <summary>Resolves policy from host-supplied immutable EditorConfig inputs without file-system reads.</summary>
    public static LuiEditorConfigResolution Resolve(
        string sourcePath,
        IEnumerable<LuiEditorConfigSnapshot> snapshots
    )
    {
        if (String.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        if (snapshots is null)
            throw new ArgumentNullException(nameof(snapshots));
        var absoluteSourcePath = Path.GetFullPath(sourcePath);
        var configurations = Discover(absoluteSourcePath, snapshots);
        return Resolve(absoluteSourcePath, configurations, new List<LuiEditorConfigDiagnostic>());
    }

    private static LuiEditorConfigResolution Resolve(
        string absoluteSourcePath,
        List<ConfigurationFile> configurations,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        var values = new Dictionary<string, ConfigValue>(StringComparer.OrdinalIgnoreCase);
        var diagnosticSeverities = new Dictionary<string, ConfigValue>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var configuration in configurations)
        {
            Apply(configuration, absoluteSourcePath, values, diagnosticSeverities);
        }

        var useTabs = ReadIndentStyle(values, diagnostics);
        var numericIndentSize = ReadNumber(values, IndentSize, 1, 16, allowTab: true, diagnostics);
        var numericTabWidth = ReadNumber(values, TabWidth, 1, 16, allowTab: false, diagnostics);
        var tabWidth = numericTabWidth.Number ?? numericIndentSize.Number ?? 4;
        var indentSize = numericIndentSize.IsTab ? tabWidth : numericIndentSize.Number ?? 4;
        var lineWidth = ReadLineWidth(values, diagnostics);
        var lineEnding = ReadLineEnding(values, diagnostics);
        var declarationOrder = ReadDeclarationOrder(values, diagnostics);
        var resolvedDiagnosticSeverities = ReadDiagnosticSeverities(
            diagnosticSeverities,
            diagnostics
        );

        return new LuiEditorConfigResolution(
            new LuiFormattingOptions(indentSize, useTabs, lineWidth, lineEnding, tabWidth),
            declarationOrder,
            configurations.Select(configuration => configuration.Path).ToArray(),
            diagnostics.ToArray(),
            resolvedDiagnosticSeverities
        );
    }

    private static List<ConfigurationFile> Discover(
        string sourcePath,
        IEnumerable<LuiEditorConfigSnapshot> snapshots
    )
    {
        var available = new Dictionary<string, LuiEditorConfigSnapshot>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null)
                throw new ArgumentException(
                    "EditorConfig snapshots cannot contain null entries.",
                    nameof(snapshots)
                );
            if (available.TryGetValue(snapshot.Path, out var existing))
            {
                if (!String.Equals(existing.Source, snapshot.Source, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Conflicting EditorConfig snapshots were supplied for '{snapshot.Path}'.",
                        nameof(snapshots)
                    );
                continue;
            }
            available.Add(snapshot.Path, snapshot);
        }

        var innerToOuter = new List<ConfigurationFile>();
        for (
            var directory = Path.GetDirectoryName(sourcePath);
            !String.IsNullOrEmpty(directory);
            directory = Directory.GetParent(directory)?.FullName
        )
        {
            var path = Path.GetFullPath(Path.Combine(directory, ConfigurationName));
            if (!available.TryGetValue(path, out var snapshot))
                continue;
            var configuration = Parse(snapshot.Path, snapshot.Source);
            innerToOuter.Add(configuration);
            if (configuration.IsRoot)
                break;
        }

        innerToOuter.Reverse();
        return innerToOuter;
    }

    /// <summary>Parses the standard EditorConfig severity vocabulary used by LUI diagnostics.</summary>
    public static bool TryParseDiagnosticSeverity(string? value, out ReportDiagnostic severity)
    {
        severity = value?.Trim().ToLowerInvariant() switch
        {
            "default" => ReportDiagnostic.Default,
            "none" => ReportDiagnostic.Suppress,
            "silent" => ReportDiagnostic.Hidden,
            "suggestion" => ReportDiagnostic.Info,
            "warning" => ReportDiagnostic.Warn,
            "error" => ReportDiagnostic.Error,
            _ => ReportDiagnostic.Default,
        };
        return value is not null
            && value.Trim().ToLowerInvariant()
                is "default"
                    or "none"
                    or "silent"
                    or "suggestion"
                    or "warning"
                    or "error";
    }

    private static List<ConfigurationFile> Discover(
        string sourcePath,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var innerToOuter = new List<ConfigurationFile>();
        while (!String.IsNullOrEmpty(directory))
        {
            var path = Path.Combine(directory, ConfigurationName);
            if (File.Exists(path))
            {
                try
                {
                    var source = File.ReadAllText(path);
                    var configuration = Parse(path, source);
                    innerToOuter.Add(configuration);
                    if (configuration.IsRoot)
                        break;
                }
                catch (IOException exception)
                {
                    diagnostics.Add(
                        new LuiEditorConfigDiagnostic("LUI6100", exception.Message, path, 1)
                    );
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add(
                        new LuiEditorConfigDiagnostic("LUI6100", exception.Message, path, 1)
                    );
                }
            }

            var parent = Directory.GetParent(directory);
            directory = parent?.FullName;
        }

        innerToOuter.Reverse();
        return innerToOuter;
    }

    private static ConfigurationFile Parse(string path, string source)
    {
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sections = new List<ConfigSection>();
        var current = default(ConfigSection);
        var isRoot = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].Trim();
            if (text.Length == 0 || text[0] is '#' or ';')
                continue;
            if (text[0] == '[' && text[text.Length - 1] == ']')
            {
                current = new ConfigSection(text.Substring(1, text.Length - 2));
                sections.Add(current);
                continue;
            }

            var separator = text.IndexOf('=');
            if (separator < 0)
                separator = text.IndexOf(':');
            if (separator < 0)
                continue;
            var key = text.Substring(0, separator).Trim();
            var value = text.Substring(separator + 1).Trim();
            if (current is null)
            {
                if (key.Equals("root", StringComparison.OrdinalIgnoreCase))
                    isRoot = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            current.Properties.Add(new ConfigProperty(key, value, index + 1));
        }

        return new ConfigurationFile(path, isRoot, sections);
    }

    private static void Apply(
        ConfigurationFile configuration,
        string sourcePath,
        Dictionary<string, ConfigValue> values,
        Dictionary<string, ConfigValue> diagnosticSeverities
    )
    {
        foreach (var section in configuration.Sections)
        {
            if (!Matches(configuration.Path, section.Pattern, sourcePath))
                continue;
            foreach (var property in section.Properties)
            {
                var isDiagnosticSeverity = TryGetLuiDiagnosticId(
                    property.Key,
                    out var diagnosticId
                );
                if (!SupportedKeys.Contains(property.Key) && !isDiagnosticSeverity)
                    continue;
                if (property.Value.Equals("unset", StringComparison.OrdinalIgnoreCase))
                {
                    if (isDiagnosticSeverity)
                        diagnosticSeverities.Remove(diagnosticId!);
                    else
                        values.Remove(property.Key);
                }
                else
                {
                    var configured = new ConfigValue(
                        property.Value,
                        configuration.Path,
                        property.Line
                    );
                    if (isDiagnosticSeverity)
                        diagnosticSeverities[diagnosticId!] = configured;
                    else
                        values[property.Key] = configured;
                }
            }
        }
    }

    private static bool TryGetLuiDiagnosticId(string key, out string? diagnosticId)
    {
        const string prefix = "dotnet_diagnostic.";
        const string suffix = ".severity";
        diagnosticId = null;
        if (
            !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        )
            return false;
        var candidate = key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length);
        if (!candidate.StartsWith("LUI", StringComparison.OrdinalIgnoreCase))
            return false;
        diagnosticId = candidate.ToUpperInvariant();
        return true;
    }

    private static System.Collections.ObjectModel.ReadOnlyDictionary<
        string,
        ReportDiagnostic
    > ReadDiagnosticSeverities(
        Dictionary<string, ConfigValue> values,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        var resolved = new Dictionary<string, ReportDiagnostic>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            if (TryParseDiagnosticSeverity(item.Value.Value, out var severity))
            {
                resolved[item.Key] = severity;
                continue;
            }
            Invalid(
                item.Value,
                $"dotnet_diagnostic.{item.Key}.severity",
                "default, none, silent, suggestion, warning or error",
                diagnostics
            );
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ReportDiagnostic>(
            resolved
        );
    }

    private static bool Matches(string configurationPath, string pattern, string sourcePath)
    {
        var probe = AnalyzerConfig.Parse(
            "[" + pattern + "]\n" + SectionMatchSentinel + " = true\n",
            configurationPath
        );
        var set = AnalyzerConfigSet.Create(new[] { probe }, out var diagnostics);
        return diagnostics.IsEmpty
            && set.GetOptionsForSourcePath(sourcePath)
                .AnalyzerOptions.TryGetValue(SectionMatchSentinel, out var value)
            && value == "true";
    }

    private static bool ReadIndentStyle(
        Dictionary<string, ConfigValue> values,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        if (!values.TryGetValue(IndentStyle, out var value))
            return false;
        if (value.Value.Equals("space", StringComparison.OrdinalIgnoreCase))
            return false;
        if (value.Value.Equals("tab", StringComparison.OrdinalIgnoreCase))
            return true;
        Invalid(value, IndentStyle, "space or tab", diagnostics);
        return false;
    }

    private static NumericValue ReadNumber(
        Dictionary<string, ConfigValue> values,
        string key,
        int minimum,
        int maximum,
        bool allowTab,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        if (!values.TryGetValue(key, out var value))
            return default;
        if (allowTab && value.Value.Equals("tab", StringComparison.OrdinalIgnoreCase))
            return new NumericValue(null, true);
        if (
            Int32.TryParse(
                value.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed >= minimum
            && parsed <= maximum
        )
        {
            return new NumericValue(parsed, false);
        }

        Invalid(
            value,
            key,
            allowTab
                ? $"an integer from {minimum} through {maximum}, or tab"
                : $"an integer from {minimum} through {maximum}",
            diagnostics
        );
        return default;
    }

    private static int ReadLineWidth(
        Dictionary<string, ConfigValue> values,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        if (!values.TryGetValue(MaximumLineLength, out var value))
            return 100;
        if (value.Value.Equals("off", StringComparison.OrdinalIgnoreCase))
            return Int32.MaxValue;
        if (
            Int32.TryParse(
                value.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed >= 20
        )
            return parsed;
        Invalid(value, MaximumLineLength, "an integer of at least 20, or off", diagnostics);
        return 100;
    }

    private static LuiLineEnding ReadLineEnding(
        Dictionary<string, ConfigValue> values,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        if (!values.TryGetValue(EndOfLine, out var value))
            return LuiLineEnding.Preserve;
        if (value.Value.Equals("lf", StringComparison.OrdinalIgnoreCase))
            return LuiLineEnding.Lf;
        if (value.Value.Equals("crlf", StringComparison.OrdinalIgnoreCase))
            return LuiLineEnding.CrLf;
        if (value.Value.Equals("cr", StringComparison.OrdinalIgnoreCase))
            return LuiLineEnding.Cr;
        Invalid(value, EndOfLine, "lf, crlf or cr", diagnostics);
        return LuiLineEnding.Preserve;
    }

    private static LuiDeclarationOrder ReadDeclarationOrder(
        Dictionary<string, ConfigValue> values,
        List<LuiEditorConfigDiagnostic> diagnostics
    )
    {
        if (!values.TryGetValue(DeclarationOrder, out var value))
            return LuiDeclarationOrder.None;
        if (value.Value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return LuiDeclarationOrder.None;
        if (value.Value.Equals("component_first", StringComparison.OrdinalIgnoreCase))
            return LuiDeclarationOrder.ComponentFirst;
        if (value.Value.Equals("styles_first", StringComparison.OrdinalIgnoreCase))
            return LuiDeclarationOrder.StylesFirst;
        Invalid(value, DeclarationOrder, "none, component_first or styles_first", diagnostics);
        return LuiDeclarationOrder.None;
    }

    private static void Invalid(
        ConfigValue value,
        string key,
        string expected,
        List<LuiEditorConfigDiagnostic> diagnostics
    ) =>
        diagnostics.Add(
            new LuiEditorConfigDiagnostic(
                "LUI6102",
                $"Unsupported {key} value '{value.Value}'; expected {expected}.",
                value.Path,
                value.Line
            )
        );

    private sealed class ConfigurationFile(
        string path,
        bool isRoot,
        IReadOnlyList<ConfigSection> sections
    )
    {
        public string Path { get; } = path;
        public bool IsRoot { get; } = isRoot;
        public IReadOnlyList<ConfigSection> Sections { get; } = sections;
    }

    private sealed class ConfigSection(string pattern)
    {
        public string Pattern { get; } = pattern;
        public List<ConfigProperty> Properties { get; } = new();
    }

    private sealed class ConfigProperty(string key, string value, int line)
    {
        public string Key { get; } = key;
        public string Value { get; } = value;
        public int Line { get; } = line;
    }

    private readonly struct ConfigValue(string value, string path, int line)
    {
        public string Value { get; } = value;
        public string Path { get; } = path;
        public int Line { get; } = line;
    }

    private readonly struct NumericValue(int? number, bool isTab)
    {
        public int? Number { get; } = number;
        public bool IsTab { get; } = isTab;
    }
}
