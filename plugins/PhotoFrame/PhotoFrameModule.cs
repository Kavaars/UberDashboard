using System.Text.Json;
using UberDashboard.Contracts.Panels;
using UberDashboard.Contracts.Plugins;
using UberDashboard.Core.Layout;

namespace PhotoFrame.Plugin;

public sealed class PhotoFrameModule : IPluginModule
{
    public ValueTask InitializeAsync(IPluginHostContext hostContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        hostContext.RegisterPanel(new PhotoFrameDescriptor());
        return ValueTask.CompletedTask;
    }
}

public sealed class PhotoFrameDescriptor : IPanelDescriptor, IPanelFactory
{
    public string TypeId => "pl.uberdashboard.photoframe.frame";
    public string DisplayName => "Ramka na zdjęcia";
    public string? IconKey => "panel.photos";
    public MinSize MinSize => new(220, 180);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult<IPanelInstance>(new PhotoFrameInstance(context));
}

public sealed class PhotoFrameInstance : IPanelInstance, IPanelStateNotifications
{
    private readonly PhotoFrameModel _model;
    public PhotoFrameInstance(PanelContext context)
    {
        InstanceId = context.InstanceId;
        _model = new PhotoFrameModel(context);
        _model.StateChanged += ForwardChange;
    }
    public PanelInstanceId InstanceId { get; }
    public object ViewModel => _model;
    public event EventHandler? StateChanged;
    private void ForwardChange(object? sender, EventArgs args) => StateChanged?.Invoke(this, args);
    public long ChangeVersion => _model.ChangeVersion;
    public void OnStatePersisted() => _model.MarkPersisted();
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public async ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        var document = JsonSerializer.Deserialize<PhotoFrameDocument>(state.Span) ?? new();
        if (document.SchemaVersion != 1) throw new InvalidOperationException("Nieobsługiwana wersja danych ramki. Zachowano oryginalny stan.");
        await _model.LoadAsync(document, cancellationToken);
    }
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(_model.Snapshot()));
    public ValueTask SuspendAsync(CancellationToken cancellationToken) { _model.Stop(); return ValueTask.CompletedTask; }
    public async ValueTask ResumeAsync(CancellationToken cancellationToken) => await _model.ReloadAsync(cancellationToken);
    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) { _model.Stop(); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync()
    {
        _model.StateChanged -= ForwardChange;
        _model.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record PhotoFrameDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string SourcePath { get; init; } = "";
    public bool IsFolder { get; init; }
    public int IntervalSeconds { get; init; } = 10;
    public PhotoFrameDocument Normalize() => this with { SourcePath = SourcePath ?? "", IntervalSeconds = Math.Clamp(IntervalSeconds, 1, 86400) };
}
