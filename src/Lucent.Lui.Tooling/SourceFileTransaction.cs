using System.Text;

namespace Lucent.Lui.Tooling;

internal enum SourceFileWriteStatus
{
    Updated,
    ChangedSinceRead,
}

internal sealed class SourceFileSnapshot
{
    private SourceFileSnapshot(byte[] bytes, string text, Encoding encoding, byte[] preamble)
    {
        Bytes = bytes;
        Text = text;
        Encoding = encoding;
        Preamble = preamble;
    }

    public byte[] Bytes { get; }
    public string Text { get; }
    public Encoding Encoding { get; }
    public byte[] Preamble { get; }

    public static SourceFileSnapshot Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, preambleLength) = DetectEncoding(bytes);
        string text;
        try
        {
            text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "The source file is not valid UTF-8, UTF-16 or UTF-32 text.",
                exception
            );
        }

        return new SourceFileSnapshot(bytes, text, encoding, bytes[..preambleLength]);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            return (
                new UTF32Encoding(
                    bigEndian: true,
                    byteOrderMark: true,
                    throwOnInvalidCharacters: true
                ),
                4
            );
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            return (
                new UTF32Encoding(
                    bigEndian: false,
                    byteOrderMark: true,
                    throwOnInvalidCharacters: true
                ),
                4
            );
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            return (
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
                3
            );
        if (StartsWith(bytes, 0xFE, 0xFF))
            return (
                new UnicodeEncoding(
                    bigEndian: true,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true
                ),
                2
            );
        if (StartsWith(bytes, 0xFF, 0xFE))
            return (
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true
                ),
                2
            );
        return (
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            0
        );
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;
        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
                return false;
        }

        return true;
    }
}

internal static class SourceFileTransaction
{
    public static SourceFileWriteStatus Replace(
        string path,
        SourceFileSnapshot snapshot,
        string replacement
    )
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp"
        );
        try
        {
            WriteTemporary(temporaryPath, snapshot, replacement);
            using var source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete
            );
            if (!MatchesSnapshot(source, snapshot.Bytes))
                return SourceFileWriteStatus.ChangedSinceRead;
            File.Replace(
                temporaryPath,
                path,
                destinationBackupFileName: null,
                ignoreMetadataErrors: false
            );
            return SourceFileWriteStatus.Updated;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void WriteTemporary(string path, SourceFileSnapshot snapshot, string replacement)
    {
        var content = snapshot.Encoding.GetBytes(replacement);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough
        );
        if (snapshot.Preamble.Length != 0)
            stream.Write(snapshot.Preamble, 0, snapshot.Preamble.Length);
        stream.Write(content, 0, content.Length);
        stream.Flush(flushToDisk: true);
    }

    private static bool MatchesSnapshot(Stream source, byte[] expected)
    {
        if (source.Length != expected.Length)
            return false;
        var buffer = new byte[8192];
        var offset = 0;
        while (offset < expected.Length)
        {
            var read = source.Read(buffer, 0, Math.Min(buffer.Length, expected.Length - offset));
            if (read == 0)
                return false;
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] != expected[offset + index])
                    return false;
            }

            offset += read;
        }

        return true;
    }
}
