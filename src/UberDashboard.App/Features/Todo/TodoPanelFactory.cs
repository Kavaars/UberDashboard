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

    /// <summary>Tworzy fabrykę z harmonogramem autozapisu.</summary>
    public TodoPanelFactory(PanelStateScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var viewModel = new TodoPanelViewModel(context.Notifier);
        var instance = new TodoPanelInstance(context, viewModel, _scheduler);

        return ValueTask.FromResult<IPanelInstance>(instance);
    }
}
