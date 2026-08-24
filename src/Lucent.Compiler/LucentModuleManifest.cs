using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lucent.Compiler.Styling;

namespace Lucent.Compiler;

/// <summary>Internal, non-executable package metadata for Lucent consumers.</summary>
internal static class LucentModuleManifest
{
    internal const string ResourceName = "Lucent.ModuleManifest.v1.json";
    internal const int FormatMajor = 1;
    internal const int FormatMinor = 0;
    // Embedded resources are untrusted PE input; this bounds the only manifest byte-array allocation.
    internal const int MaximumResourceBytes = 1024 * 1024;

    internal static byte[] Create(
        LucentProjectContext? context,
        IReadOnlyList<LucentSourceInput> sources,
        LucentAssemblyIdentity identity,
        IReadOnlyList<string>? styleCatalogTypes = null,
        IReadOnlyList<LucentGlobalStyleInput>? globalStyles = null)
    {
        var classes = new List<StyleClassEntry>();
        var sourceEntries = new List<SourceIdentity>();
        foreach (var source in sources.OrderBy(input => LogicalPath(context, input.SourcePath), StringComparer.Ordinal))
        {
            sourceEntries.Add(new SourceIdentity(
                LogicalPath(context, source.SourcePath), Hash(source.SourceText), 0, source.SourceText.Length));
            if (source.StyleText is null)
                continue;

            var stylePath = source.StylePath ?? Path.ChangeExtension(source.SourcePath, ".css");
            sourceEntries.Add(new SourceIdentity(
                LogicalPath(context, stylePath), Hash(stylePath, source.StyleText), 0, source.StyleText.Length));
            var parsed = StyleSheetParser.Parse(source.StyleText, stylePath).Sheet;
            foreach (var rule in parsed.Rules)
            foreach (var name in rule.ClassNames.Distinct(StringComparer.Ordinal))
            {
                classes.Add(new StyleClassEntry(
                    name,
                    rule.TypeName,
                    StyleClassOrigin.LocalCss,
                    new SourceIdentity(LogicalPath(context, stylePath), Hash(stylePath, source.StyleText),
                        rule.SelectorOffset, rule.SelectorText.Length),
                    rule.SelectorText));
            }
        }
        foreach (var style in globalStyles ?? [])
        {
            var logicalPath = LogicalPath(context, style.Path);
            sourceEntries.Add(new SourceIdentity(logicalPath, Hash(style.Path, style.Text), 0, style.Text.Length));
            foreach (var rule in StyleSheetParser.Parse(style.Text, style.Path).Sheet.Rules)
            {
            var targets = rule.TypeName is not null
                ? [rule.TypeName]
                : CssPropertyCatalog.InferProjectedTargetTypes(rule.Declarations);
            foreach (var target in targets)
            foreach (var name in rule.ClassNames.Distinct(StringComparer.Ordinal))
                classes.Add(new StyleClassEntry(name, ManifestTypeName(target), StyleClassOrigin.GlobalStyle,
                    new SourceIdentity(logicalPath, Hash(style.Path, style.Text), rule.SelectorOffset, rule.SelectorText.Length),
                    ManifestSelector(rule, target), style.CatalogType));
            }
        }

        var model = new LucentModuleManifestModel(
            FormatMajor, FormatMinor, 0,
            typeof(LucentModuleManifest).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            identity,
            styleCatalogTypes?.OrderBy(type => type, StringComparer.Ordinal).ToArray() ?? [],
            CanonicalizeClasses(classes).ToArray(),
            sourceEntries.OrderBy(source => source.LogicalPath, StringComparer.Ordinal).ToArray());
        return Serialize(model);
    }

    private static string ManifestTypeName(string typeName) =>
        typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName[8..] :
        typeName.Contains('.', StringComparison.Ordinal) ? typeName : "Avalonia.Controls." + typeName;

    private static string ManifestSelector(BoundStyleRule rule, string targetType)
    {
        var parts = new List<string>();
        for (var index = 0; index < rule.Selector.Parts.Count; index++)
        {
            var part = rule.Selector.Parts[index];
            var type = part.TypeName ?? (index == rule.Selector.Parts.Count - 1 ? targetType : null);
            var text = type is null ? string.Empty : ManifestTypeName(type).Split('.').Last();
            text += string.Concat(part.Classes.Select(name => "." + name));
            if (part.Name is not null) text += "#" + part.Name;
            if (index == rule.Selector.Parts.Count - 1 && rule.PseudoClass is not null)
                text += ":" + rule.PseudoClass;
            parts.Add(text);
        }
        var result = parts[0];
        for (var index = 1; index < parts.Count; index++)
            result += rule.Selector.Combinators[index - 1] == BoundStyleCombinator.Child
                ? " > " + parts[index] : " " + parts[index];
        return result;
    }

    internal static byte[] Serialize(LucentModuleManifestModel model) =>
        JsonSerializer.SerializeToUtf8Bytes(model, JsonOptions);

    // Only callers that already have live/local text may compare source content.
    internal static bool MatchesLiveSource(SourceIdentity identity, string sourceText) =>
        IsHash(identity.ContentHash) && string.Equals(identity.ContentHash, Hash(identity.LogicalPath, sourceText), StringComparison.Ordinal);

    /// <summary>Reads only the promoted/current PE metadata, then compares supplied live text.</summary>
    internal static bool TryReadLocalSnapshot(LucentProjectContext context,
        IReadOnlyDictionary<string, string> liveSources, CancellationToken cancellationToken,
        out LucentModuleManifestSnapshot? manifest, out string? error)
    {
        manifest = null;
        error = null;
        if (string.IsNullOrWhiteSpace(context.TargetPath) || !File.Exists(context.TargetPath))
            return false;
        if (!TryReadFromPe(context.TargetPath, cancellationToken, out var pe, out error))
        {
            if (error?.Contains("no Lucent module manifest", StringComparison.Ordinal) == true)
                error = null;
            return false;
        }
        manifest = pe!.Manifest;
        var projectDirectory = context.ProjectPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(context.ProjectPath));
        foreach (var source in manifest.Sources.Concat(manifest.StyleClasses.Entries
                     .Where(entry => entry.Definition is not null).Select(entry => entry.Definition!)))
        {
            var path = projectDirectory is null ? source.LogicalPath : Path.GetFullPath(source.LogicalPath, projectDirectory);
            if (liveSources.TryGetValue(path, out var text) && !MatchesLiveSource(source, text))
            {
                error = $"The local Lucent module manifest is stale for '{source.LogicalPath}'.";
                manifest = null;
                return false;
            }
        }
        return true;
    }

    internal static bool TryRead(byte[] bytes, LucentAssemblyIdentity containingIdentity,
        out LucentModuleManifestModel? manifest, out string? error)
    {
        manifest = null;
        if (!TryReadNormalized(bytes, containingIdentity, out var snapshot, out error))
            return false;
        manifest = snapshot!.ToModel();
        return true;
    }

    internal static bool TryReadNormalized(byte[] bytes, LucentAssemblyIdentity containingIdentity,
        out LucentModuleManifestSnapshot? manifest, out string? error)
    {
        manifest = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("formatMajor", out _) ||
                !root.TryGetProperty("formatMinor", out _) ||
                !root.TryGetProperty("minimumReaderMinor", out _) ||
                !root.TryGetProperty("producerVersion", out var producerVersion) ||
                producerVersion.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(producerVersion.GetString()) ||
                !HasRequiredFields(root))
            {
                error = "The Lucent module manifest is missing required format fields.";
                return false;
            }
            var candidate = JsonSerializer.Deserialize<LucentModuleManifestModel>(bytes, JsonOptions);
            if (candidate is null || candidate.FormatMajor != FormatMajor ||
                candidate.FormatMinor != FormatMinor || candidate.MinimumReaderMinor < 0 ||
                candidate.MinimumReaderMinor > FormatMinor)
            {
                error = "The Lucent module manifest format is not supported.";
                return false;
            }
            if (!ValidIdentity(candidate.Assembly) || !Equals(candidate.Assembly, containingIdentity) ||
                candidate.StyleCatalogTypes is null || candidate.Sources is null || candidate.StyleClasses is null ||
                candidate.StyleCatalogTypes.Any(string.IsNullOrWhiteSpace) ||
                candidate.StyleCatalogTypes.Distinct(StringComparer.Ordinal).Count() != candidate.StyleCatalogTypes.Count ||
                candidate.Sources.Any(source => !ValidSource(source)) ||
                candidate.StyleClasses.Any(entry => !ValidEntry(entry)) ||
                candidate.StyleClasses.Any(entry => entry.CatalogType is not null && !candidate.StyleCatalogTypes.Contains(entry.CatalogType, StringComparer.Ordinal)) ||
                candidate.StyleClasses.GroupBy(entry => (entry.Name, entry.ApplicableType, entry.CatalogType), StringTupleComparer.Instance)
                    .Any(group => group.Count() > 1))
            {
                error = "The Lucent module manifest is invalid for its containing assembly.";
                return false;
            }
            manifest = new LucentModuleManifestSnapshot(candidate.FormatMajor, candidate.FormatMinor,
                candidate.MinimumReaderMinor, candidate.ProducerVersion, candidate.Assembly,
                new StyleClassCatalog(candidate.StyleClasses
                    .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                    .ThenBy(entry => entry.ApplicableType, StringComparer.Ordinal)),
                candidate.Sources.OrderBy(source => source.LogicalPath, StringComparer.Ordinal).ToImmutableArray(),
                candidate.StyleCatalogTypes.OrderBy(type => type, StringComparer.Ordinal).ToImmutableArray());
            return true;
        }
        catch (JsonException)
        {
            error = "The Lucent module manifest is malformed.";
            return false;
        }
    }

    internal static bool TryReadFromPe(string assemblyPath,
        out LucentModuleManifestModel? manifest, out string? error)
    {
        manifest = null;
        if (!TryReadFromPe(assemblyPath, CancellationToken.None, out var result, out error))
            return false;
        manifest = result!.Manifest.ToModel();
        return true;
    }

    internal static bool TryReadFromPe(string assemblyPath, CancellationToken cancellationToken,
        out LucentPeManifest? result, out string? error)
    {
        result = null;
        error = null;
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        var fingerprint = Hash(stream, cancellationToken);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        // Embedded resources have a nil implementation. This explicit loop avoids
        // accepting linked resources (which would introduce path/network I/O).
        var resources = reader.ManifestResources.Where(handle =>
        {
            var candidate = reader.GetManifestResource(handle);
            return candidate.Implementation.IsNil &&
                string.Equals(reader.GetString(candidate.Name), ResourceName, StringComparison.Ordinal);
        }).ToArray();
        if (resources.Length != 1)
        {
            error = resources.Length == 0 ? "The referenced assembly has no Lucent module manifest."
                : "The referenced assembly has duplicate Lucent module manifests.";
            return false;
        }
        foreach (var handle in resources)
        {
            var candidate = reader.GetManifestResource(handle);
            if (!candidate.Implementation.IsNil ||
                !string.Equals(reader.GetString(candidate.Name), ResourceName, StringComparison.Ordinal))
                continue;
            var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory
                ?? throw new BadImageFormatException("The assembly has no managed resources.");
            var content = pe.GetSectionData(directory.RelativeVirtualAddress + (int)candidate.Offset).GetContent();
            if (!TryReadResourceBytes(content, out var bytes, out error))
                return false;
            var identity = ReadIdentity(reader);
            if (!TryReadNormalized(bytes, identity, out var manifest, out error))
                return false;
            result = new LucentPeManifest(path, fingerprint, manifest!);
            return true;
        }
        throw new BadImageFormatException("The embedded Lucent module manifest could not be read.");
    }

    internal static bool HasPublicCatalogTypes(string assemblyPath, IEnumerable<string> typeNames,
        out string? error)
    {
        error = null;
        var names = typeNames.ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            error = "Lucent style catalog metadata names must be non-empty and unique.";
            return false;
        }
        var expected = names.ToHashSet(StringComparer.Ordinal);
        if (expected.Count == 0) return true;
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!IsPubliclyAccessible(reader, handle)) continue;
            var metadataName = MetadataName(reader, handle);
            if (expected.Contains(metadataName)) found.Add(metadataName);
        }
        if (found.SetEquals(expected)) return true;
        error = $"The compiled assembly does not expose public Lucent style catalog type '{expected.Except(found).OrderBy(name => name, StringComparer.Ordinal).First()}'.";
        return false;
    }

    internal static bool TryReadResourceBytes(ImmutableArray<byte> content, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        if (content.Length < sizeof(int))
        {
            error = "The embedded Lucent module manifest is malformed.";
            return false;
        }
        var length = BitConverter.ToInt32(content.AsSpan(0, sizeof(int)));
        if (length < 0 || length > MaximumResourceBytes || length > content.Length - sizeof(int))
        {
            error = length > MaximumResourceBytes
                ? "The embedded Lucent module manifest exceeds the supported size."
                : "The embedded Lucent module manifest is malformed.";
            return false;
        }
        bytes = content.Slice(sizeof(int), length).ToArray();
        return true;
    }

    private static bool IsPubliclyAccessible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public
            : (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic &&
              IsPubliclyAccessible(reader, declaring);
    }

    private static string MetadataName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return MetadataName(reader, declaring) + "+" + name;
        var @namespace = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }

    private static LucentAssemblyIdentity ReadIdentity(MetadataReader reader)
    {
        var assembly = reader.GetAssemblyDefinition();
        return new LucentAssemblyIdentity(
            reader.GetString(assembly.Name),
            assembly.Version.ToString(),
            assembly.Culture.IsNil ? string.Empty : reader.GetString(assembly.Culture),
            PublicKeyToken(reader.GetBlobBytes(assembly.PublicKey)));
    }

    private static string PublicKeyToken(byte[] publicKey)
    {
        if (publicKey.Length == 0) return string.Empty;
        var hash = SHA1.HashData(publicKey);
        return Convert.ToHexString(hash[^8..].Reverse().ToArray());
    }

    private static string LogicalPath(LucentProjectContext? context, string path)
    {
        var directory = context?.ProjectPath is { Length: > 0 }
            ? Path.GetDirectoryName(Path.GetFullPath(context.ProjectPath))
            : null;
        var result = directory is null ? Path.GetFileName(path) : Path.GetRelativePath(directory, path);
        return result.Replace('\\', '/');
    }

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string Hash(string path, string content)
    {
        if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return Hash(content);
        var sheet = StyleSheetParser.Parse(content, path).Sheet;
        return Hash(string.Join("\n", sheet.Rules.Select(rule => rule.SelectorText + "{" +
            string.Join(";", rule.Declarations.Select(declaration => declaration.PropertyName + ":" + declaration.Value)) + "}")
            .OrderBy(rule => rule, StringComparer.Ordinal)));
    }

    private static string Hash(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool ValidIdentity(LucentAssemblyIdentity? identity) => identity is not null &&
        !string.IsNullOrWhiteSpace(identity.Name) && !string.IsNullOrWhiteSpace(identity.Version) &&
        identity.Culture is not null && identity.PublicKeyToken is not null;
    private static bool ValidSource(SourceIdentity? source) => source is not null &&
        !string.IsNullOrWhiteSpace(source.LogicalPath) && source.Start >= 0 && source.Length >= 0 && IsHash(source.ContentHash);
    private static bool ValidEntry(StyleClassEntry? entry) => entry is not null &&
        !string.IsNullOrWhiteSpace(entry.Name) && entry.Detail is not null && Enum.IsDefined(entry.Origin) &&
        (entry.Origin != StyleClassOrigin.GlobalStyle ||
         !string.IsNullOrWhiteSpace(entry.CatalogType)) &&
        (entry.Definition is null || ValidSource(entry.Definition));
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool HasRequiredFields(JsonElement root)
    {
        if (!root.TryGetProperty("assembly", out var assembly) || assembly.ValueKind != JsonValueKind.Object ||
            !assembly.TryGetProperty("name", out _) || !assembly.TryGetProperty("version", out _) ||
            !assembly.TryGetProperty("culture", out _) || !assembly.TryGetProperty("publicKeyToken", out _) ||
            !root.TryGetProperty("styleCatalogTypes", out var catalogs) || catalogs.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("styleClasses", out var classes) || classes.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return false;
        return sources.EnumerateArray().All(HasRequiredSourceFields) &&
            classes.EnumerateArray().All(entry => entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("name", out _) && entry.TryGetProperty("origin", out var origin) &&
                entry.TryGetProperty("detail", out _) &&
                (origin.GetInt32() != (int)StyleClassOrigin.GlobalStyle || entry.TryGetProperty("catalogType", out _)) &&
                (!entry.TryGetProperty("definition", out var definition) ||
                 definition.ValueKind == JsonValueKind.Null || HasRequiredSourceFields(definition)));
    }

    private static bool HasRequiredSourceFields(JsonElement source) =>
        source.ValueKind == JsonValueKind.Object &&
        source.TryGetProperty("logicalPath", out _) && source.TryGetProperty("contentHash", out _) &&
        source.TryGetProperty("start", out _) && source.TryGetProperty("length", out _);

    private static IEnumerable<StyleClassEntry> CanonicalizeClasses(IEnumerable<StyleClassEntry> entries) => entries
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .ThenBy(entry => entry.ApplicableType, StringComparer.Ordinal)
        .ThenBy(entry => entry.CatalogType, StringComparer.Ordinal)
        .ThenBy(entry => entry.Definition?.LogicalPath, StringComparer.Ordinal)
        .ThenBy(entry => entry.Definition?.Start ?? -1)
        .ThenBy(entry => entry.Detail, StringComparer.Ordinal)
        .GroupBy(entry => (entry.Name, entry.ApplicableType, entry.CatalogType))
        .Select(group => group.First());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed class StringTupleComparer : IEqualityComparer<(string, string?, string?)>
    {
        internal static readonly StringTupleComparer Instance = new();
        public bool Equals((string, string?, string?) x, (string, string?, string?) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.Ordinal) &&
            string.Equals(x.Item2, y.Item2, StringComparison.Ordinal) &&
            string.Equals(x.Item3, y.Item3, StringComparison.Ordinal);
        public int GetHashCode((string, string?, string?) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Item1),
                value.Item2 is null ? 0 : StringComparer.Ordinal.GetHashCode(value.Item2),
                value.Item3 is null ? 0 : StringComparer.Ordinal.GetHashCode(value.Item3));
    }
}

internal enum StyleClassOrigin { LocalCss, NativeTheme, GlobalStyle, Utility }

internal sealed record LucentAssemblyIdentity(string Name, string Version, string Culture, string PublicKeyToken);
internal sealed record SourceIdentity(string LogicalPath, string ContentHash, int Start, int Length);
internal sealed record StyleClassEntry(string Name, string? ApplicableType, StyleClassOrigin Origin,
    SourceIdentity? Definition, string Detail, string? CatalogType = null);
internal sealed record LucentGlobalStyleInput(string Path, string Text, string CatalogType);
/// <summary>Producer-neutral immutable style metadata for project generations.</summary>
internal sealed class StyleClassCatalog
{
    internal StyleClassCatalog(IEnumerable<StyleClassEntry> entries) => Entries = entries
        .OrderBy(entry => entry.Name, StringComparer.Ordinal)
        .ThenBy(entry => entry.ApplicableType, StringComparer.Ordinal)
        .ThenBy(entry => entry.CatalogType, StringComparer.Ordinal)
        .ThenBy(entry => entry.Definition?.LogicalPath, StringComparer.Ordinal)
        .ThenBy(entry => entry.Definition?.Start ?? -1)
        .ThenBy(entry => entry.Detail, StringComparer.Ordinal)
        .GroupBy(entry => (entry.Name, entry.ApplicableType, entry.CatalogType))
        .Select(group => group.First()).ToImmutableArray();
    internal ImmutableArray<StyleClassEntry> Entries { get; }
}
internal sealed record LucentModuleManifestModel(int FormatMajor, int FormatMinor, int MinimumReaderMinor,
    string ProducerVersion, LucentAssemblyIdentity Assembly, IReadOnlyList<string> StyleCatalogTypes,
    IReadOnlyList<StyleClassEntry> StyleClasses, IReadOnlyList<SourceIdentity> Sources);
internal sealed record LucentModuleManifestSnapshot(int FormatMajor, int FormatMinor, int MinimumReaderMinor,
    string ProducerVersion, LucentAssemblyIdentity Assembly, StyleClassCatalog StyleClasses,
    ImmutableArray<SourceIdentity> Sources, ImmutableArray<string> StyleCatalogTypes)
{
    internal LucentModuleManifestModel ToModel() => new(FormatMajor, FormatMinor, MinimumReaderMinor,
        ProducerVersion, Assembly, StyleCatalogTypes, StyleClasses.Entries, Sources);
}
internal sealed record LucentPeManifest(string AssemblyPath, string Fingerprint, LucentModuleManifestSnapshot Manifest);
internal sealed record ReferencedManifestSnapshot(StyleClassCatalog Catalog, ImmutableArray<string> Diagnostics);

/// <summary>One project generation's bounded, non-executing reference-manifest cache.</summary>
internal sealed class ReferencedManifestCache
{
    private readonly Dictionary<ManifestKey, LucentPeManifest> _manifests = [];
    private readonly List<string> _diagnostics = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    internal ReferencedManifestCache(IEnumerable<string> referencePaths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        ReferencePaths = referencePaths.Select(Path.GetFullPath).Distinct(comparer).ToImmutableArray();
    }

    internal ImmutableArray<string> ReferencePaths { get; }
    internal IReadOnlyList<string> Diagnostics => _diagnostics;
    internal StyleClassCatalog Catalog => new(_manifests.Values.SelectMany(item => item.Manifest.StyleClasses.Entries));

    internal StyleClassCatalog CatalogForActiveThemes(Compilation compilation, string? projectPath)
    {
        var activeTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var add in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (add.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add", Expression: var styles } ||
                    add.ArgumentList.Arguments.Count != 1 ||
                    add.ArgumentList.Arguments[0].Expression is not ObjectCreationExpressionSyntax theme ||
                    UsesAlias(theme.Type, model) ||
                    model.GetSymbolInfo(styles).Symbol is not IPropertySymbol { Name: "Styles" } property ||
                    !IsApplicationStyles(property))
                    continue;
                var type = model.GetTypeInfo(theme).Type;
                if (type is null || !IsStyles(type))
                    continue;
                activeTypes.Add(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }
        }

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var appAxaml = Path.Combine(Path.GetDirectoryName(projectPath)!, "App.axaml");
            if (File.Exists(appAxaml))
            {
                try
                {
                    var root = XElement.Load(appAxaml, LoadOptions.None);
                    foreach (var catalogType in _manifests.Values.SelectMany(item => item.Manifest.StyleCatalogTypes))
                    {
                        var split = catalogType.LastIndexOf('.');
                        if (split > 0 && root.Name.LocalName == "Application" &&
                            root.Elements().Where(element => element.Name.LocalName == "Application.Styles")
                                .SelectMany(element => element.Elements()).Any(element =>
                            element.Name.LocalName == catalogType[(split + 1)..] &&
                            element.Name.NamespaceName == "using:" + catalogType[..split]))
                            activeTypes.Add(catalogType);
                    }
                }
                catch (System.Xml.XmlException) { }
            }
        }

        return new StyleClassCatalog(_manifests.Values.SelectMany(item =>
            item.Manifest.StyleClasses.Entries.Where(entry =>
                (entry.Origin != StyleClassOrigin.NativeTheme && entry.Origin != StyleClassOrigin.GlobalStyle) ||
                (entry.Origin == StyleClassOrigin.GlobalStyle
                    ? activeTypes.Contains(entry.CatalogType!)
                    : item.Manifest.StyleCatalogTypes.Any(activeTypes.Contains)))));
    }

    private static bool IsStyles(ITypeSymbol? type)
    {
        for (; type is not null; type = type.BaseType)
            if (type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "Avalonia.Styling.Styles") return true;
        return false;
    }

    // This is invoked while building a generation, never by a completion request.
    internal void Load(CancellationToken cancellationToken)
    {
        foreach (var path in ReferencePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!LucentModuleManifest.TryReadFromPe(path, cancellationToken, out var manifest, out var error))
                {
                    if (!error!.Contains("no Lucent module manifest", StringComparison.Ordinal))
                        Report(path, error);
                    continue;
                }
                var key = new ManifestKey(manifest!.Manifest.Assembly, manifest.Fingerprint);
                _manifests.TryAdd(key, manifest);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                Report(path, $"The referenced assembly manifest could not be read: {exception.Message}");
            }
        }
    }

    private void Report(string path, string error)
    {
        if (_reported.Add(path)) _diagnostics.Add(error);
    }

    private static bool IsApplicationStyles(IPropertySymbol property) =>
        string.Equals(property.OriginalDefinition.ContainingType
            .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            "Avalonia.Application", StringComparison.Ordinal);

    private static bool UsesAlias(TypeSyntax type, SemanticModel model) =>
        type.DescendantNodesAndSelf().OfType<NameSyntax>()
            .Any(name => name is AliasQualifiedNameSyntax || model.GetAliasInfo(name) is not null);

    private sealed record ManifestKey(LucentAssemblyIdentity Identity, string Fingerprint);
}
