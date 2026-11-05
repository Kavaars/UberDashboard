using UberDashboard.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using UberDashboard.App.Panels;



namespace UberDashboard.App.Features.Affirmations;



public sealed record AffirmationDay(DateOnly Date, int Affirmations, int Sets);



public sealed record AffirmationDocument

{

    public string Texts { get; init; } = "";

    public string FontFamily { get; init; } = "Segoe UI";

    public double FontSize { get; init; } = 16;

    public double LineHeight { get; init; } = 24;

    public string NormalColor { get; init; } = "#FFFFFFFF";

    public string MutedColor { get; init; } = "#88808080";

    public string? RemainingColor { get; init; }

    public double RemainingOpacity { get; init; } = 1;

    public List<AffirmationDay> History { get; init; } = [];

    public int ResetHours { get; init; }

    public int Index { get; init; }

    public int Position { get; init; }

    public DateTimeOffset ResetAt { get; init; }

}



public sealed class AffirmationPanelViewModel : ObservableObject, IPanelOptionsPresenter

{

    private readonly Func<DateTimeOffset> _clock;

    private string _texts = "";

    private int _resetHours, _index, _position;

    private DateTimeOffset _resetAt;

    private bool _options;

    private string _fontFamily = "Segoe UI", _normalColor = "#FFFFFFFF", _mutedColor = "#88808080";

    private double _fontSize = 16, _lineHeight = 24, _remainingOpacity = 1;
    private string? _remainingColor;
    public string? RemainingColor { get => _remainingColor; set { if (SetProperty(ref _remainingColor, value)) Changed?.Invoke(this, EventArgs.Empty); } }
    public double RemainingOpacity { get => _remainingOpacity; set { if (SetProperty(ref _remainingOpacity, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1)) Changed?.Invoke(this, EventArgs.Empty); } }

    private List<AffirmationDay> _history = [];

    public string FontFamily { get => _fontFamily; set { if (SetProperty(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value)) Changed?.Invoke(this, EventArgs.Empty); } }

    public double FontSize { get => _fontSize; set { if (SetProperty(ref _fontSize, double.IsFinite(value) ? Math.Clamp(value, 10, 72) : 16)) Changed?.Invoke(this, EventArgs.Empty); } }

    public double LineHeight { get => _lineHeight; set { if (SetProperty(ref _lineHeight, double.IsFinite(value) ? Math.Clamp(value, 12, 100) : 24)) Changed?.Invoke(this, EventArgs.Empty); } }

    public string NormalColor { get => _normalColor; set { if (SetProperty(ref _normalColor, value ?? "#FFFFFFFF")) Changed?.Invoke(this, EventArgs.Empty); } }

    public string MutedColor { get => _mutedColor; set { if (SetProperty(ref _mutedColor, value ?? "#88808080")) Changed?.Invoke(this, EventArgs.Empty); } }

    public int TotalAffirmations => _history.Sum(x => x.Affirmations);

    public int TotalSets => _history.Sum(x => x.Sets);

    public string HistoryText => _history.Count == 0 ? UiText.Get("NoHistory") : string.Join("\n", _history.OrderByDescending(x => x.Date).Select(x => UiText.Format("AffirmationsSets", x.Date, x.Affirmations, x.Sets)));

    public string StreakHistory
    {
        get
        {
            var days = _history.Where(x => x.Sets > 0).Select(x => x.Date).Distinct().Order().ToArray();
            if (days.Length == 0) return UiText.Get("NoCompletedStreaks");
            var ranges = new List<string>();
            var start = days[0]; var end = start;
            foreach (var day in days.Skip(1))
            {
                if (day.DayNumber == end.DayNumber + 1) end = day;
                else { ranges.Add(UiText.Format("Days", start, end, end.DayNumber - start.DayNumber + 1)); start = end = day; }
            }
            ranges.Add(UiText.Format("Days", start, end, end.DayNumber - start.DayNumber + 1));
            ranges.Reverse(); return string.Join("\n", ranges);
        }
    }
    public int CurrentStreak

    {

        get

        {

            var days = _history.Where(x => x.Sets > 0).Select(x => x.Date).ToHashSet();

            var day = DateOnly.FromDateTime(_clock().ToLocalTime().DateTime);

            if (!days.Contains(day)) day = day.AddDays(-1);

            var count = 0;

            while (days.Contains(day)) { count++; day = day.AddDays(-1); }

            return count;

        }

    }

    private void RecordCompletion(bool setCompleted)

    {

        var day = DateOnly.FromDateTime(_clock().ToLocalTime().DateTime);

        var i = _history.FindIndex(x => x.Date == day);

        var old = i < 0 ? new AffirmationDay(day, 0, 0) : _history[i];

        var next = old with { Affirmations = old.Affirmations + 1, Sets = old.Sets + (setCompleted ? 1 : 0) };

        if (i < 0) _history.Add(next); else _history[i] = next;

        OnPropertyChanged(nameof(TotalAffirmations)); OnPropertyChanged(nameof(TotalSets)); OnPropertyChanged(nameof(CurrentStreak)); OnPropertyChanged(nameof(HistoryText)); OnPropertyChanged(nameof(StreakHistory));

    }

    private string[] Lines => _texts.Split('\n').Select(x => x.TrimEnd('\r').Trim()).Where(x => x.Length > 0).ToArray();



    public AffirmationPanelViewModel() : this(() => DateTimeOffset.UtcNow) { }

    public AffirmationPanelViewModel(Func<DateTimeOffset> clock)

    {

        _clock = clock;

        _resetAt = _clock();

        ResetCommand = new RelayCommand(Reset);

        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);

    }

    public event EventHandler? Changed;

    public bool HasOptions => true;

    public bool IsOptionsOpen { get => _options; set => SetProperty(ref _options, value); }

    public IRelayCommand ToggleOptionsCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public string Texts { get => _texts; set { if (SetProperty(ref _texts, value ?? "")) Reset(); } }

    public int ResetHours { get => _resetHours; set { var hours = value is 4 or 8 or 24 or 48 ? value : 0; if (SetProperty(ref _resetHours, hours)) Reset(); } }

    public string Completed => string.Join("\n", Lines.Take(_index));

    public string Typed => _index < Lines.Length ? Lines[_index][.._position] : "";

    public string Remaining => _index < Lines.Length ? Lines[_index][_position..] : "";

    public bool HasCurrent => _index < Lines.Length;

    public string Status => Lines.Length == 0 ? UiText.Get("AddAffirmationsInThePanelOptions") : HasCurrent ? UiText.Format("AffirmationOf", _index + 1, Lines.Length) : UiText.Get("AllAffirmationsCompleted");

    public void Accept(string input)

    {

        CheckReset();

        foreach (var c in input)

        {

            var lines = Lines;

            if (_index >= lines.Length || c != lines[_index][_position]) break;

            _position++;

            if (_position == lines[_index].Length) { _index++; _position = 0; RecordCompletion(_index == lines.Length); }

        }

        Notify();

    }

    public void CheckReset() { if (_resetHours > 0 && _clock() >= _resetAt.AddHours(_resetHours)) Reset(); }

    public void Reset() { _index = 0; _position = 0; _resetAt = _clock(); Notify(); }

    public AffirmationDocument Snapshot() => new() { Texts = _texts, ResetHours = _resetHours, Index = _index, Position = _position, ResetAt = _resetAt, FontFamily = FontFamily, FontSize = FontSize, LineHeight = LineHeight, NormalColor = NormalColor, MutedColor = MutedColor, RemainingColor = RemainingColor, RemainingOpacity = RemainingOpacity, History = [.. _history] };

    public void Load(AffirmationDocument doc)

    {

        _texts = doc.Texts ?? "";

        _history = doc.History ?? [];

        FontFamily = doc.FontFamily; FontSize = doc.FontSize; LineHeight = doc.LineHeight; NormalColor = doc.NormalColor; MutedColor = doc.MutedColor; RemainingColor = doc.RemainingColor; RemainingOpacity = doc.RemainingOpacity;

        OnPropertyChanged(nameof(TotalAffirmations)); OnPropertyChanged(nameof(TotalSets)); OnPropertyChanged(nameof(CurrentStreak)); OnPropertyChanged(nameof(HistoryText)); OnPropertyChanged(nameof(StreakHistory));

        _resetHours = doc.ResetHours is 4 or 8 or 24 or 48 ? doc.ResetHours : 0;

        var lines = Lines;

        _index = Math.Clamp(doc.Index, 0, lines.Length);

        _position = _index < lines.Length ? Math.Clamp(doc.Position, 0, Math.Max(0, lines[_index].Length - 1)) : 0;

        _resetAt = doc.ResetAt == default ? _clock() : doc.ResetAt;

        OnPropertyChanged(nameof(Texts)); OnPropertyChanged(nameof(ResetHours));

        if (_resetHours > 0 && _clock() >= _resetAt.AddHours(_resetHours)) Reset(); else Notify();

    }

    private void Notify()

    {

        OnPropertyChanged(nameof(Completed)); OnPropertyChanged(nameof(Typed)); OnPropertyChanged(nameof(Remaining));

        OnPropertyChanged(nameof(HasCurrent)); OnPropertyChanged(nameof(Status)); Changed?.Invoke(this, EventArgs.Empty);

    }

}
