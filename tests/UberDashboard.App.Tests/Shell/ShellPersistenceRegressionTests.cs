using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Hosting;
using UberDashboard.App.Panels;
using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;
using UberDashboard.Infrastructure.DependencyInjection;
using UberDashboard.Infrastructure.Paths;
using UberDashboard.Platform.Windows.DependencyInjection;

namespace UberDashboard.App.Tests.Shell;

public sealed class ShellPersistenceRegressionTests
{
    [Fact]
    public async Task Cancelled_removal_keeps_instance_data_and_layout()
    {
        await using var fixture = new ShellFixture();
        var shell = fixture.Shell;
        await shell.InitializeAsync();
        await shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(shell.Root);
        var before = await fixture.Instances.GetAsync(leaf.InstanceId);
        await shell.RemovePanelCommand.ExecuteAsync(leaf);
        Assert.Equal(1, fixture.Confirmation.Calls);
        Assert.Same(leaf, shell.Root);
        Assert.Equal(before, await fixture.Instances.GetAsync(leaf.InstanceId));
        var saved = await fixture.Layouts.LoadAsync(shell.SelectedDashboard!.DashboardId);
        Assert.True(LayoutWalker.Contains(saved.Tree.Root, leaf.InstanceId));
    }

    [Fact]
    public async Task Hide_survives_autosave_and_reload_without_losing_todo()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        todo.NewItemText = "Testowa treść";
        todo.AddItemCommand.Execute(null);
        await f.Shell.HidePanelCommand.ExecuteAsync(leaf);
        var record = await f.Instances.GetAsync(leaf.InstanceId);
        Assert.True(record!.IsHidden);
        Assert.Contains("Testowa", record.StateJson);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Null(f.Shell.Root);
        Assert.Single(f.Shell.HiddenPanels);
        await f.Shell.RestorePanelCommand.ExecuteAsync(f.Shell.HiddenPanels[0]);
        var restored = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(leaf.InstanceId, restored.InstanceId);
        Assert.Single(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(restored.ViewModel).Items);
    }
    [Fact]
    public async Task Vertical_split_resize_and_move_survive_reload()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        await f.Shell.AddPanelAtAsync("todo", first.InstanceId, SplitAxis.Vertical);
        var split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Vertical, split.Axis);
        await f.Shell.ResizeSplitAsync(split.SplitId, .65, 1200);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(.65, split.Ratio, 5);
        var second = Assert.IsType<PanelLeafViewModel>(split.Second);
        await f.Shell.MovePanelAsync(second.InstanceId, first.InstanceId, DropZone.Left);
        Assert.Equal(SplitAxis.Vertical, Assert.IsType<PanelSplitViewModel>(f.Shell.Root).Axis);
        f.Shell.IsEditMode = true;
        await f.Shell.MovePanelAsync(second.InstanceId, first.InstanceId, DropZone.Left);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Horizontal, split.Axis);
        Assert.Equal(second.InstanceId, Assert.IsType<PanelLeafViewModel>(split.First).InstanceId);
    }

    [Fact]
    public async Task Replace_creates_new_identity_and_keeps_old_state_hidden()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var old = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(old.ViewModel);
        todo.NewItemText = "Zachowaj";
        todo.AddItemCommand.Execute(null);
        await f.Shell.AddPanelAtAsync("weather", old.InstanceId, SplitAxis.Vertical, true);
        var replacement = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.NotEqual(old.InstanceId, replacement.InstanceId);
        var record = await f.Instances.GetAsync(old.InstanceId);
        Assert.True(record!.IsHidden);
        Assert.Contains("Zachowaj", record.StateJson);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(replacement.InstanceId, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).InstanceId);
        Assert.Equal(old.InstanceId, Assert.Single(f.Shell.HiddenPanels).InstanceId);
    }

    [Fact]
    public async Task Missing_plugin_keeps_saved_position_and_state()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var record = await f.Instances.GetAsync(leaf.InstanceId);
        await f.Instances.UpsertAsync(record! with { PanelTypeId = "missing-type", PluginId = "pl.example.missing", StateJson = "{\"saved\":true}" });
        // Unload would save the still-active instance; start a new shell after unload.
        await f.Host.SuspendAsync(leaf.InstanceId, CancellationToken.None);
        await f.Instances.UpsertAsync(record! with { PanelTypeId = "missing-type", PluginId = "pl.example.missing", StateJson = "{\"saved\":true}" });
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        var placeholder = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(leaf.InstanceId, placeholder.InstanceId);
        Assert.IsType<UnavailablePanelViewModel>(placeholder.ViewModel);
        var saved = await f.Layouts.LoadAsync(f.Shell.SelectedDashboard!.DashboardId);
        Assert.True(LayoutWalker.Contains(saved.Tree.Root, leaf.InstanceId));
        Assert.Equal("{\"saved\":true}", (await f.Instances.GetAsync(leaf.InstanceId))!.StateJson);
    }

    [Fact]
    public async Task Confirmed_removal_only_deletes_selected_instance()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var second = Assert.IsType<PanelLeafViewModel>(Assert.IsType<PanelSplitViewModel>(f.Shell.Root).Second);
        f.Confirmation.Result = true;
        await f.Shell.RemovePanelCommand.ExecuteAsync(first);
        Assert.Null(await f.Instances.GetAsync(first.InstanceId));
        Assert.NotNull(await f.Instances.GetAsync(second.InstanceId));
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(second.InstanceId, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).InstanceId);
    }
    [Fact]
    public async Task Failed_layout_save_reports_failure_and_can_be_retried()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        f.LayoutFailure.Fail = true;
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        Assert.Contains("Nie zapisano układu", f.Shell.StatusMessage);
        Assert.True((await f.Layouts.LoadAsync(f.Shell.SelectedDashboard!.DashboardId)).Tree.IsEmpty);
        f.LayoutFailure.Fail = false;
        await f.Shell.RetrySaveLayoutCommand.ExecuteAsync(null);
        Assert.Equal("Układ zapisany.", f.Shell.StatusMessage);
        Assert.False((await f.Layouts.LoadAsync(f.Shell.SelectedDashboard.DashboardId)).Tree.IsEmpty);
    }

    [Fact]
    public async Task Three_panels_in_one_column_keep_original_content_and_reload()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        f.Shell.IsEditMode = true;
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(first.ViewModel);
        todo.NewItemText = "Zachowaj zadanie"; todo.AddItemCommand.Execute(null);
        var action = first.LayoutActions.Single(a => a.Header == "Trzy panele w kolumnie: Lista zadań");
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)action.Command).ExecuteAsync(null);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        var root = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Vertical, root.Axis);
        Assert.Equal(first.InstanceId, Assert.IsType<PanelLeafViewModel>(root.First).InstanceId);
        var sub = Assert.IsType<PanelSplitViewModel>(root.Second);
        Assert.Equal(SplitAxis.Vertical, sub.Axis);
        Assert.IsType<PanelLeafViewModel>(sub.First); Assert.IsType<PanelLeafViewModel>(sub.Second);
        Assert.Single(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(((PanelLeafViewModel)root.First).ViewModel).Items);
    }

    internal sealed class FailingLayoutStore(ILayoutStore inner) : ILayoutStore
    {
        public bool Fail { get; set; }
        public Task<LayoutLoadResult> LoadAsync(Guid dashboardId, CancellationToken cancellationToken = default) => inner.LoadAsync(dashboardId, cancellationToken);
        public Task SaveAsync(Guid dashboardId, LayoutTree tree, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException(new IOException("Testowy błąd układu")) : inner.SaveAsync(dashboardId, tree, cancellationToken);
    }
    internal sealed class ShellFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public ShellFixture()
        {
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "UberDashboard-audit", Guid.NewGuid().ToString("N")));
            paths.EnsureCreated();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddUberDashboardInfrastructure(paths);
            services.AddUberDashboardWindows();
            services.AddUberDashboardApp();
            services.AddSingleton<UberDashboard.Infrastructure.Database.Stores.LayoutStore>();
            services.AddSingleton<ILayoutStore>(p => LayoutFailure = new FailingLayoutStore(p.GetRequiredService<UberDashboard.Infrastructure.Database.Stores.LayoutStore>()));
            services.AddSingleton<IPanelRemovalConfirmation>(Confirmation);
            _provider = services.BuildServiceProvider();
            Shell = _provider.GetRequiredService<ShellViewModel>();
            Instances = _provider.GetRequiredService<IPanelInstanceStore>();
            Layouts = _provider.GetRequiredService<ILayoutStore>();
        }
        public FailingLayoutStore LayoutFailure { get; private set; } = null!;
        public PanelHostManager Host => _provider.GetRequiredService<PanelHostManager>();
        public ShellViewModel Shell { get; }
        public IPanelInstanceStore Instances { get; }
        public ILayoutStore Layouts { get; }
        public RecordingConfirmation Confirmation { get; } = new();
        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    internal sealed class RecordingConfirmation : IPanelRemovalConfirmation
    {
        public int Calls { get; private set; }
        public bool Result { get; set; }
        public bool Confirm(string displayName) { Calls++; return Result; }
    }
}
