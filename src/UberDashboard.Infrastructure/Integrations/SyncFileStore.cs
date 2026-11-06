using System.Security.Cryptography;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed class SyncFileStore(IAppPaths paths) : ISyncFileStore
{
    private string Root => Path.GetFullPath(Path.Combine(paths.DataDirectory, "IntegrationFiles"));
    private string FilePath(Guid id) => id == Guid.Empty ? throw new ArgumentException("Invalid file identity.", nameof(id)) : Path.Combine(Root, id.ToString("N") + ".bin");

    public Task<SyncStagedFile> StageAsync(string source, long maximum, CancellationToken token = default) => Task.Run(async () =>
    {
        var name = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(c => char.IsControl(c) || c is '/' or '\\')) throw new InvalidDataException("Invalid file name.");
        Directory.CreateDirectory(Root); var id = Guid.NewGuid(); var target = FilePath(id); var temporary = target + ".pending";
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length > maximum) throw new InvalidDataException("File exceeds provider limit.");
            var size = input.Length; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[1024 * 1024]; long copied = 0; int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                { copied += count; if (copied > maximum || copied > size) throw new InvalidDataException("Source file changed."); hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
                if (copied != size) throw new InvalidDataException("Source file changed.");
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target);
            return new SyncStagedFile(id, name, "application/octet-stream", size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);

    public Task<Stream> OpenAsync(SyncStagedFile file, CancellationToken token = default) => Task.Run<Stream>(() =>
    {
        var stream = new FileStream(FilePath(file.Id), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == file.Size) return stream;
        stream.Dispose(); throw new InvalidDataException("Staged file size changed.");
    }, token);

    public async Task<bool> VerifyAsync(SyncStagedFile file, CancellationToken token = default)
    {
        await using var input = await OpenAsync(file, token).ConfigureAwait(false);
        var hash = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public Task DeleteAsync(Guid id, CancellationToken token = default) => Task.Run(() => File.Delete(FilePath(id)), token);
}
