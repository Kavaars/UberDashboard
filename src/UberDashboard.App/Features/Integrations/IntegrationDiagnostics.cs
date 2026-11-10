using System.IO;
using System.Net.Http;
using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;

namespace UberDashboard.App.Features.Integrations;

internal sealed class IntegrationDiagnostics(IAppSettingsStore settings, IAppPaths paths, IClock clock) : IIntegrationDiagnostics
{
    internal const string Key = "integration.diagnostics.enabled";
    private const long MaxBytes = 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _enabled = true;
    private bool _loaded;
    private volatile bool _writeFailed;
    internal bool WriteFailed => _writeFailed;
    private string FilePath => Path.Combine(paths.LogsDirectory, "integration.txt");

    public async Task<bool> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_loaded)
            {
                var value = await settings.GetAsync(Key).ConfigureAwait(false);
                _enabled = value is null || JsonSerializer.Deserialize<bool>(value);
                _loaded = true;
            }
            return _enabled;
        }
        finally { _gate.Release(); }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await settings.SetAsync(Key, JsonSerializer.Serialize(enabled), clock.UtcNow).ConfigureAwait(false);
            _enabled = enabled; _loaded = true;
        }
        finally { _gate.Release(); }
    }

    public void Report(string provider, string operation, Exception error) => _ = Task.Run(() => WriteAsync(provider, operation, error));

    internal async Task WriteAsync(string provider, string operation, Exception error)
    {
        try
        {
            await LoadAsync().ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_enabled) return;
                Directory.CreateDirectory(paths.LogsDirectory);
                // Only allow fixed categories and exception type names; never persist exception messages or URLs.
                var service = provider is "MicrosoftTodo" or "GoogleCalendar" ? provider : "Integration";
                var stage = operation is "background-sync" or "sync" or "oauth" or "refresh" or "setup" ? operation : "integration";
                var status = error is HttpRequestException { StatusCode: { } code } ? (int?)code : null;
                var record = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now.ToString("O"), level = "WARN", module = service,
                    operation = stage, errorType = error.GetType().Name, httpStatus = status,
                    reason = Reason(error), origin = Origin(error) });
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= MaxBytes) await File.WriteAllTextAsync(FilePath, "").ConfigureAwait(false);
                await File.AppendAllTextAsync(FilePath, record + Environment.NewLine).ConfigureAwait(false);
                _writeFailed = false;
            }
            finally { _gate.Release(); }
        }
        catch (Exception) { _writeFailed = true; }
    }

    private static string? Reason(Exception error) => error is not InvalidDataException ? null : error.Message switch
    {
        "Untrusted synchronization resource path." => "resource-path-invalid",
        "Continuation does not belong to this collection." => "continuation-collection-mismatch",
        "Untrusted synchronization continuation URL." => "continuation-origin-invalid",
        "Missing collection." => "response-collection-missing",
        "Empty API response." => "response-empty",
        "Missing API resource ID." => "response-id-missing",
        "Invalid resource ID." => "resource-id-invalid",
        "Invalid pagination." => "pagination-invalid",
        "Invalid related-item pagination." => "related-pagination-invalid",
        "Invalid related collection snapshot." => "related-snapshot-invalid",
        _ => "invalid-data-other",
    };

    private static string? Origin(Exception error)
    {
        foreach (var frame in new System.Diagnostics.StackTrace(error).GetFrames())
        {
            var name = frame.GetMethod()?.DeclaringType?.FullName;
            if (name?.StartsWith("UberDashboard.Infrastructure.Integrations.MicrosoftTodoProvider", StringComparison.Ordinal) == true) return "MicrosoftTodoProvider";
            if (name?.StartsWith("UberDashboard.Infrastructure.Integrations.SyncHttpProvider", StringComparison.Ordinal) == true) return "SyncHttpProvider";
            if (name?.StartsWith("UberDashboard.Application.Synchronization.SyncCoordinator", StringComparison.Ordinal) == true) return "SyncCoordinator";
        }
        return null;
    }

    public async Task<string> ReadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return File.Exists(FilePath) ? await File.ReadAllTextAsync(FilePath).ConfigureAwait(false) : ""; }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (File.Exists(FilePath)) await File.WriteAllTextAsync(FilePath, "").ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
