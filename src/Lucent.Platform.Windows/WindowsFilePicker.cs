using System.Runtime.CompilerServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Native Windows file selection bound to a Lucent window's composition.</summary>
/// <remarks>Inject this capability into application services. Requests are serialized by the host,
/// and completed on its owner thread. Windows runs its ordinary modal message loop while a chooser
/// is visible; Lucent application callbacks resume after dismissal. Save selection never writes a file.</remarks>
public sealed class WindowsFilePicker : IFilePicker
{
    private static readonly ConditionalWeakTable<Composition, WindowsFilePickerHost> Hosts = new();
    private readonly Composition _owner;

    /// <summary>Binds a picker to a composition; no native handle or COM object is retained by the service.</summary>
    public WindowsFilePicker(Composition owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public ValueTask<FilePickerResult> OpenFilesAsync(
        OpenFileOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new();
        return Enqueue(
            new(
                WindowsFilePickerKind.Open,
                options.Title,
                null,
                options.Filters,
                options.InitialDirectory,
                options.AllowMultiple
            ),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<FilePickerResult> SaveFileAsync(
        SaveFileOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new();
        return Enqueue(
            new(
                WindowsFilePickerKind.Save,
                options.Title,
                options.SuggestedName,
                options.Filters,
                options.InitialDirectory,
                false
            ),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<FilePickerResult> PickFolderAsync(
        PickFolderOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new();
        return Enqueue(
            new(
                WindowsFilePickerKind.Folder,
                options.Title,
                null,
                null,
                options.InitialDirectory,
                false
            ),
            cancellationToken
        );
    }

    private ValueTask<FilePickerResult> Enqueue(
        WindowsFilePickerRequest request,
        CancellationToken cancellation
    )
    {
        if (cancellation.IsCancellationRequested || _owner.IsDisposed)
            return ValueTask.FromResult(new FilePickerResult(FilePickerStatus.Canceled));
        return Hosts.TryGetValue(_owner, out var host)
            ? host.Enqueue(request, cancellation)
            : ValueTask.FromResult(new FilePickerResult(FilePickerStatus.Unsupported));
    }

    internal static WindowsFilePickerHost Attach(
        Composition owner,
        nint hwnd,
        uint windowId,
        Action wake,
        Func<bool> closeRequested
    )
    {
        var host = new WindowsFilePickerHost(
            owner,
            hwnd,
            wake,
            WindowsFilePickerNative.Show,
            closeRequested,
            windowId
        );
        Hosts.Add(owner, host);
        return host;
    }

    internal static void Detach(Composition owner) => Hosts.Remove(owner);
}

internal enum WindowsFilePickerKind
{
    Open,
    Save,
    Folder,
}

internal sealed record WindowsFilePickerRequest
{
    internal WindowsFilePickerRequest(
        WindowsFilePickerKind kind,
        string? title,
        string? suggestedName,
        IReadOnlyList<FilePickerFilter>? filters,
        Uri? initialDirectory,
        bool multiple
    )
    {
        if (title is not null && (string.IsNullOrWhiteSpace(title) || title.Contains('\0')))
            throw new ArgumentException(
                "A picker title must be nonempty and contain no NUL characters.",
                nameof(title)
            );
        if (
            suggestedName is not null
            && (
                string.IsNullOrWhiteSpace(suggestedName)
                || suggestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || suggestedName is "." or ".."
            )
        )
            throw new ArgumentException(
                "Supply a filename, not a path, as the suggested name.",
                nameof(suggestedName)
            );
        if (
            initialDirectory is not null
            && (!initialDirectory.IsAbsoluteUri || !initialDirectory.IsFile)
        )
            throw new ArgumentException(
                "The Windows picker requires a file URI for the initial directory.",
                nameof(initialDirectory)
            );
        var snapshot = filters?.ToArray() ?? [];
        if (snapshot.Any(filter => filter is null || filter.Name.Contains('\0')))
            throw new ArgumentException(
                "Filters require non-null entries and names without NUL characters.",
                nameof(filters)
            );
        Kind = kind;
        Title = title;
        SuggestedName = suggestedName;
        Filters = Array.AsReadOnly(snapshot);
        InitialDirectory = initialDirectory;
        Multiple = multiple;
    }

    internal WindowsFilePickerKind Kind { get; }
    internal string? Title { get; }
    internal string? SuggestedName { get; }
    internal IReadOnlyList<FilePickerFilter> Filters { get; }
    internal Uri? InitialDirectory { get; }
    internal bool Multiple { get; }
}
