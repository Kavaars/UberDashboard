using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UberDashboard.App.Toasts;
using UberDashboard.Contracts.Host;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Panels;

/// <summary>
/// Kanał logowania instancji panelu. Każdy wpis jest opatrzony etykietą z wtyczką,
/// pulpitem i identyfikatorem instancji, dzięki czemu w logu aplikacji widać
/// pochodzenie komunikatu nawet przy wielu instancjach tego samego typu.
/// </summary>
internal sealed class PanelLogger : IPanelLogger
{
    private readonly ILogger _logger;
    private readonly string _label;

    public PanelLogger(ILoggerFactory loggerFactory, string pluginId, Guid dashboardId, PanelInstanceId instanceId)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger($"Panel:{pluginId}");
        _label = $"[Panel {pluginId}/{instanceId}] [Pulpit {dashboardId:D}]";
    }

    /// <inheritdoc />
    public void Log(PanelLogLevel level, string message, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var text = properties is null || properties.Count == 0
            ? $"{_label} {message}"
            : $"{_label} {message} {JsonSerializer.Serialize(properties)}";

        _logger.Log(MapLevel(level), "{PanelMessage}", text);
    }

    private static LogLevel MapLevel(PanelLogLevel level) => level switch
    {
        PanelLogLevel.Debug => LogLevel.Debug,
        PanelLogLevel.Information => LogLevel.Information,
        PanelLogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Error,
    };
}

/// <summary>
/// Kanał powiadomień instancji panelu. Zamienia komunikaty panelu na toasty
/// globalne, więc publikowanie nigdy nie otwiera okna modalnego.
/// </summary>
internal sealed class PanelNotifier : IPanelNotifier
{
    private readonly IToastService _toasts;

    public PanelNotifier(IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(toasts);
        _toasts = toasts;
    }

    /// <inheritdoc />
    public void Publish(string message, PanelNotificationSeverity severity = PanelNotificationSeverity.Information)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _toasts.Show(message, severity switch
        {
            PanelNotificationSeverity.Success => ToastSeverity.Success,
            PanelNotificationSeverity.Warning => ToastSeverity.Warning,
            PanelNotificationSeverity.Error => ToastSeverity.Error,
            _ => ToastSeverity.Information,
        });
    }
}

/// <summary>
/// Magazyn plików instancji panelu. Nazwy plików są redukowane do samej nazwy,
/// dlatego panel nie może wyjść poza własny katalog danych.
/// </summary>
internal sealed class PanelStorage : IPanelStorage
{
    private readonly string _directory;

    public PanelStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = Resolve(fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var path = Resolve(fileName);
        await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = Resolve(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var files = Directory.GetFiles(_directory);
        var names = new List<string>(files.Length);
        foreach (var file in files)
        {
            names.Add(Path.GetFileName(file));
        }

        return names;
    }

    private string Resolve(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(_directory, Path.GetFileName(fileName));
    }
}
