using UberDashboard.Core.Localization;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.App.Features.Todo;

/// <summary>Odświeża zatwierdzone listy bez otwierania logowania w tle.</summary>
internal sealed class MicrosoftTodoRefresh(MicrosoftTodoSession session, MicrosoftTodoSettings settings, ILogger<MicrosoftTodoRefresh> logger) : IDisposable
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    internal static MicrosoftTodoReader Reader { get; } = new(Http);
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _pending;
    private bool _paused;
    public string? LastError { get; private set; }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += Tick;
        _timer.Start();
        Tick(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _paused = true;
        _pending?.Cancel();
    }

    public void Resume() => _paused = false;

    private async void Tick(object? sender, EventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_paused || _pending is not null) return;

        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        var token = cancellation.Token;
        try
        {
            var saved = await settings.LoadAsync(token);
            if (saved.AccountId is not { } accountId || saved.SelectedLists.Length == 0) return;

            var credential = await session.TrySignInSilentlyAsync(saved.ClientId, accountId, token);
            if (credential is null)
            {
                LastError = UiText.Get("ReconnectTheMicrosoftAccountInTheSetup");
                return;
            }

            var selected = saved.SelectedLists.ToHashSet();
            var snapshots = new List<MicrosoftTodoSnapshot>();
            var unavailable = false;
            foreach (var item in saved.Snapshots.Where(item =>
                item.Link.ClientId == saved.ClientId && item.Link.AccountId == accountId && selected.Contains(item.Link.ListId)))
            {
                try
                {
                    var tasks = await Reader.ReadTasksAsync(item.Link.ListId, credential.AccessToken, token);
                    var document = await Task.Run(() => MicrosoftTodoSettings.CreateDocument(tasks), token);
                    snapshots.Add(item with { Document = document });
                }
                catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    unavailable = true;
                }
            }
            token.ThrowIfCancellationRequested();
            if (snapshots.Count > 0)
                await settings.SaveAsync(saved.UpdateSnapshots(snapshots.ToArray(), DateTimeOffset.UtcNow), token);
            LastError = unavailable ? UiText.Get("SomeListsAreUnavailableTheirLastSaved") : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (LastError is null) logger.LogWarning("Microsoft To Do refresh failed with {ErrorType}", error.GetType().Name);
            LastError = UiText.Get("CouldNotRefreshTheListsAutomaticallyThe");
        }
        finally { _pending = null; }
    }

    public void ClearError() => LastError = null;

    public void Dispose()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= Tick;
        _timer = null;
        _pending?.Cancel();
    }
}
