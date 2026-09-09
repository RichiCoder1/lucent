namespace Lucent.Core;

/// <summary>The outcome of a native selection dialog. Cancellation is an ordinary result.</summary>
public enum FilePickerStatus
{
    /// <summary>The user selected one or more locations.</summary>
    Selected,

    /// <summary>The user canceled, or the owning window closed.</summary>
    Canceled,

    /// <summary>The current host does not provide this capability.</summary>
    Unsupported,

    /// <summary>The platform could not complete the request.</summary>
    Failed,
}

/// <summary>A selected location. Selection grants no promise that subsequent file access will succeed.</summary>
public sealed record FilePickerItem(Uri Location, string Name);

/// <summary>An immutable dialog outcome; selecting a save destination never writes data.</summary>
public sealed class FilePickerResult
{
    /// <summary>Creates and snapshots a platform result.</summary>
    public FilePickerResult(
        FilePickerStatus status,
        IEnumerable<FilePickerItem>? items = null,
        string? error = null
    )
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        var snapshot = items?.ToArray() ?? [];
        if ((status == FilePickerStatus.Selected) != (snapshot.Length != 0))
            throw new ArgumentException(
                "Only a selected result contains locations.",
                nameof(items)
            );
        if (
            snapshot.Any(item =>
                item is null
                || item.Location is null
                || !item.Location.IsAbsoluteUri
                || string.IsNullOrWhiteSpace(item.Name)
            )
        )
            throw new ArgumentException(
                "Selected items require an absolute location and display name.",
                nameof(items)
            );
        if (
            error is not null
            && (status != FilePickerStatus.Failed || string.IsNullOrWhiteSpace(error))
        )
            throw new ArgumentException(
                "Only a failed result may contain a nonempty error.",
                nameof(error)
            );
        Status = status;
        Items = Array.AsReadOnly(snapshot);
        Error = error;
    }

    /// <summary>The outcome of this request.</summary>
    public FilePickerStatus Status { get; }

    /// <summary>The selected locations, or an empty collection for other outcomes.</summary>
    public IReadOnlyList<FilePickerItem> Items { get; }

    /// <summary>A platform-safe failure explanation, without exception or credential details.</summary>
    public string? Error { get; }
}

/// <summary>A named set of filename extensions. Use a sole asterisk to allow all files.</summary>
public sealed class FilePickerFilter
{
    /// <summary>Snapshots extensions such as "txt" or ".md"; paths and wildcard patterns are rejected.</summary>
    public FilePickerFilter(string name, IEnumerable<string> extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(extensions);
        var snapshot = extensions
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (
            snapshot.Length == 0
            || snapshot.Contains("*", StringComparer.Ordinal) && snapshot.Length != 1
        )
            throw new ArgumentException(
                "Provide extensions or a sole asterisk for all files.",
                nameof(extensions)
            );
        Name = name;
        Extensions = Array.AsReadOnly(snapshot);
    }

    /// <summary>The name displayed by the native chooser.</summary>
    public string Name { get; }

    /// <summary>Normalized extensions without leading dots.</summary>
    public IReadOnlyList<string> Extensions { get; }

    private static string Normalize(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        if (extension == "*")
            return extension;
        var normalized = extension.StartsWith('.') ? extension[1..] : extension;
        if (
            normalized
                .Split('.')
                .Any(part =>
                    part.Length == 0
                    || part.Any(character =>
                        !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'
                    )
                )
        )
            throw new ArgumentException(
                "An extension must contain only letters, digits, hyphens or underscores.",
                nameof(extension)
            );
        return normalized;
    }
}

/// <summary>Options for selecting existing files. The adapter snapshots collections before showing UI.</summary>
public sealed record OpenFileOptions(
    string? Title = null,
    IReadOnlyList<FilePickerFilter>? Filters = null,
    bool AllowMultiple = false,
    Uri? InitialDirectory = null
);

/// <summary>Options for selecting a save destination. The application owns overwrite/write errors after selection.</summary>
public sealed record SaveFileOptions(
    string? Title = null,
    string? SuggestedName = null,
    IReadOnlyList<FilePickerFilter>? Filters = null,
    Uri? InitialDirectory = null
);

/// <summary>Options for selecting an existing folder.</summary>
public sealed record PickFolderOptions(string? Title = null, Uri? InitialDirectory = null);

/// <summary>Application-injected native file selection. Implementations own window, cancellation and apartment lifetimes.</summary>
public interface IFilePicker
{
    /// <summary>Selects existing files without opening them.</summary>
    ValueTask<FilePickerResult> OpenFilesAsync(
        OpenFileOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Selects a destination without creating or modifying a file.</summary>
    ValueTask<FilePickerResult> SaveFileAsync(
        SaveFileOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Selects an existing folder without enumerating its contents for the application.</summary>
    ValueTask<FilePickerResult> PickFolderAsync(
        PickFolderOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
