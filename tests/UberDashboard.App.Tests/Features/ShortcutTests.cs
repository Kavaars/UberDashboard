using System.Text.Json;
using UberDashboard.App.Features.Shortcuts;
using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Tests.Features;

public sealed class ShortcutTests
{
    [Theory]
    [InlineData("https://example.org/a?q=1")]
    [InlineData("http://localhost:8080")]
    [InlineData(@"C:\Apps\My App.exe")]
    [InlineData(@"C:\Users\Public")]
    [InlineData(@"C:\Links\App.lnk")]
    public void Accepts_web_and_absolute_local_targets(string target) =>
        Assert.Equal(target, ShortcutTarget.Normalize(target));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("cmd /c del test")]
    [InlineData("relative.exe")]
    [InlineData("")]
    public void Rejects_commands_and_unapproved_schemes(string target) =>
        Assert.Throws<ArgumentException>(() => ShortcutTarget.Normalize(target));

    [Fact]
    public void Expands_environment_and_trims_quoted_paths()
    {
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\notepad.exe"),
            ShortcutTarget.Normalize("  \"%SystemRoot%\\notepad.exe\"  "));
    }

    [Fact]
    public void Editing_uses_a_copy_and_commits_only_valid_input()
    {
        var model = new ShortcutsModel();
        var item = new ShortcutEntry { Name = "Old", Target = @"C:\App.exe" };
        model.Items.Add(item);
        var draft = item with { Name = "New", Target = "invalid" };
        Assert.Throws<ArgumentException>(() => model.Save(draft, item));
        Assert.Same(item, model.Items[0]);
        model.Save(draft with { Target = "https://example.org" }, item);
        Assert.Equal("New", model.Items[0].Name);
        Assert.Equal(item.Id, model.Items[0].Id);
    }

    [Fact]
    public void Order_and_appearance_survive_serialization()
    {
        var model = new ShortcutsModel { Mode = ShortcutViewMode.Tiles, Wallpaper = @"C:\wall.jpg" };
        var a = model.Save(new() { Name = "A", Target = @"C:\A.exe", Background = "#112233", Foreground = "#FFFFFF", Icon = "★", ImagePath = @"C:\a.png" });
        var b = model.Save(new() { Name = "B", Target = "https://example.org" });
        model.Move(b, -1);
        model.Move(b, -1);
        var restored = ShortcutsModel.Restore(model.Serialize());
        Assert.Equal([b.Id, a.Id], restored.Items.Select(item => item.Id));
        Assert.Equal(ShortcutViewMode.Tiles, restored.Mode);
        Assert.Equal(model.Wallpaper, restored.Wallpaper);
        Assert.Equal(a, restored.Items[1]);
    }

    [Fact]
    public void Restore_repairs_invalid_view_and_colors_without_dropping_missing_targets()
    {
        var state = JsonSerializer.SerializeToUtf8Bytes(new { Mode = 999, Items = new[] {
            new { Name = "Missing", Target = @"Z:\missing.exe", Background = "not-a-color" } } });
        var restored = ShortcutsModel.Restore(state);
        Assert.Equal(ShortcutViewMode.Icons, restored.Mode);
        Assert.Null(restored.Items[0].Background);
        Assert.Equal(@"Z:\missing.exe", restored.Items[0].Target);
    }

    [Fact]
    public void Restore_repairs_duplicate_ids_and_ignores_null_entries()
    {
        var item = new ShortcutEntry { Name = "A", Target = @"C:\A.exe" };
        var state = JsonSerializer.SerializeToUtf8Bytes(new { Items = new ShortcutEntry?[] { item, null, item } });
        var restored = ShortcutsModel.Restore(state);
        Assert.Equal(2, restored.Items.Count);
        Assert.NotEqual(restored.Items[0].Id, restored.Items[1].Id);
    }

    [Fact]
    public async Task Panel_restores_without_dirtying_and_tracks_changes_and_removal()
    {
        await using var scheduler = new PanelStateScheduler(NullLogger<PanelStateScheduler>.Instance);
        var notifications = new Notifications();
        await using var panel = CreatePanel(scheduler, notifications);
        var model = new ShortcutsModel { Mode = ShortcutViewMode.Buttons, Wallpaper = @"C:\wall.png" };
        model.Save(new() { Name = "App", Target = @"Z:\missing-app.exe" });
        await panel.RestoreStateAsync(model.Serialize(), CancellationToken.None);
        Assert.Equal(0, panel.ChangeVersion);
        panel.Selected = panel.Model.Items[0];
        panel.Mode = ShortcutViewMode.List;
        Assert.Equal(1, panel.ChangeVersion);
        var state = ShortcutsModel.Restore((await panel.CaptureStateAsync(CancellationToken.None)).Span);
        Assert.Equal(ShortcutViewMode.List, state.Mode);
        Assert.Equal(model.Wallpaper, state.Wallpaper);
        panel.RemoveCommand.Execute(null);
        Assert.Empty(panel.Model.Items);
        Assert.False(panel.OpenCommand.CanExecute(null));
        Assert.Single(notifications.Messages);
    }

    [Fact]
    public async Task Missing_target_reports_error_and_allows_retry_without_starting_a_process()
    {
        await using var scheduler = new PanelStateScheduler(NullLogger<PanelStateScheduler>.Instance);
        var notifications = new Notifications();
        await using var panel = CreatePanel(scheduler, notifications);
        panel.Selected = new() { Name = "Missing", Target = @"Z:\missing-uberdashboard-shortcut-79239.exe" };
        await panel.OpenCommand.ExecuteAsync(null);
        Assert.Equal(PanelNotificationSeverity.Error, Assert.Single(notifications.Messages));
        Assert.True(panel.OpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task Malformed_state_reports_error_without_replacing_existing_shortcuts()
    {
        await using var scheduler = new PanelStateScheduler(NullLogger<PanelStateScheduler>.Instance);
        var notifications = new Notifications();
        await using var panel = CreatePanel(scheduler, notifications);
        panel.Model.Save(new() { Name = "Keep", Target = @"C:\keep.exe" });
        await panel.RestoreStateAsync("bad json"u8.ToArray(), CancellationToken.None);
        Assert.Equal("Keep", Assert.Single(panel.Model.Items).Name);
        Assert.Equal(PanelNotificationSeverity.Error, Assert.Single(notifications.Messages));
    }

    private static ShortcutsPanel CreatePanel(PanelStateScheduler scheduler, Notifications notifications) => new(new PanelContext
    {
        InstanceId = PanelInstanceId.New(), PluginId = "test", DashboardId = "test", CloseToken = CancellationToken.None,
        Logger = new PanelLogger(NullLoggerFactory.Instance, "test", Guid.NewGuid(), PanelInstanceId.New()),
        Notifier = notifications, Storage = new PanelStorage(Path.GetTempPath()),
    }, scheduler);

    private sealed class Notifications : IPanelNotifier
    {
        public List<PanelNotificationSeverity> Messages { get; } = [];
        public void Publish(string message, PanelNotificationSeverity severity = PanelNotificationSeverity.Information) => Messages.Add(severity);
    }
}
