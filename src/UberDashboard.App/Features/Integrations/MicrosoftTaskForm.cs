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

/// <summary>Task-only controls; the host keeps ownership of persistence and window lifecycle.</summary>
internal sealed class MicrosoftTaskForm
{
    private readonly MicrosoftTaskDraft _initial;
    private readonly ComboBox _status, _importance;
    private readonly TextBox _categories;
    private readonly CheckBox _html, _reminderOn;
    private readonly ZonedDateField _start, _due, _completed, _reminder;
    private readonly TaskRecurrenceForm _recurrence;

    internal MicrosoftTaskForm(Panel parent, JsonObject? original)
    {
        _initial = MicrosoftTaskDraft.Read(original);
        _html = Toggle(parent, "SyncDescriptionHtml", _initial.DescriptionIsHtml);
        _html.ToolTip = UiText.Get("SyncDescriptionHtmlHelp");
        _status = Choice(parent, "SyncTaskStatus", _initial.Status, MicrosoftTaskDraft.Statuses, "SyncTaskStatus_");
        _importance = Choice(parent, "SyncTaskImportance", _initial.Importance, MicrosoftTaskDraft.ImportanceValues, "SyncImportance_");
        _categories = SyncEditorWindow.Field(parent, "SyncTaskCategories", _initial.Categories, true);
        _categories.ToolTip = UiText.Get("SyncTaskCategoriesHelp");
        var dates = new StackPanel();
        var section = new Expander { Header = UiText.Get("SyncTaskDates"), Content = dates, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(section, UiText.Get("SyncTaskDates")); parent.Children.Add(section);
        _start = new(dates, "SyncTaskStartDate", _initial.Start);
        _due = new(dates, "SyncTaskDueDate", _initial.Due);
        _completed = new(dates, "SyncTaskCompletedDate", _initial.Completed);
        _reminderOn = Toggle(dates, "SyncTaskReminderOn", _initial.IsReminderOn);
        _reminder = new(dates, "SyncTaskReminderDate", _initial.Reminder);
        _recurrence = new(parent, original?["recurrence"] as JsonObject);
    }

    internal bool TryApply(JsonObject? original, string title, string description, out JsonObject result, out MicrosoftTaskValidation error)
    {
        if (!(_initial with { Title = title, Description = description, DescriptionIsHtml = _html.IsChecked == true,
            Status = ((Option)_status.SelectedItem).Value, Importance = ((Option)_importance.SelectedItem).Value,
            Categories = _categories.Text, Start = _start.Read(), Due = _due.Read(), Completed = _completed.Read(),
            IsReminderOn = _reminderOn.IsChecked == true, Reminder = _reminder.Read() }).TryApply(original, out result, out error)) return false;
        if (!_recurrence.TryApply(out var rule, out var recurrenceError))
        { error = recurrenceError == TaskRecurrenceValidation.InvalidTimeZone ? MicrosoftTaskValidation.InvalidTimeZone : MicrosoftTaskValidation.InvalidRecurrence; return false; }
        if (!JsonNode.DeepEquals(original?["recurrence"], rule)) result["recurrence"] = rule;
        return true;
    }

    private static ComboBox Choice(Panel parent, string label, string value, IReadOnlyList<string> values, string prefix)
    {
        SyncEditorWindow.Label(parent, label);
        var options = values.Append(value).Distinct(StringComparer.Ordinal).Select(v => new Option(v,
            values.Contains(v) ? UiText.Get(prefix + v) : UiText.Get("SyncServiceValuePreserved"))).ToArray();
        var box = new ComboBox { ItemsSource = options, DisplayMemberPath = nameof(Option.Label), SelectedItem = options.First(o => o.Value == value) };
        box.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBox");
        AutomationProperties.SetName(box, UiText.Get(label)); parent.Children.Add(box); return box;
    }

    private static CheckBox Toggle(Panel parent, string key, bool value)
    {
        var box = new CheckBox { Content = new TextBlock { Text = UiText.Get(key), TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 8, 0, 3) };
        box.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
        AutomationProperties.SetName(box, UiText.Get(key)); parent.Children.Add(box); return box;
    }

    private sealed class ZonedDateField
    {
        private readonly TaskDateInput _initial;
        private readonly CheckBox _enabled;
        private readonly DatePicker _date;
        private readonly TextBox _time, _zone;
        private readonly DateTime? _displayDate;
        private readonly string _displayTime;

        internal ZonedDateField(Panel parent, string key, TaskDateInput value)
        {
            _initial = value;
            _enabled = Toggle(parent, key, value.HasValue);
            _enabled.ToolTip = UiText.Get("SyncOptionalDateHelp");
            var controls = new StackPanel { IsEnabled = value.HasValue, Margin = new Thickness(12, 0, 0, 6) };
            parent.Children.Add(controls);
            _displayDate = DateTime.TryParseExact(value.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed : value.HasValue ? null : DateTime.Today;
            _date = SyncEditorWindow.DateField(controls, "SyncDate", _displayDate ?? DateTime.Today);
            _date.SelectedDate = _displayDate;
            AutomationProperties.SetName(_date, UiText.Get(key) + " · " + UiText.Get("SyncDate"));
            _displayTime = value.Time.Length > 0 ? value.Time : "09:00";
            _time = SyncEditorWindow.Field(controls, "SyncTimeInZone", _displayTime);
            AutomationProperties.SetName(_time, UiText.Get(key) + " · " + UiText.Get("SyncTimeInZone"));
            _zone = SyncEditorWindow.Field(controls, "SyncTimeZone", value.TimeZone);
            _zone.ToolTip = UiText.Get("SyncMicrosoftTimeZoneHelp");
            AutomationProperties.SetName(_zone, UiText.Get(key) + " · " + UiText.Get("SyncTimeZone"));
            _enabled.Checked += (_, _) => controls.IsEnabled = true;
            _enabled.Unchecked += (_, _) => controls.IsEnabled = false;
        }

        internal TaskDateInput Read()
        {
            if (!SyncEditorWindow.HasDateError(_date) && (_enabled.IsChecked == true) == _initial.HasValue && _date.SelectedDate == _displayDate &&
                _time.Text == _displayTime && _zone.Text == _initial.TimeZone) return _initial;
            return new(_enabled.IsChecked == true, SyncEditorWindow.DateValue(_date)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                _time.Text, _zone.Text);
        }
    }

    private sealed record Option(string Value, string Label) { public override string ToString() => Label; }
}
