using UberDashboard.Core.Localization;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Shell;
using UberDashboard.App.Toasts;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;

namespace UberDashboard.App.Panels;

/// <summary>Serializuje operacje zakładek jednej grupy i cykl życia ich instancji.</summary>
public sealed class PanelGroupSession : ObservableObject
{
    private readonly IPanelTabGroupStore _groups;
    private readonly IPanelInstanceStore _instances;
    private readonly PanelHostManager _host;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PanelInstanceRecord _root;
    private readonly IToastService _toasts;
    public PanelTabGroup Group { get; private set; }
    public object ActiveViewModel { get; private set; }
    public event EventHandler? Changed;
    private bool _isBusy;
    private bool _closed;
    public bool IsClosed => _closed;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public PanelGroupSession(PanelInstanceRecord root, PanelTabGroup group, IPanelTabGroupStore groups, IPanelInstanceStore instances, PanelHostManager host, IClock clock, IToastService toasts)
    {
        _root = root; Group = group; _groups = groups; _instances = instances; _host = host; _clock = clock; _toasts = toasts;
        ActiveViewModel = new UnavailablePanelViewModel(root.PluginId, root.DisplayName);
    }

    public void ReportError(Exception error) => _toasts.Show(UiText.Format("TheTabOperationFailed", error.Message), ToastSeverity.Error);
    public string ImportDisplayName => _root.DisplayName;
    internal bool ImportsTasks => _root.PanelTypeId == Features.Todo.TodoPanelDescriptor.TypeId;
    internal bool SupportsFileImport => ImportsTasks || _root.PanelTypeId == Features.Notes.NotesPanelDescriptor.TypeId;

    /// <summary>Adds prepared documents together and leaves the current editor active.</summary>
    internal async Task ImportTabsAsync(IReadOnlyList<ImportedPanelTab> tabs, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try
        {
            EnsureOpen();
            if (!SupportsFileImport)
                throw new InvalidOperationException(UiText.Get("ThisPanelDoesNotSupportImport"));

            var next = Group;
            var records = new List<PanelInstanceRecord>(tabs.Count);
            var now = _clock.UtcNow;
            foreach (var tab in tabs)
            {
                var linkJson = tab.Link is null ? null : JsonSerializer.Serialize(tab.Link);
                var id = linkJson is null ? PanelInstanceId.New() : LinkedTabId(Group.GroupId, tab.Link!);
                if (next.Tabs.Any(existing => existing.InstanceId == id)) continue;
                next = next.Add(id, tab.Name);
                records.Add(_root with
                {
                    InstanceId = id, DisplayName = tab.Name, IsHidden = true, StateJson = tab.StateJson,
                    DataSchemaVersion = tab.SchemaVersion, ConfigJson = linkJson, RestoreHintJson = null,
                    CreatedAtUtc = now, UpdatedAtUtc = now,
                });
            }
            if (records.Count == 0) return;
            await _groups.AddBatchAsync(Group, next, records, token).ConfigureAwait(true);
            Group = next;
            Changed?.Invoke(this, EventArgs.Empty);
            _toasts.Show(UiText.Format("ImportedTabs", records.Count), ToastSeverity.Success);
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    internal static PanelInstanceId LinkedTabId(PanelInstanceId group, Features.Todo.MicrosoftTodoLink link)
    {
        var key = JsonSerializer.SerializeToUtf8Bytes(new { Group = group.Value, Link = link });
        return new PanelInstanceId(new Guid(SHA256.HashData(key).AsSpan(0, 16)));
    }

    public async Task ActivateAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        try { _closed = false; await SetViewAsync(Group.ActiveId, token).ConfigureAwait(true); }
        finally { _gate.Release(); }
    }

    public async Task SelectAsync(PanelInstanceId id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try { EnsureOpen(); await SelectCoreAsync(id, token).ConfigureAwait(true); }
        finally { IsBusy = false; _gate.Release(); }
    }

    private async Task SelectCoreAsync(PanelInstanceId id, CancellationToken token)
    {
        if (id == Group.ActiveId) return;
        var next = Group.Select(id);
        var previous = Group.ActiveId;
        var record = await _instances.GetAsync(id, token).ConfigureAwait(true) ?? throw new InvalidOperationException(UiText.Get("TabStateIsMissing"));
        var instance = await _host.ActivateAsync(record, token).ConfigureAwait(true);
        if (instance is null) throw new InvalidOperationException(UiText.Get("TheTabSPanelIsUnavailable"));
        try
        {
            await _host.SuspendAsync(previous, token).ConfigureAwait(true);
            await _groups.UpdateAsync(Group, next, token).ConfigureAwait(true);
        }
        catch
        {
            try { await _host.SuspendAsync(id, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception cleanupError) { ReportError(cleanupError); }
            try { await SetViewAsync(previous, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception restoreError) { ReportError(restoreError); }
            throw;
        }
        Group = next; ActiveViewModel = instance.ViewModel; Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try
        {
            EnsureOpen();
            var id = PanelInstanceId.New();
            var next = Group.Add(id, $"{_root.DisplayName} {Group.Tabs.Length + 1}");
            var now = _clock.UtcNow;
            var record = _root with { InstanceId = id, IsHidden = true, StateJson = null, ConfigJson = null, RestoreHintJson = null, CreatedAtUtc = now, UpdatedAtUtc = now };
            await _groups.AddAsync(Group, next, record, token).ConfigureAwait(true);
            Group = next; Changed?.Invoke(this, EventArgs.Empty);
            await SelectCoreAsync(id, token).ConfigureAwait(true);
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    public async Task UpdateAsync(Func<PanelTabGroup, PanelTabGroup> update, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try { EnsureOpen(); var next = update(Group); await _groups.UpdateAsync(Group, next, token).ConfigureAwait(true); Group = next; Changed?.Invoke(this, EventArgs.Empty); }
        finally { IsBusy = false; _gate.Release(); }
    }

    public async Task<bool> NeedsDeletionConfirmationAsync(PanelInstanceId id, CancellationToken token = default)
    {
        if (_root.PanelTypeId != Features.Notes.NotesPanelDescriptor.TypeId) return true;
        if (id == Group.ActiveId && ActiveViewModel is Features.Notes.NotesPanelViewModel active)
            return Features.Notes.NotesPanelViewModel.HasContent(active.Snapshot());
        var record = await _instances.GetAsync(id, token).ConfigureAwait(true);
        if (record is null) return true;
        if (string.IsNullOrWhiteSpace(record.StateJson)) return false;
        try
        {
            var document = System.Text.Json.JsonSerializer.Deserialize<Features.Notes.NotesDocument>(record.StateJson);
            return document is null || Features.Notes.NotesPanelViewModel.HasContent(document);
        }
        catch (System.Text.Json.JsonException) { return true; }
    }

    public async Task RemoveAsync(PanelInstanceId id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try
        {
            EnsureOpen();
            var next = Group.Remove(id);
            var active = id == Group.ActiveId;
            var wasHosted = _host.IsActive(id);
            if (!wasHosted)
            {
                var record = await _instances.GetAsync(id, token).ConfigureAwait(true);
                if (record is not null) await _host.ActivateAsync(record, token).ConfigureAwait(true);
            }
            try { await _host.RemoveWithCommitAsync(id, ct => _groups.RemoveAsync(Group, next, id, ct), token).ConfigureAwait(true); }
            catch
            {
                if (!wasHosted && _host.IsActive(id))
                {
                    try { await _host.SuspendAsync(id, CancellationToken.None).ConfigureAwait(true); }
                    catch (Exception cleanupError) { ReportError(cleanupError); }
                }
                throw;
            }
            Group = next;
            if (active) await SetViewAsync(next.ActiveId, token).ConfigureAwait(true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    public async Task FlushAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try { if (!_closed) await _host.FlushAsync(Group.ActiveId, token).ConfigureAwait(true); }
        finally { IsBusy = false; _gate.Release(); }
    }

    public async Task<bool> SuspendAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        try
        {
            if (_closed) return false;
            _closed = true;
            try { await _host.SuspendAsync(Group.ActiveId, token).ConfigureAwait(true); return true; }
            catch { _closed = false; throw; }
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    public async Task DeleteGroupAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(true);
        IsBusy = true;
        var temporarilyHosted = new List<PanelInstanceId>();
        try
        {
            EnsureOpen();
            foreach (var tab in Group.Tabs)
            {
                if (_host.IsActive(tab.InstanceId)) continue;
                var record = await _instances.GetAsync(tab.InstanceId, token).ConfigureAwait(true);
                if (record is not null && await _host.ActivateAsync(record, token).ConfigureAwait(true) is not null)
                    temporarilyHosted.Add(tab.InstanceId);
            }
            await _host.RemoveManyWithCommitAsync(Group.Tabs.Select(t => t.InstanceId), ct => _groups.DeleteAsync(Group, ct), token).ConfigureAwait(true);
            _closed = true;
        }
        catch
        {
            foreach (var id in temporarilyHosted)
            {
                if (!_host.IsActive(id)) continue;
                try { await _host.SuspendAsync(id, CancellationToken.None).ConfigureAwait(true); }
                catch (Exception cleanupError) { ReportError(cleanupError); }
            }
            throw;
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    private void EnsureOpen()
    {
        if (_closed) throw new InvalidOperationException(UiText.Get("ThePanelWasClosedOrHidden"));
    }

    private async Task SetViewAsync(PanelInstanceId id, CancellationToken token)
    {
        var record = await _instances.GetAsync(id, token).ConfigureAwait(true);
        var instance = record is null ? null : await _host.ActivateAsync(record, token).ConfigureAwait(true);
        ActiveViewModel = instance?.ViewModel ?? new UnavailablePanelViewModel(_root.PluginId, _root.DisplayName);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
