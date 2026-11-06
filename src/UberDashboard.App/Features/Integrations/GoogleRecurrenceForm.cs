using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NodaTime;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace UberDashboard.App.Features.Integrations;

internal sealed class GoogleRecurrenceForm
{
    private readonly TextBox _startZone, _endZone, _startOffset, _endOffset, _rules;
    private readonly string _initialStartZone, _initialEndZone, _initialRules;
    public bool ZonesChanged => _startZone.Text.Trim() != _initialStartZone || _endZone.Text.Trim() != _initialEndZone;
    public bool RulesChanged => _rules.Text.Replace("\r", "", StringComparison.Ordinal) != _initialRules;

    internal GoogleRecurrenceForm(StackPanel form, JsonObject? original, JsonObject? metadata)
    {
        var defaultZone = DefaultZone();
        _initialStartZone = original?["start"]?["timeZone"]?.GetValue<string>() ?? defaultZone;
        _initialEndZone = original?["end"]?["timeZone"]?.GetValue<string>() ?? _initialStartZone;
        _startZone = SyncEditorWindow.Field(form, "SyncSeriesStartZone", _initialStartZone);
        _endZone = SyncEditorWindow.Field(form, "SyncSeriesEndZone", _initialEndZone);
        var zoneHelp = new TextBlock { Text = UiText.Get("SyncSeriesZoneHelp"), TextWrapping = TextWrapping.Wrap }; form.Children.Add(zoneHelp);
        _startOffset = SyncEditorWindow.Field(form, "SyncSeriesStartOffset", AmbiguousOffset(original?["start"] as JsonObject, _initialStartZone));
        _endOffset = SyncEditorWindow.Field(form, "SyncSeriesEndOffset", AmbiguousOffset(original?["end"] as JsonObject, _initialEndZone));
        var body = new StackPanel(); var expander = new Expander { Header = UiText.Get("SyncSeriesRules"), Content = body, Margin = new Thickness(0, 8, 0, 8), IsExpanded = original?["recurrence"] is not null };
        AutomationProperties.SetName(expander, UiText.Get("SyncSeriesRules")); form.Children.Add(expander);
        body.Children.Add(new TextBlock { Text = UiText.Get("SyncSeriesRulesHelp"), TextWrapping = TextWrapping.Wrap });
        var choices = new[] { "DAILY", "WEEKLY", "MONTHLY", "YEARLY" };
        SyncEditorWindow.Label(body, "SyncSeriesPreset"); var frequency = new ComboBox { ItemsSource = choices.Select(v => new Choice(v, UiText.Get("SyncSeries" + v))).ToArray(), DisplayMemberPath = nameof(Choice.Label), SelectedValuePath = nameof(Choice.Value), SelectedIndex = 1 };
        AutomationProperties.SetName(frequency, UiText.Get("SyncSeriesPreset")); body.Children.Add(frequency);
        var interval = SyncEditorWindow.Field(body, "SyncRecurrenceInterval", "1");
        var days = SyncEditorWindow.Field(body, "SyncSeriesByDay", "MO,TU,WE,TH,FR");
        var count = SyncEditorWindow.Field(body, "SyncSeriesCount", "");
        var until = SyncEditorWindow.Field(body, "SyncSeriesUntil", "");
        var usePreset = new System.Windows.Controls.Button { Content = UiText.Get("SyncSeriesUsePreset"), ToolTip = UiText.Get("SyncSeriesUsePresetHelp"), Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(usePreset, UiText.Get("SyncSeriesUsePreset")); body.Children.Add(usePreset);
        _initialRules = string.Join('\n', original is null ? [] : GoogleRecurrence.Lines(original));
        _rules = SyncEditorWindow.Field(body, "SyncSeriesAdvancedRules", _initialRules, true);
        _rules.MinHeight = 85; _rules.AcceptsReturn = true;
        usePreset.Click += (_, _) =>
        {
            var rule = "RRULE:FREQ=" + (frequency.SelectedItem as Choice)?.Value + ";INTERVAL=" + interval.Text.Trim();
            if ((frequency.SelectedItem as Choice)?.Value == "WEEKLY" && !string.IsNullOrWhiteSpace(days.Text)) rule += ";BYDAY=" + days.Text.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(count.Text)) rule += ";COUNT=" + count.Text.Trim();
            if (!string.IsNullOrWhiteSpace(until.Text)) rule += ";UNTIL=" + until.Text.Trim().ToUpperInvariant();
            _rules.Text = rule;
        };
        if (metadata?["recurringEventId"] is not null)
        {
            expander.IsEnabled = false;
            body.Children.Add(new TextBlock { Text = UiText.Get("SyncSeriesInstanceRulesNotice"), TextWrapping = TextWrapping.Wrap });
        }
    }

    internal static string DefaultZone()
    {
        var zone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var local) ? local : TimeZoneInfo.Local.Id;
        return DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) is null ? "UTC" : zone;
    }

    internal void ApplyRules(JsonObject fields)
    {
        if (!RulesChanged) return;
        var lines = _rules.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        fields["recurrence"] = lines.Length == 0 ? null : new JsonArray(lines.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
    }

    internal bool TryTimes(DateTime first, DateTime last, out JsonObject start, out JsonObject end)
    {
        start = new(); end = new();
        return TryTime(first, _startZone.Text.Trim(), _startOffset.Text.Trim(), out start) && TryTime(last, _endZone.Text.Trim(), _endOffset.Text.Trim(), out end) &&
            GoogleRecurrence.Time(end) > GoogleRecurrence.Time(start);
    }

    private static bool TryTime(DateTime value, string zoneId, string offsetChoice, out JsonObject result)
    {
        result = new(); var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId); if (zone is null) return false;
        var mapping = zone.MapLocal(LocalDateTime.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Unspecified)));
        if (mapping.Count == 0) return false;
        var chosen = mapping.First();
        if (mapping.Count == 2)
        {
            string Format(Offset offset) => (offset.Seconds < 0 ? "-" : "+") + TimeSpan.FromSeconds(Math.Abs(offset.Seconds)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            if (offsetChoice == Format(mapping.Last().Offset)) chosen = mapping.Last();
            else if (offsetChoice != Format(chosen.Offset)) return false;
        }
        result = new JsonObject { ["dateTime"] = chosen.ToDateTimeOffset().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), ["timeZone"] = zoneId }; return true;
    }

    private static string AmbiguousOffset(JsonObject? time, string zone)
    {
        if (time?["dateTime"]?.GetValue<string>() is not { } value || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return "";
        var wall = GoogleRecurrence.Time(time, zone).Value; var mapping = DateTimeZoneProviders.Tzdb[zone].MapLocal(LocalDateTime.FromDateTime(wall));
        return mapping.Count == 2 ? (parsed.Offset < TimeSpan.Zero ? "-" : "+") + parsed.Offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture) : "";
    }
    private sealed record Choice(string Value, string Label);
}
