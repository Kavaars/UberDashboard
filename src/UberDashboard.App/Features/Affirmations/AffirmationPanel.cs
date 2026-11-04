using System.Text.Json;
using System.Windows.Threading;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Panels;
using UberDashboard.Contracts.Host;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Affirmations;

public sealed class AffirmationPanelDescriptor(IPanelFactory factory) : IPanelDescriptor
{
    public string TypeId => "affirmations";
    public string DisplayName => "Afirmacje";
    public string? IconKey => null;
    public MinSize MinSize => new(280, 150);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => factory;
}
public sealed class AffirmationPanelFactory(PanelStateScheduler scheduler) : IPanelFactory
{
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new AffirmationPanelInstance(context, scheduler));
}
public sealed class AffirmationPanelInstance : IPanelInstance
{
    private readonly PanelContext _context;
    private readonly PanelStateScheduler _scheduler;
    private readonly AffirmationPanelViewModel _vm = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    public AffirmationPanelInstance(PanelContext context, PanelStateScheduler scheduler)
    {
        _context = context; _scheduler = scheduler;
        _timer.Tick += OnTick;
    }
    public PanelInstanceId InstanceId => _context.InstanceId;
    public object ViewModel => _vm;
    public ValueTask InitializeAsync(CancellationToken cancellationToken) { _vm.Changed += OnChanged; _timer.Start(); return ValueTask.CompletedTask; }
    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        try { _vm.Load(JsonSerializer.Deserialize<AffirmationDocument>(state.Span) ?? new()); }
        catch (JsonException ex) { _context.Logger.Error("Nie udało się odtworzyć afirmacji.", ex); }
        return ValueTask.CompletedTask;
    }
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(_vm.Snapshot()));
    public ValueTask SuspendAsync(CancellationToken cancellationToken) { _timer.Stop(); return ValueTask.CompletedTask; }
    public ValueTask ResumeAsync(CancellationToken cancellationToken) { _vm.CheckReset(); _timer.Start(); return ValueTask.CompletedTask; }
    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() { _timer.Stop(); _timer.Tick -= OnTick; _vm.Changed -= OnChanged; return ValueTask.CompletedTask; }
    private void OnTick(object? sender, EventArgs e) => _vm.CheckReset();
    private void OnChanged(object? sender, EventArgs e) => _scheduler.RequestSave(InstanceId);
}
