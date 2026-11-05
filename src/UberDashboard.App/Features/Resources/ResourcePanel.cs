using UberDashboard.Core.Localization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Resources;

internal sealed class ResourcePanelDescriptor(HardwareSampler sampler) : IPanelDescriptor, IPanelFactory
{
    public const string Id = "system-resources";
    public string TypeId => Id;
    public string DisplayName => UiText.Get("ResourceMonitor");
    public string? IconKey => null;
    public MinSize MinSize => new(270, 150);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;

    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new ResourcePanel(context, sampler));
}

public sealed partial class ResourcePanel : ObservableObject, IPanelInstance
{
    private readonly PanelContext _context;
    private readonly HardwareSampler _sampler;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _reportedError;

    [ObservableProperty] private IReadOnlyList<ResourceReading> _readings = [];
    [ObservableProperty] private string _status = UiText.Get("ReadingSensors");

    public PanelInstanceId InstanceId => _context.InstanceId;
    object IPanelInstance.ViewModel => this;

    internal ResourcePanel(PanelContext context, HardwareSampler sampler)
    {
        _context = context;
        _sampler = sampler;
        _timer.Tick += OnTick;
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ResumeAsync(cancellationToken);
    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ReadOnlyMemory<byte>>("{}"u8.ToArray());

    public ValueTask SuspendAsync(CancellationToken cancellationToken)
    {
        _timer.Stop();
        SampleCommand.Cancel();
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        if (_context.CloseToken.IsCancellationRequested) return ValueTask.CompletedTask;
        OnTick(null, EventArgs.Empty);
        _timer.Start();
        return ValueTask.CompletedTask;
    }

    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => SuspendAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _timer.Tick -= OnTick;
        return SuspendAsync(CancellationToken.None);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (SampleCommand.CanExecute(null)) SampleCommand.Execute(null);
    }

    [RelayCommand]
    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _context.CloseToken);
        try
        {
            var result = await Task.Run(() => _sampler.Sample(linked.Token), linked.Token).ConfigureAwait(true);
            linked.Token.ThrowIfCancellationRequested();
            Readings = result;
            Status = UiText.Format("ReadingAt", DateTime.Now);
            _reportedError = false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Readings = [];
            Status = UiText.Get("CouldNotReadSensorsTheMonitorWill");
            if (!_reportedError)
            {
                _context.Logger.Error("Nie udało się odczytać czujników sprzętowych.", ex);
                _reportedError = true;
            }
        }
    }
}
