using UberDashboard.Core.Localization;
using System.Text;

namespace UberDashboard.Application.Import;

public sealed record ImportSource(string Name, string Text);

/// <summary>Reads an import batch without changing its sources or persisting any records.</summary>
public static class ImportFileReader
{
    public const int MaxFiles = 100;
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxBatchBytes = 50 * 1024 * 1024;
    public const int MaxTasks = 10_000;

    public static async Task<IReadOnlyList<ImportSource>> ReadAsync(
        IReadOnlyList<string> paths, Encoding encoding, CancellationToken cancellationToken)
    {
        if (paths.Count > MaxFiles)
            throw new InvalidDataException(UiText.Format("YouCanLoadUpToFilesAt", MaxFiles));

        var fallbackEncoding = (Encoding)encoding.Clone();
        fallbackEncoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        var sources = new List<ImportSource>(paths.Count);
        long total = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);
            var length = stream.Length;
            if (length > MaxFileBytes || total + length > MaxBatchBytes)
                throw new InvalidDataException(UiText.Format("ImportSizeLimitExceeded", Path.GetFileName(path)));

            var bytes = new byte[(int)length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (await stream.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
                throw new IOException(UiText.Format("FileSizeChangedWhileReading", Path.GetFileName(path)));

            total += length;
            sources.Add(new ImportSource(Path.GetFileName(path), Decode(bytes, fallbackEncoding)));
        }
        return sources;
    }

    private static string Decode(byte[] bytes, Encoding fallback)
    {
        try
        {
            var data = bytes.AsSpan();
            var utf8 = new UTF8Encoding(false, true);
            if (data.StartsWith("\uFEFF"u8))
                return utf8.GetString(data[3..]);
            if (data.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }))
                return new UTF32Encoding(false, true, true).GetString(data[4..]);
            if (data.StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
                return new UTF32Encoding(true, true, true).GetString(data[4..]);
            if (data.StartsWith(new byte[] { 0xff, 0xfe }))
                return new UnicodeEncoding(false, true, true).GetString(data[2..]);
            if (data.StartsWith(new byte[] { 0xfe, 0xff }))
                return new UnicodeEncoding(true, true, true).GetString(data[2..]);
            return utf8.GetString(data);
        }
        catch (DecoderFallbackException) when (fallback.CodePage != Encoding.UTF8.CodePage)
        {
            return fallback.GetString(bytes);
        }
    }
}
