using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Lucent.Core;
using Lucent.Renderer.Skia;
using Microsoft.CodeAnalysis.CSharp;

namespace Lucent.Lui.Tooling.Assets;

internal static class AssetCatalogGenerator
{
    private const string ManifestVersion = "lucent-assets-v1";
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions InventoryJson = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 5)
        {
            error.WriteLine(
                "Usage: Lucent.Lui.Tooling --generate-assets manifest output.cs inventory.json payload-directory stamp"
            );
            return 2;
        }

        try
        {
            Generate(args[0], args[1], args[2], args[3], args[4]);
            output.WriteLine($"Lucent asset catalog: {Path.GetFullPath(args[2])}");
            return 0;
        }
        catch (AssetGenerationException exception)
        {
            error.WriteLine($"LUIA{exception.Code:D4}: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            error.WriteLine($"LUIA9999: Asset generation failed: {exception.Message}");
            return 1;
        }
    }

    private static void Generate(
        string manifestPath,
        string sourcePath,
        string inventoryPath,
        string payloadDirectory,
        string stampPath
    )
    {
        var manifest = AssetManifest.Read(manifestPath);
        var assets = manifest.Assets.Select(asset => ReadAsset(manifest, asset)).ToArray();
        ValidateIdentityCollisions(assets);
        ValidateAccessorCollisions(manifest, assets);
        var applicationIcon = ValidateApplicationIcon(manifest, assets);
        var applicationArtwork = applicationIcon is null
            ? null
            : BuildApplicationArtwork(manifest, applicationIcon);

        var source = EmitSource(manifest, assets, applicationArtwork);
        var inventory = JsonSerializer.Serialize(
            new
            {
                version = 1,
                domain = manifest.Domain,
                applicationIconArtwork = applicationArtwork is null
                    ? null
                    : new
                    {
                        source = applicationIcon!.Source,
                        sourceHash = applicationIcon.Hash,
                        iconFile = applicationIcon.IconFile,
                        icoHash = Convert
                            .ToHexString(SHA256.HashData(applicationArtwork.IcoBytes))
                            .ToLowerInvariant(),
                        icoSize = applicationArtwork.IcoBytes.LongLength,
                        svgPolicy = SkiaImagePreparer.SvgPolicyIdentity,
                        svgFont = "none",
                        renditions = applicationArtwork.Renditions.Select(rendition => new
                        {
                            size = rendition.Size,
                            hash = rendition.Hash,
                            sizeBytes = rendition.Bytes.LongLength,
                        }),
                    },
                assets = assets.Select(asset => new
                {
                    path = asset.Path,
                    hash = asset.Hash,
                    size = asset.Length,
                    format = asset.Format,
                    mediaType = asset.MediaType,
                    intrinsicWidth = asset.Width,
                    intrinsicHeight = asset.Height,
                    density = asset.Density,
                    relativeWidth = asset.RelativeWidth,
                    relativeHeight = asset.RelativeHeight,
                    source = asset.Source,
                    accessorNamespace = asset.Namespace,
                    accessor = manifest.RootClass + "." + string.Join(".", asset.Accessor),
                    applicationIcon = asset.ApplicationIcon,
                    applicationIconFile = asset.IconFile,
                    resource = asset.ResourceName,
                }),
            },
            InventoryJson
        );

        SynchronizePayloads(payloadDirectory, assets);
        SynchronizeApplicationIconRenditions(payloadDirectory, assets, applicationArtwork);
        SynchronizeApplicationIcon(payloadDirectory, applicationArtwork);

        WriteIfChanged(sourcePath, source);
        WriteIfChanged(inventoryPath, inventory + Environment.NewLine);
        WriteAtomically(
            stampPath,
            string.Join(
                Environment.NewLine,
                assets
                    .Select(asset => asset.ResourceName)
                    .Concat(
                        applicationArtwork?.Renditions.Select(rendition => rendition.ResourceName)
                            ?? Enumerable.Empty<string>()
                    )
                    .Order(StringComparer.Ordinal)
            ) + Environment.NewLine
        );
    }

    private static GeneratedAsset ReadAsset(AssetManifest manifest, AssetInput input)
    {
        var fullPath = Path.GetFullPath(input.FullPath);
        if (!File.Exists(fullPath))
            throw new AssetGenerationException(
                1,
                $"Declared asset '{input.Source}' does not exist at '{fullPath}'."
            );

        var path = NormalizePath(
            string.IsNullOrWhiteSpace(input.Path)
                ? Path.GetRelativePath(manifest.ProjectDirectory, fullPath)
                : input.Path,
            input.Source
        );
        var bytes = File.ReadAllBytes(fullPath);
        var format = InferFormat(path, input.Kind, bytes, input.Source);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var dimensions =
            format.Name == "Binary"
                ? default
                : ReadDimensions(format, bytes, input.Density, input.Source);
        var accessor = string.IsNullOrWhiteSpace(input.Accessor)
            ? InferAccessor(path, input.Source)
            : ParseAccessor(input.Accessor, input.Source);
        var accessorNamespace = string.IsNullOrWhiteSpace(input.AccessorNamespace)
            ? manifest.Namespace
            : input.AccessorNamespace;
        ValidateNamespace(accessorNamespace, input.Source);
        var iconFile = string.IsNullOrWhiteSpace(input.IconFile)
            ? null
            : ResolveIconFile(manifest, input.IconFile, input.Source);

        return new GeneratedAsset(
            fullPath,
            input.Source,
            path,
            accessorNamespace,
            accessor,
            format.Name,
            format.MediaType,
            hash,
            bytes.LongLength,
            dimensions.Width,
            dimensions.Height,
            dimensions.Density,
            dimensions.RelativeWidth,
            dimensions.RelativeHeight,
            bytes,
            "Lucent.Assets." + HashText(manifest.Domain + "\0" + path + "\0" + hash),
            iconFile,
            ParseApplicationIcon(input.ApplicationIcon, input.Source)
        );
    }

    private static bool ParseApplicationIcon(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (bool.TryParse(value, out var result))
            return result;
        throw new AssetGenerationException(
            8,
            $"Asset '{source}' has invalid ApplicationIcon metadata '{value}'; use true or false."
        );
    }

    private static GeneratedAsset? ValidateApplicationIcon(
        AssetManifest manifest,
        GeneratedAsset[] assets
    )
    {
        var declared = assets.Where(asset => asset.ApplicationIcon).ToArray();
        var iconFiles = assets.Where(asset => asset.IconFile is not null).ToArray();
        if (iconFiles.Any(asset => !asset.ApplicationIcon))
            throw new AssetGenerationException(
                8,
                "IconFile is only valid on the asset that declares ApplicationIcon=true."
            );
        if (declared.Length == 0)
            return null;
        if (!manifest.IsApplication)
            throw new AssetGenerationException(
                8,
                "ApplicationIcon is only valid on the current executable project; referenced libraries must not declare an application default."
            );
        if (declared.Length > 1)
            throw new AssetGenerationException(
                8,
                $"Only one ApplicationIcon asset may be declared for the current application; found {declared.Length}."
            );
        if (iconFiles.Length > 1)
            throw new AssetGenerationException(
                8,
                "Only one IconFile may be paired with the current application's ApplicationIcon."
            );
        if (declared[0].Format == "Binary")
            throw new AssetGenerationException(
                8,
                $"ApplicationIcon asset '{declared[0].Source}' must be a PNG, JPEG, or SVG image."
            );
        return declared[0];
    }

    private static ApplicationIconArtwork BuildApplicationArtwork(
        AssetManifest manifest,
        GeneratedAsset applicationIcon
    )
    {
        byte[] ico;
        IReadOnlyList<ArtworkRendition> renditions;
        if (applicationIcon.IconFile is { } iconFile)
        {
            if (new FileInfo(iconFile).Length > 64L * 1024 * 1024)
                throw new AssetGenerationException(
                    8,
                    $"IconFile '{iconFile}' exceeds the 64 MiB application-icon limit."
                );
            ico = File.ReadAllBytes(iconFile);
            try
            {
                renditions = SkiaArtwork
                    .DecodeIco(ico)
                    .OrderBy(rendition => rendition.Width)
                    .ToArray();
            }
            catch (Exception exception)
                when (exception
                        is InvalidDataException
                            or ArgumentException
                            or OverflowException
                            or InvalidOperationException
                )
            {
                throw new AssetGenerationException(
                    8,
                    $"IconFile '{iconFile}' could not be decoded: {exception.Message}"
                );
            }
        }
        else
        {
            var source = CreateGeneratedImageSource(applicationIcon, manifest.Domain);
            try
            {
                renditions = SkiaArtwork.PrepareRenditions(
                    source,
                    SkiaArtwork.ApplicationIconSizes,
                    new SkiaImagePreparer(),
                    ImageLoadLimits.Default
                );
                ico = SkiaArtwork.CreateIco(renditions);
            }
            catch (Exception exception)
                when (exception
                        is ImageLoadException
                            or InvalidDataException
                            or ArgumentException
                            or InvalidOperationException
                )
            {
                throw new AssetGenerationException(
                    8,
                    $"ApplicationIcon asset '{applicationIcon.Source}' could not be prepared: {exception.Message}"
                );
            }
        }

        try
        {
            var generatedRenditions = renditions
                .Select(rendition =>
                {
                    var bytes = SkiaArtwork.EncodePng(rendition);
                    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    var path = $"__application-icon/{rendition.Width}.png";
                    var resource =
                        "Lucent.Assets.ApplicationIcon."
                        + HashText(
                            manifest.Domain
                                + "\0"
                                + applicationIcon.Path
                                + "\0"
                                + applicationIcon.Hash
                                + "\0"
                                + rendition.Width.ToString(CultureInfo.InvariantCulture)
                                + "\0"
                                + hash
                        );
                    return new GeneratedIconRendition(rendition.Width, path, hash, bytes, resource);
                })
                .ToArray();
            return new ApplicationIconArtwork(ico, generatedRenditions);
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or ArgumentException
                        or InvalidOperationException
                        or OverflowException
            )
        {
            throw new AssetGenerationException(
                8,
                $"ApplicationIcon asset '{applicationIcon.Source}' could not be encoded: {exception.Message}"
            );
        }
    }

    private static void SynchronizeApplicationIcon(
        string payloadDirectory,
        ApplicationIconArtwork? applicationArtwork
    )
    {
        var output = Path.Combine(Path.GetDirectoryName(payloadDirectory)!, "application.ico");
        if (applicationArtwork is null)
        {
            if (File.Exists(output))
                File.Delete(output);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, applicationArtwork.IcoBytes);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void SynchronizeApplicationIconRenditions(
        string payloadDirectory,
        GeneratedAsset[] assets,
        ApplicationIconArtwork? applicationArtwork
    )
    {
        var desired = new HashSet<string>(
            assets.Select(asset => asset.ResourceName),
            StringComparer.Ordinal
        );
        if (applicationArtwork is not null)
            foreach (var rendition in applicationArtwork.Renditions)
                desired.Add(rendition.ResourceName);
        Directory.CreateDirectory(payloadDirectory);
        foreach (var existing in Directory.EnumerateFiles(payloadDirectory))
            if (!desired.Contains(Path.GetFileName(existing)))
                File.Delete(existing);
        foreach (
            var rendition in applicationArtwork?.Renditions ?? Array.Empty<GeneratedIconRendition>()
        )
        {
            var destination = Path.Combine(payloadDirectory, rendition.ResourceName);
            if (
                File.Exists(destination)
                && FileMatches(rendition.Bytes, rendition.Hash, destination)
            )
                continue;
            var temporary = destination + ".tmp";
            File.WriteAllBytes(temporary, rendition.Bytes);
            File.Move(temporary, destination, overwrite: true);
        }
    }

    private static ImageSource CreateGeneratedImageSource(
        GeneratedAsset applicationIcon,
        string domain
    )
    {
        var assetFormat = Enum.Parse<AssetFormat>(applicationIcon.Format, ignoreCase: false);
        var reference = new AssetReference(
            new AssetId(domain, applicationIcon.Path),
            applicationIcon.Hash,
            applicationIcon.Length,
            assetFormat,
            () => new MemoryStream(applicationIcon.Bytes, writable: false),
            new AssetImageMetadata(
                checked((float)applicationIcon.Width!.Value),
                checked((float)applicationIcon.Height!.Value),
                checked((float)applicationIcon.Density!.Value),
                (float?)applicationIcon.RelativeWidth,
                (float?)applicationIcon.RelativeHeight
            )
        );
        return ImageSource.FromAsset(reference);
    }

    private static string ResolveIconFile(AssetManifest manifest, string value, string source)
    {
        var relative = NormalizePath(value, source);
        var projectDirectory = Path.GetFullPath(manifest.ProjectDirectory);
        var fullPath = Path.GetFullPath(
            Path.Combine(projectDirectory, relative.Replace('/', Path.DirectorySeparatorChar))
        );
        var projectPrefix = projectDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? projectDirectory
            : projectDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            throw new AssetGenerationException(
                2,
                $"Asset '{source}' IconFile '{value}' must stay within the project directory."
            );
        if (!File.Exists(fullPath))
            throw new AssetGenerationException(
                1,
                $"Asset '{source}' IconFile does not exist at '{fullPath}'."
            );
        return fullPath;
    }

    private static string NormalizePath(string value, string source)
    {
        var normalized = value.Replace('\\', '/');
        if (
            string.IsNullOrWhiteSpace(normalized)
            || normalized[0] == '/'
            || Regex.IsMatch(normalized, "^[A-Za-z]:")
            || normalized.Any(character =>
                char.IsControl(character) || ":*?\"<>|#".Contains(character)
            )
        )
            throw new AssetGenerationException(
                2,
                $"Asset '{source}' has invalid catalog Path '{value}'; use a relative path."
            );

        var segments = normalized.Split('/');
        if (
            segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment != segment.Trim()
            )
        )
            throw new AssetGenerationException(
                2,
                $"Asset '{source}' has invalid catalog Path '{value}'; empty, whitespace-edged, and traversal segments are not allowed."
            );
        return string.Join("/", segments);
    }

    private static AssetFormatInfo InferFormat(
        string path,
        string kind,
        byte[] bytes,
        string source
    )
    {
        if (string.Equals(kind, "Binary", StringComparison.OrdinalIgnoreCase))
            return new("Binary", "application/octet-stream");
        if (
            !string.IsNullOrWhiteSpace(kind)
            && !string.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase)
        )
            throw new AssetGenerationException(
                3,
                $"Asset '{source}' has unsupported Kind '{kind}'; use Image or Binary."
            );
        var inferred = SniffFormat(bytes, source);
        AssetFormatInfo? declared = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => new("Png", "image/png"),
            ".jpg" or ".jpeg" => new("Jpeg", "image/jpeg"),
            ".svg" => new("Svg", "image/svg+xml"),
            _ => null,
        };
        if (declared is not null && declared.Name != inferred.Name)
            throw new AssetGenerationException(
                3,
                $"Asset '{source}' content is {inferred.Name} but catalog Path '{path}' declares {declared.Name}."
            );
        return inferred;
    }

    private static AssetFormatInfo SniffFormat(byte[] bytes, string source)
    {
        ReadOnlySpan<byte> png = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length >= png.Length && bytes.AsSpan(0, png.Length).SequenceEqual(png))
            return new("Png", "image/png");
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xd8)
            return new("Jpeg", "image/jpeg");
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = XmlReader.Create(stream, SecureXmlSettings());
            if (
                reader.MoveToContent() == XmlNodeType.Element
                && reader.LocalName == "svg"
                && reader.NamespaceURI is "" or "http://www.w3.org/2000/svg"
            )
                return new("Svg", "image/svg+xml");
        }
        catch (XmlException) { }
        throw new AssetGenerationException(
            3,
            $"Asset '{source}' content is not a supported PNG, JPEG, or static SVG image; use Kind='Binary' for general bytes."
        );
    }

    private static AssetDimensions ReadDimensions(
        AssetFormatInfo format,
        byte[] bytes,
        string density,
        string source
    )
    {
        try
        {
            var result = AssetImageMetadataReader.Read(format.Name, bytes, density, source);
            return new(
                result.Width,
                result.Height,
                result.Density,
                result.RelativeWidth,
                result.RelativeHeight
            );
        }
        catch (InvalidDataException exception)
        {
            throw new AssetGenerationException(4, exception.Message);
        }
    }

    private static XmlReaderSettings SecureXmlSettings() =>
        new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024,
        };

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string[] InferAccessor(string path, string source)
    {
        var segments = path.Split('/');
        segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]);
        var result = segments.Select(segment => ToIdentifier(segment, source)).ToArray();
        if (result.Length == 0)
            throw new AssetGenerationException(5, $"Asset '{source}' cannot infer an accessor.");
        return result;
    }

    private static string[] ParseAccessor(string value, string source)
    {
        var result = value.Split('.');
        if (result.Any(segment => !IsIdentifier(segment)))
            throw new AssetGenerationException(
                5,
                $"Asset '{source}' has invalid Accessor '{value}'. Use dotted C# identifiers."
            );
        return result;
    }

    private static string ToIdentifier(string value, string source)
    {
        var builder = new StringBuilder(value.Length);
        var uppercase = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                uppercase = true;
                continue;
            }
            builder.Append(uppercase ? char.ToUpperInvariant(character) : character);
            uppercase = false;
        }
        if (builder.Length == 0)
            throw new AssetGenerationException(
                5,
                $"Asset '{source}' path segment '{value}' cannot form an accessor."
            );
        if (char.IsDigit(builder[0]))
            builder.Insert(0, '_');
        var result = builder.ToString();
        if (!IsIdentifier(result))
            throw new AssetGenerationException(
                5,
                $"Asset '{source}' path segment '{value}' produces invalid accessor '{result}'."
            );
        return result;
    }

    private static bool IsIdentifier(string value) => SyntaxFacts.IsValidIdentifier(value);

    private static void ValidateNamespace(string value, string source)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Split('.').Any(segment => !IsIdentifier(segment))
        )
            throw new AssetGenerationException(
                5,
                $"Asset '{source}' has invalid AccessorNamespace '{value}'."
            );
    }

    private static void ValidateIdentityCollisions(GeneratedAsset[] assets)
    {
        foreach (var group in assets.GroupBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase))
        {
            var exact = group
                .Select(asset => asset.Path)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (exact.Length > 1)
                throw new AssetGenerationException(
                    6,
                    $"Asset Paths differ only by case: {string.Join(", ", exact)}."
                );
            if (group.Count() > 1)
                throw new AssetGenerationException(
                    6,
                    $"Asset Path '{group.Key}' is declared more than once."
                );
        }
    }

    private static void ValidateAccessorCollisions(AssetManifest manifest, GeneratedAsset[] assets)
    {
        foreach (
            var group in assets.GroupBy(asset => asset.Namespace, StringComparer.OrdinalIgnoreCase)
        )
        {
            var spellings = group
                .Select(asset => asset.Namespace)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (spellings.Length > 1)
                throw new AssetGenerationException(
                    7,
                    $"Accessor namespaces differ only by case: {string.Join(", ", spellings)}."
                );
        }
        var generatedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var enclosing = CanonicalIdentifier(manifest.RootClass);
            var typeName = CanonicalQualified(asset.Namespace) + "." + enclosing;
            generatedTypes.Add(typeName);
            for (var index = 0; index < asset.Accessor.Length; index++)
            {
                var segment = CanonicalIdentifier(asset.Accessor[index]);
                if (string.Equals(segment, enclosing, StringComparison.OrdinalIgnoreCase))
                    throw new AssetGenerationException(
                        7,
                        $"Asset '{asset.Source}' accessor '{string.Join(".", asset.Accessor)}' conflicts with its enclosing generated type '{enclosing}'."
                    );
                if (index == asset.Accessor.Length - 1)
                    continue;
                typeName += "." + segment;
                generatedTypes.Add(typeName);
                enclosing = segment;
            }
        }
        foreach (
            var accessorNamespace in assets
                .Select(asset => CanonicalQualified(asset.Namespace))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
            if (
                generatedTypes.Any(type =>
                    accessorNamespace == type
                    || accessorNamespace.StartsWith(type + ".", StringComparison.OrdinalIgnoreCase)
                )
            )
                throw new AssetGenerationException(
                    7,
                    $"Accessor namespace '{accessorNamespace}' conflicts with a generated asset type of the same name."
                );

        foreach (
            var namespaceGroup in assets.GroupBy(asset => asset.Namespace, StringComparer.Ordinal)
        )
        {
            var root = new AccessorNode();
            foreach (var asset in namespaceGroup)
            {
                var current = root;
                foreach (var segment in asset.Accessor)
                {
                    if (current.Asset is not null)
                        throw AccessorConflict(current.Asset, asset);
                    if (!current.Children.TryGetValue(segment, out var next))
                    {
                        next = new AccessorNode();
                        current.Children.Add(segment, next);
                    }
                    current = next;
                }
                if (current.Asset is not null || current.Children.Count != 0)
                    throw AccessorConflict(current.Asset ?? current.FirstDescendant()!, asset);
                current.Asset = asset;
            }
        }
    }

    private static AssetGenerationException AccessorConflict(
        GeneratedAsset left,
        GeneratedAsset right
    ) =>
        new(
            7,
            $"Assets '{left.Source}' and '{right.Source}' produce conflicting accessor members."
        );

    private static string EmitSource(
        AssetManifest manifest,
        GeneratedAsset[] assets,
        ApplicationIconArtwork? applicationArtwork
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine(
            "#pragma warning disable CS1591 // Generated asset accessors are self-describing."
        );
        foreach (var asset in assets)
        {
            var metadata =
                Uri.EscapeDataString(manifest.Domain)
                + "|"
                + Uri.EscapeDataString(asset.Path)
                + "|"
                + asset.Hash;
            builder
                .Append(
                    "[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"Lucent.Asset.v1\", "
                )
                .Append(Literal(metadata))
                .AppendLine(")]");
        }
        if (assets.Length != 0)
        {
            builder.AppendLine("namespace Lucent.Lui.Generated.Assets");
            builder.AppendLine("{");
            builder.AppendLine("    internal static class __LucentAssetProvider");
            builder.AppendLine("    {");
            builder.AppendLine(
                "        internal static global::System.IO.Stream OpenRead(string resourceName)"
            );
            builder.AppendLine(
                "            => typeof(__LucentAssetProvider).Assembly.GetManifestResourceStream(resourceName)"
            );
            builder.AppendLine(
                "                ?? throw new global::System.IO.FileNotFoundException(\"Embedded Lucent asset is unavailable.\", resourceName);"
            );
            builder.AppendLine("    }");
            if (assets.SingleOrDefault(asset => asset.ApplicationIcon) is { } applicationIcon)
            {
                builder.AppendLine("    internal static class __LucentApplicationIconRegistration");
                builder.AppendLine("    {");
                builder.AppendLine(
                    "        [global::System.Runtime.CompilerServices.ModuleInitializer]"
                );
                builder.AppendLine("        internal static void Register()");
                builder.AppendLine(
                    "            => global::Lucent.Core.ApplicationIconDefaults.RegisterGenerated("
                );
                builder.AppendLine(
                    "                new global::Lucent.Core.ApplicationIconDefault("
                );
                builder
                    .Append("                    global::")
                    .Append(applicationIcon.Namespace)
                    .Append('.')
                    .Append(manifest.RootClass)
                    .Append('.')
                    .Append(string.Join(".", applicationIcon.Accessor))
                    .AppendLine(",");
                builder.AppendLine(
                    "                    new global::Lucent.Core.ApplicationIconRendition[]"
                );
                builder.AppendLine("                    {");
                foreach (
                    var rendition in applicationArtwork?.Renditions
                        ?? Array.Empty<GeneratedIconRendition>()
                )
                {
                    builder
                        .Append(
                            "                        new global::Lucent.Core.ApplicationIconRendition("
                        )
                        .Append(rendition.Size.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(",");
                    AppendGeneratedRenditionSource(builder, manifest, rendition, 28);
                    builder.AppendLine("                        ),");
                }
                builder.AppendLine("                    }));");
                builder.AppendLine("    }");
            }
            builder.AppendLine("}");
        }

        foreach (
            var namespaceGroup in assets.GroupBy(asset => asset.Namespace, StringComparer.Ordinal)
        )
        {
            builder.Append("namespace ").Append(namespaceGroup.Key).AppendLine();
            builder.AppendLine("{");
            builder
                .Append("    public static partial class ")
                .Append(manifest.RootClass)
                .AppendLine();
            builder.AppendLine("    {");
            EmitAccessorLevel(builder, namespaceGroup, 1, 1, manifest);
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
        return builder.ToString();
    }

    private static void AppendGeneratedRenditionSource(
        StringBuilder builder,
        AssetManifest manifest,
        GeneratedIconRendition rendition,
        int indent
    )
    {
        builder.Append(' ', indent).AppendLine("global::Lucent.Core.ImageSource.FromAsset(");
        builder.Append(' ', indent + 4).AppendLine("new global::Lucent.Core.AssetReference(");
        builder
            .Append(' ', indent + 8)
            .Append("new global::Lucent.Core.AssetId(")
            .Append(Literal(manifest.Domain))
            .Append(", ")
            .Append(Literal(rendition.Path))
            .AppendLine("),");
        builder.Append(' ', indent + 8).Append(Literal(rendition.Hash)).AppendLine(",");
        builder
            .Append(' ', indent + 8)
            .Append(rendition.Bytes.LongLength.ToString(CultureInfo.InvariantCulture))
            .AppendLine("L,");
        builder.Append(' ', indent + 8).AppendLine("global::Lucent.Core.AssetFormat.Png,");
        builder
            .Append(' ', indent + 8)
            .Append(
                "static () => global::Lucent.Lui.Generated.Assets.__LucentAssetProvider.OpenRead("
            )
            .Append(Literal(rendition.ResourceName))
            .AppendLine("),");
        builder
            .Append(' ', indent + 8)
            .Append("new global::Lucent.Core.AssetImageMetadata(")
            .Append(rendition.Size.ToString(CultureInfo.InvariantCulture))
            .Append("F, ")
            .Append(rendition.Size.ToString(CultureInfo.InvariantCulture))
            .AppendLine("F, 1F)");
        builder.Append(' ', indent + 4).AppendLine(")");
        builder.Append(' ', indent).AppendLine(")");
    }

    private static void EmitAccessorLevel(
        StringBuilder builder,
        IEnumerable<GeneratedAsset> assets,
        int depth,
        int indent,
        AssetManifest manifest
    )
    {
        foreach (
            var group in assets
                .GroupBy(asset => asset.Accessor[depth - 1], StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            var first = group.First();
            builder.Append(' ', (indent + 1) * 4);
            if (depth == first.Accessor.Length)
            {
                var image = first.Format != "Binary";
                builder
                    .Append("public static global::Lucent.Core.")
                    .Append(image ? "ImageSource " : "AssetReference ")
                    .Append(group.Key)
                    .AppendLine(" { get; } =");
                if (image)
                    builder
                        .Append(' ', (indent + 2) * 4)
                        .AppendLine("global::Lucent.Core.ImageSource.FromAsset(");
                builder
                    .Append(' ', (indent + 3) * 4)
                    .AppendLine("new global::Lucent.Core.AssetReference(");
                builder
                    .Append(' ', (indent + 4) * 4)
                    .Append("new global::Lucent.Core.AssetId(")
                    .Append(Literal(manifest.Domain))
                    .Append(", ")
                    .Append(Literal(first.Path))
                    .AppendLine("),");
                builder.Append(' ', (indent + 4) * 4).Append(Literal(first.Hash)).AppendLine(",");
                builder
                    .Append(' ', (indent + 4) * 4)
                    .Append(first.Length.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("L,");
                builder
                    .Append(' ', (indent + 4) * 4)
                    .Append("global::Lucent.Core.AssetFormat.")
                    .Append(first.Format)
                    .AppendLine(",");
                builder
                    .Append(' ', (indent + 4) * 4)
                    .Append(
                        "static () => global::Lucent.Lui.Generated.Assets.__LucentAssetProvider.OpenRead("
                    )
                    .Append(Literal(first.ResourceName))
                    .Append(')');
                if (image)
                {
                    builder.AppendLine(",");
                    builder
                        .Append(' ', (indent + 4) * 4)
                        .Append("new global::Lucent.Core.AssetImageMetadata(")
                        .Append(Float(first.Width))
                        .Append(", ")
                        .Append(Float(first.Height))
                        .Append(", ")
                        .Append(Float(first.Density))
                        .Append(", ")
                        .Append(NullableFloat(first.RelativeWidth))
                        .Append(", ")
                        .Append(NullableFloat(first.RelativeHeight))
                        .AppendLine(")");
                }
                else
                    builder.AppendLine();
                builder.Append(' ', (indent + 3) * 4).AppendLine(")");
                if (image)
                    builder.Append(' ', (indent + 2) * 4).AppendLine(");");
                else
                    builder.Append(' ', (indent + 2) * 4).AppendLine(";");
            }
            else
            {
                builder.Append("public static class ").Append(group.Key).AppendLine();
                builder.Append(' ', (indent + 1) * 4).AppendLine("{");
                EmitAccessorLevel(builder, group, depth + 1, indent + 1, manifest);
                builder.Append(' ', (indent + 1) * 4).AppendLine("}");
            }
        }
    }

    private static string Literal(string value) => JsonSerializer.Serialize(value);

    private static string Float(double? value) =>
        (
            (float?)value ?? throw new InvalidOperationException("Image dimensions are required.")
        ).ToString("R", CultureInfo.InvariantCulture) + "F";

    private static string NullableFloat(double? value) => value is null ? "null" : Float(value);

    private static void SynchronizePayloads(string path, GeneratedAsset[] assets)
    {
        Directory.CreateDirectory(path);
        var desired = assets
            .GroupBy(asset => asset.ResourceName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var existing in Directory.EnumerateFiles(path))
            if (!desired.ContainsKey(Path.GetFileName(existing)))
                File.Delete(existing);
        foreach (var pair in desired)
        {
            var destination = Path.Combine(path, pair.Key);
            if (File.Exists(destination) && FileMatches(pair.Value, destination))
                continue;
            var temporary = destination + ".tmp";
            File.WriteAllBytes(temporary, pair.Value.Bytes);
            File.Move(temporary, destination, true);
        }
    }

    private static bool FileMatches(GeneratedAsset asset, string path)
    {
        return FileMatches(asset.Bytes, asset.Hash, path);
    }

    private static bool FileMatches(byte[] bytes, string hash, string path)
    {
        var info = new FileInfo(path);
        if (info.Length != bytes.LongLength)
            return false;
        using var stream = File.OpenRead(path);
        return string.Equals(
            Convert.ToHexString(SHA256.HashData(stream)),
            hash,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string CanonicalIdentifier(string value) =>
        value.Length != 0 && value[0] == '@' ? value[1..] : value;

    private static string CanonicalQualified(string value) =>
        string.Join(".", value.Split('.').Select(CanonicalIdentifier));

    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path) && File.ReadAllText(path) == content)
            return;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Utf8);
        File.Move(temporary, path, true);
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Utf8);
        File.Move(temporary, path, true);
    }

    private sealed class AccessorNode
    {
        public Dictionary<string, AccessorNode> Children { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public GeneratedAsset? Asset { get; set; }

        public GeneratedAsset? FirstDescendant() =>
            Asset
            ?? Children
                .Values.Select(node => node.FirstDescendant())
                .FirstOrDefault(asset => asset is not null);
    }

    private sealed record AssetFormatInfo(string Name, string MediaType);

    private sealed record ApplicationIconArtwork(
        byte[] IcoBytes,
        IReadOnlyList<GeneratedIconRendition> Renditions
    );

    private sealed record GeneratedIconRendition(
        int Size,
        string Path,
        string Hash,
        byte[] Bytes,
        string ResourceName
    );

    private readonly record struct AssetDimensions(
        double? Width,
        double? Height,
        double? Density,
        double? RelativeWidth,
        double? RelativeHeight
    );

    private sealed record GeneratedAsset(
        string FullPath,
        string Source,
        string Path,
        string Namespace,
        string[] Accessor,
        string Format,
        string MediaType,
        string Hash,
        long Length,
        double? Width,
        double? Height,
        double? Density,
        double? RelativeWidth,
        double? RelativeHeight,
        byte[] Bytes,
        string ResourceName,
        string? IconFile,
        bool ApplicationIcon
    );

    private sealed class AssetGenerationException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private sealed record AssetInput(
        string FullPath,
        string Path,
        string Accessor,
        string AccessorNamespace,
        string Source,
        string Kind,
        string Density,
        string ApplicationIcon,
        string IconFile
    );

    private sealed record AssetManifest(
        string ProjectDirectory,
        string Domain,
        string Namespace,
        string RootClass,
        IReadOnlyList<AssetInput> Assets,
        string OutputType
    )
    {
        public bool IsApplication =>
            string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);

        public static AssetManifest Read(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                throw new AssetGenerationException(1, $"Asset manifest '{path}' is empty.");
            var header = DecodeLine(lines[0]);
            if (header.Length is < 6 or > 7 || header[0] != ManifestVersion)
                throw new AssetGenerationException(
                    1,
                    $"Asset manifest '{path}' has an unsupported header."
                );
            var rootClass = header[4];
            if (!IsIdentifier(rootClass))
                throw new AssetGenerationException(
                    5,
                    $"LucentAssetAccessorClass '{rootClass}' is not a C# identifier."
                );
            ValidateNamespace(header[3], path);
            ValidateDomain(header[2], path);
            var assets = lines
                .Skip(1)
                .Where(line => line.Length != 0)
                .Select(line =>
                {
                    var fields = DecodeLine(line);
                    if (fields.Length is < 6 or > 10 || fields[0] != "asset")
                        throw new AssetGenerationException(
                            1,
                            $"Asset manifest '{path}' contains an invalid entry."
                        );
                    return new AssetInput(
                        fields[1],
                        fields[2],
                        fields[3],
                        fields[4],
                        fields[5],
                        fields.Length > 6 ? fields[6] : string.Empty,
                        fields.Length > 7 ? fields[7] : string.Empty,
                        fields.Length > 8 ? fields[8] : string.Empty,
                        fields.Length > 9 ? fields[9] : string.Empty
                    );
                })
                .ToArray();
            return new(
                Path.GetFullPath(header[1]),
                header[2],
                header[3],
                rootClass,
                assets,
                header.Length > 6 ? header[6] : string.Empty
            );
        }

        private static void ValidateDomain(string domain, string source)
        {
            if (
                string.IsNullOrWhiteSpace(domain)
                || domain is "." or ".."
                || domain.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '.' and not '-' and not '_'
                )
            )
                throw new AssetGenerationException(
                    2,
                    $"Asset domain '{domain}' from '{source}' must contain letters, digits, '.', '-' or '_'."
                );
        }

        private static string[] DecodeLine(string line)
        {
            var fields = line.Split('\t');
            if (fields.Length == 0)
                return fields;
            var result = new string[fields.Length];
            result[0] = fields[0];
            for (var index = 1; index < fields.Length; index++)
            {
                try
                {
                    result[index] = Uri.UnescapeDataString(fields[index]);
                }
                catch (UriFormatException)
                {
                    throw new AssetGenerationException(
                        1,
                        "Asset manifest contains invalid encoded metadata."
                    );
                }
            }
            return result;
        }
    }
}
