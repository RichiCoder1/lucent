namespace Lucent.LanguageServer;

internal static class FileUri
{
    public static bool TryGetPath(string? value, out string path)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
            if (OperatingSystem.IsWindows() &&
                path.Length >= 3 &&
                path[0] == '/' &&
                char.IsAsciiLetter(path[1]) &&
                path[2] == ':')
            {
                path = path[1..];
            }

            path = Path.GetFullPath(path);
            return true;
        }

        path = string.Empty;
        return false;
    }
}
