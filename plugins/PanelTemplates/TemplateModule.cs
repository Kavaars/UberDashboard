using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Controls;
using UberDashboard.Contracts.Panels;
using UberDashboard.Contracts.Plugins;
using UberDashboard.Core.Layout;
using UberDashboard.Core.ModularPanels;

namespace PanelTemplates.Plugin;

public sealed class TemplateModule : IPluginModule
{
    public ValueTask InitializeAsync(IPluginHostContext hostContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        hostContext.RegisterPanel(new TemplateDescriptor(false));
        hostContext.RegisterPanel(new TemplateDescriptor(true));
        return ValueTask.CompletedTask;
    }
}

internal sealed class TemplateDescriptor(bool modular) : IPanelDescriptor, IPanelFactory
{
    public string TypeId => "pl.uberdashboard.paneltemplates." + (modular ? "modular" : "simple");
    public string DisplayName => TemplateText.Get(modular ? "Wzór panelu modułowego" : "Wzór panelu zwykłego", modular ? "Modular panel template" : "Simple panel template");
    public string? IconKey => null;
    public MinSize MinSize => new(220, 140);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) => ValueTask.FromResult<IPanelInstance>(new TemplateInstance(context, modular));
}

internal sealed class TemplateInstance : IPanelInstance, IPanelStateNotifications
{
    private readonly TemplateModel _model;
    public TemplateInstance(PanelContext context, bool modular)
    {
        InstanceId = context.InstanceId;
        _model = new TemplateModel(modular);
        _model.Changed += ForwardChange;
    }
    public PanelInstanceId InstanceId { get; }
    public object ViewModel => _model.ContentModel;
    public event EventHandler? StateChanged;
    public long ChangeVersion { get; private set; }
    public void OnStatePersisted() { }
    private void ForwardChange(object? sender, EventArgs e) { ChangeVersion++; StateChanged?.Invoke(this, EventArgs.Empty); }
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        var document = JsonSerializer.Deserialize<TemplateDocument>(state.Span) ?? new();
        if (document.SchemaVersion != 1) throw new InvalidOperationException("Unsupported panel state version.");
        _model.Restore(document);
        return ValueTask.CompletedTask;
    }
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(_model.Snapshot()));
    public ValueTask SuspendAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask ResumeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() { _model.Changed -= ForwardChange; return ValueTask.CompletedTask; }
}

public sealed record TemplateItem(string Id, string Name);
public sealed record TemplateDocument
{
    public int SchemaVersion { get; init; } = 1;
    public TemplateItem[]? Items { get; init; }
    public ModulePosition[]? Positions { get; init; }
}

public sealed class TemplateModel : INotifyPropertyChanged, IPanelContent, IModularPanel
{
    private readonly bool _modular;
    private IReadOnlyList<ModulePosition> _positions = [];
    private bool _edit;
    public TemplateModel(bool modular)
    {
        _modular = modular;
        if (modular)
        {
            Modules.Add(new("first", TemplateText.Get("Element 1", "Item 1")));
            Modules.Add(new("second", TemplateText.Get("Element 2", "Item 2")));
            Content = new ModularView { DataContext = this };
            ContentModel = this;
        }
        else
        {
            var text = new TextBlock { Text = TemplateText.Get("Pusty panel", "Empty panel"), Margin = new System.Windows.Thickness(12) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Content = text;
            ContentModel = new SimpleContent(Content);
        }
    }
    public object Content { get; }
    public object ContentModel { get; }
    public ObservableCollection<TemplateItem> Modules { get; } = [];
    public bool IsModuleEditMode { get => _edit; set { if (_edit == value) return; _edit = value; Notify(nameof(IsModuleEditMode)); } }
    public IReadOnlyList<ModulePosition> ModulePositions => ModuleGrid.Restore(Modules.Select(x => x.Id), _positions, 4);
    public string? GetModuleId(object item) => (item as TemplateItem)?.Id;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
    public void MoveModuleToCell(string id, int column, int row)
    {
        if (!_modular || !IsModuleEditMode || ModulePositions.All(p => p.Id != id)) return;
        var result = ModuleGrid.Move(ModulePositions, id, column, row, 4);
        if (result.SequenceEqual(ModulePositions)) return;
        _positions = result; Notify(nameof(ModulePositions)); Changed?.Invoke(this, EventArgs.Empty);
    }
    public TemplateDocument Snapshot() => new() { Items = [.. Modules], Positions = [.. ModulePositions] };
    public void Restore(TemplateDocument document)
    {
        if (document.Items is not null)
        {
            Modules.Clear();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.Items)
                if (item is not null && !string.IsNullOrWhiteSpace(item.Id) && ids.Add(item.Id)) Modules.Add(item);
        }
        _positions = document.Positions ?? [];
        Notify(nameof(ModulePositions));
    }
    private sealed record SimpleContent(object Content) : IPanelContent;
}

internal static class TemplateText
{
    public static string Get(string pl, string en) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl" ? pl : en;
}
