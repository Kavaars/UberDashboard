using UberDashboard.App.Panels;
using UberDashboard.Contracts.Panels;

namespace UberDashboard.App.Features.Notes;

/// <summary>
/// Fabryka instancji panelu notatnika. Otrzymuje wspólny harmonogram autozapisu,
/// dzięki czemu ten sam mechanizm obsługuje wszystkie instancje niezależnie od typu.
/// </summary>
public sealed class NotesPanelFactory : IPanelFactory
{
    private readonly PanelStateScheduler _scheduler;

    /// <summary>Tworzy fabrykę z harmonogramem autozapisu.</summary>
    public NotesPanelFactory(PanelStateScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var viewModel = new NotesPanelViewModel(context.Notifier);
        var instance = new NotesPanelInstance(context, viewModel, _scheduler);

        return ValueTask.FromResult<IPanelInstance>(instance);
    }
}
