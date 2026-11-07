using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Panels;

namespace UberDashboard.App.Features.Todo;

/// <summary>
/// Fabryka instancji panelu listy zadań. Otrzymuje wspólny harmonogram autozapisu,
/// dzięki czemu ten sam mechanizm obsługuje wszystkie instancje niezależnie od typu.
/// </summary>
public sealed class TodoPanelFactory : IPanelFactory
{
    private readonly PanelStateScheduler _scheduler;
    private readonly IPanelInstanceStore? _instances;
    private readonly MicrosoftTodoSettings? _settings;
    private readonly Features.Integrations.IntegrationSyncService? _sync;
    private readonly MicrosoftTodoSession? _session;
    private readonly MicrosoftTodoRefresh? _refresh;

    /// <summary>Tworzy fabrykę z harmonogramem autozapisu.</summary>
    public TodoPanelFactory(PanelStateScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    internal TodoPanelFactory(PanelStateScheduler scheduler, IPanelInstanceStore instances, MicrosoftTodoSettings settings,
        Features.Integrations.IntegrationSyncService? sync = null, MicrosoftTodoSession? session = null, MicrosoftTodoRefresh? refresh = null) : this(scheduler)
    {
        _instances = instances;
        _settings = settings;
        _sync = sync; _session = session; _refresh = refresh;
    }

    /// <inheritdoc />
    public async ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var viewModel = new TodoPanelViewModel(context.Notifier);
        var instance = new TodoPanelInstance(context, viewModel, _scheduler);

        if (_instances is not null && _settings is not null &&
            (await _instances.GetAsync(context.InstanceId, cancellationToken))?.ConfigJson is { } config)
        {
            MicrosoftTodoLink? link = null;
            try { link = JsonSerializer.Deserialize<MicrosoftTodoLink>(config); }
            catch (JsonException) { /* Older panel configuration may use a different format. */ }
            if (link is { AccountId.Length: > 0, ListId.Length: > 0 } && link.ClientId != Guid.Empty)
                instance.AttachLink(link, _settings, _sync, _session, _refresh);
        }

        return instance;
    }
}
