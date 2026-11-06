using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static JsonObject? EditGoogleAccess(Window owner, bool sharing, JsonObject? original, Action<Window>? onLoaded = null)
    {
        var window = WindowFrame.Create(owner, UiText.Get(sharing ? "SyncGoogleSharing" : "SyncGoogleSubscriptions"), 580, 540);
        window.MinWidth = 400; window.MinHeight = 320;
        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = UiText.Get(sharing ? "SyncGoogleSharingHelp" : "SyncGoogleSubscriptionsHelp"), TextWrapping = TextWrapping.Wrap });
        var acl = GoogleSharingDraft.Read(original); var subscription = GoogleSubscriptionDraft.Read(original);
        ComboBox Choice(string key, string selected, IEnumerable<string> values, string prefix)
        {
            Label(form, key); var options = values.Select(v => new AccessOption(v, UiText.Get(prefix + v))).ToList();
            if (options.All(o => o.Value != selected)) options.Add(new(selected, UiText.Format("SyncGoogleServiceValue", selected)));
            var control = new ComboBox { ItemsSource = options, SelectedItem = options.Single(o => o.Value == selected), MinHeight = 28 };
            control.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBox"); AutomationProperties.SetName(control, UiText.Get(key)); form.Children.Add(control); return control;
        }
        ComboBox? scope = null, role = null; TextBox? audience = null, id = null, alias = null, background = null, foreground = null;
        CheckBox? hidden = null, selected = null;
        if (sharing)
        {
            scope = Choice("SyncGoogleScope", acl.ScopeType, ["user", "group", "domain", "default"], "SyncGoogleScope_");
            scope.IsEnabled = original is null;
            audience = Field(form, "SyncGoogleAudience", acl.ScopeValue); audience.IsReadOnly = original is not null;
            role = Choice("SyncGoogleAccessRole", acl.Role, GoogleSharingDraft.Roles, "SyncGoogleRole_");
            var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; form.Children.Add(warning);
            void UpdateAudience()
            {
                var publicly = ((AccessOption)scope.SelectedItem).Value == "default";
                audience.IsEnabled = !publicly; if (publicly && original is null) audience.Text = "";
                warning.Text = UiText.Get(publicly ? "SyncGooglePublicWarning" : "SyncGoogleSharingApprovalNotice");
            }
            scope.SelectionChanged += (_, _) => UpdateAudience(); UpdateAudience();
        }
        else
        {
            id = Field(form, "SyncGoogleCalendarId", subscription.CalendarId); id.IsReadOnly = original is not null;
            id.ToolTip = UiText.Get("SyncGoogleCalendarIdHelp");
            alias = Field(form, "SyncGoogleCalendarAlias", subscription.Alias);
            background = Field(form, "SyncGoogleCalendarBackground", subscription.BackgroundColor);
            foreground = Field(form, "SyncGoogleCalendarForeground", subscription.ForegroundColor);
            CheckBox Toggle(string key, bool value)
            {
                var toggle = new CheckBox { Content = UiText.Get(key), IsChecked = value, Margin = new Thickness(0, 10, 0, 0) };
                toggle.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox"); AutomationProperties.SetName(toggle, UiText.Get(key)); form.Children.Add(toggle); return toggle;
            }
            hidden = Toggle("SyncGoogleCalendarHidden", subscription.Hidden); selected = Toggle("SyncGoogleCalendarSelected", subscription.Selected);
        }
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        form.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => validation.Text = ""));
        form.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => validation.Text = ""));
        var actions = new WrapPanel(); var save = ActionButton("SyncSaveLocal", actions); var cancel = ActionButton("SyncCancel", actions); cancel.IsCancel = true;
        JsonObject? result = null;
        save.Click += (_, _) =>
        {
            JsonObject fields; bool valid;
            if (sharing) valid = new GoogleSharingDraft(((AccessOption)scope!.SelectedItem).Value, audience!.Text, ((AccessOption)role!.SelectedItem).Value).TryApply(original, out fields);
            else valid = (subscription with { CalendarId = id!.Text.Trim(), Alias = alias!.Text, BackgroundColor = background!.Text.Trim(), ForegroundColor = foreground!.Text.Trim(),
                Hidden = hidden!.IsChecked == true, Selected = selected!.IsChecked == true }).TryApply(original, out fields);
            if (!valid) { validation.Text = UiText.Get(sharing ? "SyncGoogleInvalidSharing" : "SyncGoogleInvalidSubscription"); return; }
            result = fields; window.DialogResult = true;
        };
        var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) }; footer.Children.Add(validation); footer.Children.Add(actions);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = layout;
        if (onLoaded is not null) window.Loaded += (_, _) => onLoaded(window);
        return window.ShowDialog() == true ? result : null;
    }

    private sealed record AccessOption(string Value, string Label) { public override string ToString() => Label; }
    private static string SharingAudience(JsonObject? value)
    {
        var type = value?["scope"]?["type"]?.GetValue<string>() ?? "user";
        return (type is "user" or "group" or "domain" or "default" ? UiText.Get("SyncGoogleScope_" + type) : UiText.Format("SyncGoogleServiceValue", type)) +
            (value?["scope"]?["value"]?.GetValue<string>() is { Length: > 0 } target ? " · " + target : "");
    }
    private static string SharingRole(JsonObject? value) => value?["role"]?.GetValue<string>() is not { } role || role == "none" ? UiText.Get("SyncGoogleNoAccess") :
        GoogleSharingDraft.Roles.Contains(role, StringComparer.Ordinal) ? UiText.Get("SyncGoogleRole_" + role) : UiText.Format("SyncGoogleServiceValue", role);
}
