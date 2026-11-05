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

internal sealed class TaskRecurrenceForm
{
    private readonly JsonObject? _original;
    private readonly TaskRecurrenceInput _displayInitial;
    private readonly CheckBox _enabled;
    private readonly ComboBox _pattern, _range, _firstDay, _index;
    private readonly TextBox _interval, _day, _month, _count, _zone;
    private readonly DatePicker _start, _end;
    private readonly Dictionary<string, CheckBox> _days = new(StringComparer.Ordinal);

    internal TaskRecurrenceForm(Panel parent, JsonObject? original)
    {
        _original = original;
        var initial = TaskRecurrenceInput.Read(original);
        var content = new StackPanel();
        var section = new Expander { Header = UiText.Get("SyncTaskRecurrence"), Content = content, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(section, UiText.Get("SyncTaskRecurrence")); parent.Children.Add(section);
        _enabled = new CheckBox { Content = new TextBlock { Text = UiText.Get("SyncRecurrenceEnabled"), TextWrapping = TextWrapping.Wrap }, IsChecked = initial.Enabled };
        _enabled.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
        AutomationProperties.SetName(_enabled, UiText.Get("SyncRecurrenceEnabled")); content.Children.Add(_enabled);
        var controls = new StackPanel { IsEnabled = initial.Enabled }; content.Children.Add(controls);
        _enabled.Checked += (_, _) => controls.IsEnabled = true; _enabled.Unchecked += (_, _) => controls.IsEnabled = false;
        _pattern = Choice(controls, "SyncRecurrencePattern", initial.Pattern, TaskRecurrenceInput.Patterns, "SyncRecurrencePattern_");
        _interval = SyncEditorWindow.Field(controls, "SyncRecurrenceInterval", initial.Interval);
        var weekdayGroup = new StackPanel(); controls.Children.Add(weekdayGroup);
        SyncEditorWindow.Label(weekdayGroup, "SyncRecurrenceWeekdays");
        var days = new WrapPanel(); weekdayGroup.Children.Add(days);
        foreach (var day in TaskRecurrenceInput.Weekdays)
        {
            var checkbox = new CheckBox { Content = UiText.Get("SyncWeekday_" + day), IsChecked = initial.Days.Split(',').Contains(day), Margin = new Thickness(0, 0, 8, 4) };
            checkbox.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
            AutomationProperties.SetName(checkbox, UiText.Get("SyncWeekday_" + day)); days.Children.Add(checkbox); _days[day] = checkbox;
        }
        var firstDayGroup = new StackPanel(); controls.Children.Add(firstDayGroup);
        _firstDay = Choice(firstDayGroup, "SyncRecurrenceFirstDay", initial.FirstDay, TaskRecurrenceInput.Weekdays, "SyncWeekday_");
        var indexGroup = new StackPanel(); controls.Children.Add(indexGroup);
        _index = Choice(indexGroup, "SyncRecurrenceIndex", initial.Index, TaskRecurrenceInput.Indices, "SyncRecurrenceIndex_");
        var dayGroup = new StackPanel(); controls.Children.Add(dayGroup);
        _day = SyncEditorWindow.Field(dayGroup, "SyncRecurrenceDay", initial.DayOfMonth);
        var monthGroup = new StackPanel(); controls.Children.Add(monthGroup);
        _month = SyncEditorWindow.Field(monthGroup, "SyncRecurrenceMonth", initial.Month);
        _range = Choice(controls, "SyncRecurrenceRange", initial.Range, TaskRecurrenceInput.Ranges, "SyncRecurrenceRange_");
        _start = Date(controls, "SyncRecurrenceStart", initial.StartDate);
        var endGroup = new StackPanel(); controls.Children.Add(endGroup);
        _end = Date(endGroup, "SyncRecurrenceEnd", initial.EndDate);
        var countGroup = new StackPanel(); controls.Children.Add(countGroup);
        _count = SyncEditorWindow.Field(countGroup, "SyncRecurrenceCount", initial.Occurrences);
        _zone = SyncEditorWindow.Field(controls, "SyncRecurrenceZone", initial.TimeZone);
        _zone.ToolTip = UiText.Get("SyncRecurrenceZoneHelp");
        void Update()
        {
            var p = ((Option)_pattern.SelectedItem).Value; var r = ((Option)_range.SelectedItem).Value;
            weekdayGroup.Visibility = p is "weekly" or "relativeMonthly" or "relativeYearly" ? Visibility.Visible : Visibility.Collapsed;
            firstDayGroup.Visibility = p == "weekly" ? Visibility.Visible : Visibility.Collapsed;
            indexGroup.Visibility = p is "relativeMonthly" or "relativeYearly" ? Visibility.Visible : Visibility.Collapsed;
            dayGroup.Visibility = p is "absoluteMonthly" or "absoluteYearly" ? Visibility.Visible : Visibility.Collapsed;
            monthGroup.Visibility = p is "absoluteYearly" or "relativeYearly" ? Visibility.Visible : Visibility.Collapsed;
            endGroup.Visibility = r == "endDate" ? Visibility.Visible : Visibility.Collapsed;
            countGroup.Visibility = r == "numbered" ? Visibility.Visible : Visibility.Collapsed;
        }
        _pattern.SelectionChanged += (_, _) => Update(); _range.SelectionChanged += (_, _) => Update(); Update();
        _displayInitial = Read();
    }

    internal bool TryApply(out JsonObject? rule, out TaskRecurrenceValidation error)
    {
        var current = Read();
        if (current == _displayInitial) { rule = (JsonObject?)_original?.DeepClone(); error = TaskRecurrenceValidation.None; return true; }
        return current.TryApply(_original, out rule, out error);
    }

    private TaskRecurrenceInput Read() => new(_enabled.IsChecked == true, ((Option)_pattern.SelectedItem).Value,
        _interval.Text, string.Join(",", _days.Where(p => p.Value.IsChecked == true).Select(p => p.Key)),
        ((Option)_firstDay.SelectedItem).Value, ((Option)_index.SelectedItem).Value, _day.Text, _month.Text,
        ((Option)_range.SelectedItem).Value, SyncEditorWindow.DateValue(_start)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        SyncEditorWindow.DateValue(_end)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", _count.Text, _zone.Text);

    private static DatePicker Date(Panel parent, string key, string value) => SyncEditorWindow.DateField(parent, key,
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : DateTime.Today);

    private static ComboBox Choice(Panel parent, string key, string value, IReadOnlyList<string> values, string prefix)
    {
        SyncEditorWindow.Label(parent, key);
        var options = values.Append(value).Distinct(StringComparer.Ordinal).Select(v => new Option(v,
            values.Contains(v) ? UiText.Get(prefix + v) : UiText.Get("SyncServiceValuePreserved"))).ToArray();
        var box = new ComboBox { ItemsSource = options, DisplayMemberPath = nameof(Option.Label), SelectedItem = options.First(o => o.Value == value) };
        box.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBox");
        AutomationProperties.SetName(box, UiText.Get(key)); parent.Children.Add(box); return box;
    }

    private sealed record Option(string Value, string Label) { public override string ToString() => Label; }
}
