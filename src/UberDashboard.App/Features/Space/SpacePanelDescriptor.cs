using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Space;

/// <summary>Puste miejsce używa istniejącego zapisu liści, bez migracji układu użytkownika.</summary>
public sealed class SpacePanelDescriptor : IPanelDescriptor, IPanelFactory
{
    public const string Id = "builtin.layout-space";
    public string TypeId => Id;
    public string DisplayName => "Wolne miejsce";
    public string? IconKey => null;
    public MinSize MinSize => new(48, 48);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new Instance(context.InstanceId));

    private sealed class Instance(PanelInstanceId id) : IPanelInstance
    {
        public PanelInstanceId InstanceId => id;
        public object ViewModel { get; } = new SpacePanelViewModel();
        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ReadOnlyMemory<byte>>("{}"u8.ToArray());
        public ValueTask SuspendAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ResumeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class SpacePanelViewModel;
