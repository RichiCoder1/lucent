using System.Security.Cryptography;
using NuGet.Common;
using NuGet.Configuration;

namespace Lucent.Tools.NuGet;

// Discovery/order only, pinned to NuGet.Client 7.9.0 / 977537e19c6be57fead1411e6cf05f936bf1baf4.
// NuGet continues to own XML parsing, merging, clear and source mapping.
internal sealed class PinnedNuGetSettings : IDisposable
{
    private readonly List<FileStream> _handles = [];
    private readonly SettingsLoadingContext _context = new();
    private readonly string _workspace;
    private readonly string _user;
    private readonly string _machine;
    private readonly string _defaults;
    private readonly string[] _paths;
    private readonly string[] _hashes;
    internal ISettings Settings { get; }

    internal static PinnedNuGetSettings Open(
        string workspace,
        CancellationToken cancellationToken
    ) =>
        new(
            workspace,
            NuGetEnvironment.GetFolderPath(NuGetFolderPath.UserSettingsDirectory),
            NuGetEnvironment.GetFolderPath(NuGetFolderPath.MachineWideConfigDirectory),
            cancellationToken,
            Path.Combine(
                NuGetEnvironment.GetFolderPath(NuGetFolderPath.MachineWideSettingsBaseDirectory),
                ConfigurationConstants.ConfigurationDefaultsFile
            )
        );

    internal PinnedNuGetSettings(
        string workspace,
        string user,
        string machine,
        CancellationToken cancellationToken,
        string? defaults = null
    )
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException();
            _workspace = Path.GetFullPath(workspace);
            _user = Path.GetFullPath(user);
            _machine = Path.GetFullPath(machine);
            _defaults =
                defaults
                ?? Path.Combine(
                    Path.GetDirectoryName(_machine)!,
                    ConfigurationConstants.ConfigurationDefaultsFile
                );
            ValidatePath(_defaults);
            if (File.Exists(_defaults))
                throw new UnsupportedDefaultsException();
            _paths = Discover(_workspace, _user, _machine, cancellationToken);
            foreach (var path in _paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // FileMode.Open never creates. Deny write/delete while NuGet owns the fixed inputs.
                _handles.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            _hashes = _handles.Select(handle => Hash(handle, cancellationToken)).ToArray();
            Settings = global::NuGet.Configuration.Settings.LoadImmutableSettingsGivenConfigPaths(
                _paths,
                _context
            );
            if (!IsCurrent(cancellationToken))
                throw new IOException("Configuration changed.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool IsCurrent(CancellationToken cancellationToken)
    {
        var current = Discover(_workspace, _user, _machine, cancellationToken);
        return !File.Exists(_defaults)
            && _paths.SequenceEqual(current, StringComparer.OrdinalIgnoreCase)
            && _hashes.SequenceEqual(_handles.Select(handle => Hash(handle, cancellationToken)));
    }

    private static string[] Discover(
        string workspace,
        string user,
        string machine,
        CancellationToken cancellationToken
    )
    {
        var paths = new List<string>();
        var directory = workspace;
        var count = 0;
        while (directory is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > 128)
                throw new IOException("Directory bound exceeded.");
            ValidatePath(directory);
            foreach (var name in global::NuGet.Configuration.Settings.OrderedSettingsFileNames)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    Add(path);
                    break;
                }
            }
            directory = Path.GetDirectoryName(directory);
        }
        ValidatePath(user);
        var userDefault = Path.Combine(
            user,
            global::NuGet.Configuration.Settings.DefaultSettingsFileName
        );
        if (File.Exists(userDefault))
            Add(userDefault);
        var additional = Path.Combine(user, ConfigurationConstants.Config);
        foreach (var path in ConfigFiles(additional).Order(StringComparer.OrdinalIgnoreCase))
            if (
                !Path.GetFileName(path)
                    .Equals(
                        global::NuGet.Configuration.Settings.DefaultSettingsFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
                Add(path);
        // XPlatMachineWideSetting has no IDE/version subdirectory selectors.
        foreach (var path in ConfigFiles(machine))
            Add(path);
        return paths.ToArray();

        void Add(string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(path);
            if (paths.Count >= 64)
                throw new IOException("Configuration bound exceeded.");
            paths.Add(Path.GetFullPath(path));
        }
        IEnumerable<string> ConfigFiles(string folder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(folder);
            if (!Directory.Exists(folder))
                yield break;
            foreach (
                var pattern in global::NuGet
                    .Configuration
                    .Settings
                    .SupportedMachineWideConfigExtension
            )
            foreach (
                var path in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }
        }
    }

    private static void ValidatePath(string path)
    {
        if (
            !Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\", StringComparison.Ordinal)
            || new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed
        )
            throw new IOException("Only local fixed-drive configuration is supported.");
        for (
            var current = Path.GetFullPath(path);
            current is not null;
            current = Path.GetDirectoryName(current)
        )
            if (
                Path.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
                throw new IOException("Configuration reparse paths are unsupported.");
    }

    private static string Hash(FileStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length > 1024 * 1024)
            throw new IOException("Configuration bound exceeded.");
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256
        );
        var buffer = new byte[8192];
        int count;
        var length = 0;
        while ((count = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            length += count;
            if (length > 1024 * 1024)
                throw new IOException("Configuration bound exceeded.");
            hash.AppendData(buffer.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public void Dispose()
    {
        _context.Dispose();
        foreach (var handle in _handles)
            handle.Dispose();
    }
}

internal sealed class UnsupportedDefaultsException : IOException;
