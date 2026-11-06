using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static JsonObject? EditContainer(Window owner, SyncProviderKind provider, JsonObject? original, Action<Window>? onLoaded = null)
    {
        var window = WindowFrame.Create(owner, UiText.Get(provider == SyncProviderKind.MicrosoftTodo ? "SyncManageLists" : "SyncManageCalendars"), 560, 510);
        window.MinWidth = 400; window.MinHeight = 300;
        var initial = SyncContainerDraft.Read(provider, original);
        var form = new StackPanel { Margin = new Thickness(16) };
        var name = Field(form, "SyncContainerName", initial.Name);
        System.Windows.Controls.TextBox? description = null, location = null, zone = null;
        if (provider == SyncProviderKind.GoogleCalendar)
        {
            form.Children.Add(new TextBlock { Text = UiText.Get("SyncCalendarPropertiesNotice"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
            description = Field(form, "SyncDescription", initial.Description, true);
            location = Field(form, "SyncLocation", initial.Location);
            zone = Field(form, "SyncTimeZone", initial.TimeZone); zone.ToolTip = UiText.Get("SyncCalendarTimeZoneHelp");
        }
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        form.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => validation.Text = ""));
        var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) }; footer.Children.Add(validation);
        var actions = new WrapPanel(); footer.Children.Add(actions);
        var save = ActionButton("SyncSaveLocal", actions); var cancel = ActionButton("SyncCancel", actions); cancel.IsCancel = true;
        JsonObject? result = null;
        save.Click += (_, _) =>
        {
            var draft = initial with { Name = name.Text, Description = description?.Text ?? initial.Description,
                Location = location?.Text ?? initial.Location, TimeZone = zone?.Text ?? initial.TimeZone };
            if (!draft.TryApply(provider, original, out var fields, out var error))
            { validation.Text = UiText.Get(error == SyncContainerValidation.NameRequired ? "SyncTitleRequired" : "SyncInvalidTimeZone"); return; }
            result = fields; window.DialogResult = true;
        };
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = layout;
        if (onLoaded is not null) window.Loaded += (_, _) => onLoaded(window);
        return window.ShowDialog() == true ? result : null;
    }
}
