using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;

namespace UberDashboard.App.Features.Integrations;

internal sealed class GoogleEventOptionsForm
{
    private readonly GoogleEventOptions _initial;
    private readonly JsonObject? _original, _metadata;
    private readonly TextBox _emails;
    private readonly ComboBox _response, _reminderMode, _visibility, _transparency;
    private readonly List<(CheckBox Enabled, ComboBox Method, TextBox Minutes)> _reminders = [];

    internal GoogleEventOptionsForm(Panel parent, JsonObject? original, JsonObject? metadata)
    {
        _initial = GoogleEventOptions.Read(original); _original = original; _metadata = metadata;
        var people = Section(parent, "SyncGooglePeople");
        people.Children.Add(new TextBlock { Text = UiText.Get("SyncGooglePeopleHelp"), TextWrapping = TextWrapping.Wrap });
        _emails = SyncEditorWindow.Field(people, "SyncGoogleEmails", _initial.AttendeeEmails, true);
        _emails.IsReadOnly = !GoogleEventOptions.CanManageAttendees(original, metadata);
        if (original?["attendees"] is JsonArray attendees)
        {
            foreach (var attendee in attendees.OfType<JsonObject>())
            {
                var status = attendee["responseStatus"]?.GetValue<string>() ?? "needsAction";
                people.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", new[] { attendee["displayName"]?.GetValue<string>(), attendee["email"]?.GetValue<string>(),
                        ResponseLabel(status), attendee["optional"]?.GetValue<bool>() == true ? UiText.Get("SyncGoogleOptional") : null }.Where(t => !string.IsNullOrEmpty(t))),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
                });
            }
        }
        _response = Choice(people, "SyncGoogleResponse", _initial.SelfResponse,
            ["needsAction", "accepted", "declined", "tentative"], ResponseLabel);
        _response.IsEnabled = GoogleEventOptions.CanRespond(original, metadata);
        if (!GoogleEventOptions.CanManageAttendees(original, metadata))
            people.Children.Add(new TextBlock { Text = UiText.Get("SyncGooglePeopleReadOnly"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });

        var reminders = Section(parent, "SyncGoogleReminders");
        _reminderMode = Choice(reminders, "SyncGoogleReminderMode", _initial.ReminderMode, ["default", "none", "custom"], v => UiText.Get("SyncGoogleReminder_" + v));
        reminders.Children.Add(new TextBlock { Text = UiText.Get("SyncGoogleRemindersHelp"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var entries = new StackPanel(); reminders.Children.Add(entries);
        for (var i = 0; i < 5; i++)
        {
            var value = _initial.Reminders.ElementAtOrDefault(i) ?? new("popup", 10);
            var row = new StackPanel(); entries.Children.Add(row);
            var key = UiText.Format("SyncGoogleReminderNumber", i + 1);
            var enabled = new CheckBox { Content = key, IsChecked = i < _initial.Reminders.Count, Margin = new Thickness(0, 8, 0, 0) };
            enabled.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox"); AutomationProperties.SetName(enabled, key); row.Children.Add(enabled);
            var controls = new StackPanel { IsEnabled = enabled.IsChecked == true }; row.Children.Add(controls);
            var method = Choice(controls, "SyncGoogleReminderMethod", value.Method, ["popup", "email"], v => UiText.Get("SyncGoogleReminder_" + v));
            AutomationProperties.SetName(method, key + " · " + UiText.Get("SyncGoogleReminderMethod"));
            var minutes = SyncEditorWindow.Field(controls, "SyncGoogleReminderMinutes", value.Minutes.ToString(CultureInfo.InvariantCulture));
            AutomationProperties.SetName(minutes, key + " · " + UiText.Get("SyncGoogleReminderMinutes"));
            enabled.Checked += (_, _) => controls.IsEnabled = true; enabled.Unchecked += (_, _) => controls.IsEnabled = false;
            _reminders.Add((enabled, method, minutes));
        }
        void UpdateReminders() => entries.IsEnabled = Value(_reminderMode) == "custom";
        _reminderMode.SelectionChanged += (_, _) => UpdateReminders(); UpdateReminders();
        reminders.IsEnabled = GoogleEventOptions.IsStandard(original);
        var options = Section(parent, "SyncGoogleVisibilityAvailability");
        _visibility = Choice(options, "SyncGoogleVisibility", _initial.Visibility, ["default", "public", "private", "confidential"], v => UiText.Get("SyncGoogleVisibility_" + v));
        _transparency = Choice(options, "SyncGoogleAvailability", _initial.Transparency, ["opaque", "transparent"], v => UiText.Get("SyncGoogleAvailability_" + v));
        options.IsEnabled = GoogleEventOptions.CanEditMain(original, metadata);
    }

    internal bool TryApply(out JsonObject result, out GoogleEventValidation error)
    {
        result = new(); error = GoogleEventValidation.InvalidReminders;
        var reminders = new List<GoogleReminder>();
        if (Value(_reminderMode) == "custom")
        {
            foreach (var (enabled, method, minutes) in _reminders.Where(r => r.Enabled.IsChecked == true))
            {
                if (!int.TryParse(minutes.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return false;
                reminders.Add(new(Value(method), count));
            }
        }
        else reminders.AddRange(_initial.Reminders);
        // More than five future server reminders cannot be represented by this editor. Preserve an untouched document.
        if (_initial.Reminders.Count > 5 && reminders.SequenceEqual(_initial.Reminders.Take(5))) reminders = [.. _initial.Reminders];
        return (_initial with { AttendeeEmails = _emails.Text, SelfResponse = Value(_response), ReminderMode = Value(_reminderMode),
            Reminders = reminders, Visibility = Value(_visibility), Transparency = Value(_transparency) })
            .TryApply(_original, _metadata, out result, out error);
    }

    private static StackPanel Section(Panel parent, string key)
    {
        var content = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        parent.Children.Add(new Expander { Header = UiText.Get(key), Content = content, Margin = new Thickness(0, 10, 0, 0) }); return content;
    }
    private static ComboBox Choice(Panel parent, string key, string selected, string[] values, Func<string, string> label)
    {
        SyncEditorWindow.Label(parent, key);
        var options = values.Select(v => new Option(v, label(v))).ToList();
        if (!values.Contains(selected, StringComparer.Ordinal)) options.Add(new(selected, UiText.Format("SyncGoogleServiceValue", selected)));
        var control = new ComboBox { ItemsSource = options, SelectedItem = options.Single(o => o.Value == selected), MinHeight = 28 };
        control.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBox");
        AutomationProperties.SetName(control, UiText.Get(key)); parent.Children.Add(control); return control;
    }
    private static string Value(ComboBox control) => ((Option)control.SelectedItem).Value;
    private static string ResponseLabel(string status) => status is "needsAction" or "accepted" or "declined" or "tentative"
        ? UiText.Get("SyncGoogleResponse_" + status) : UiText.Format("SyncGoogleServiceValue", status);
    private sealed record Option(string Value, string Label) { public override string ToString() => Label; }
}
