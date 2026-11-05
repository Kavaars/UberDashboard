using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UberDashboard.App.Features.Shortcuts;

public enum ShortcutViewMode { Icons, List, Buttons, Tiles }

public sealed record ShortcutEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Target { get; init; } = "";
    public string Icon { get; init; } = "↗";
    public string? ImagePath { get; init; }
    public string? Background { get; init; }
    public string? Foreground { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasBackground => Background is not null;
    [System.Text.Json.Serialization.JsonIgnore] public bool HasForeground => Foreground is not null;
    [System.Text.Json.Serialization.JsonIgnore] public string? AutoIconTarget => Icon == "↗" ? Target : null;
}

public static class ShortcutTarget
{
    public static string Normalize(string? value)
    {
        var target = Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"'));
        if (target.Any(char.IsControl)) throw new ArgumentException("Invalid target.");
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host))
            return target;
        // Paths are passed as a single shell target, never interpreted as a command line.
        if (Path.IsPathFullyQualified(target) && !target.StartsWith(@"\\?\", StringComparison.Ordinal) &&
            !target.StartsWith(@"\\.\", StringComparison.Ordinal) && !target.Contains("://", StringComparison.Ordinal) &&
            target.IndexOfAny(['"', '<', '>', '|', '*', '?']) < 0)
            return target;
        throw new ArgumentException("Use an absolute path or an HTTP/HTTPS address.");
    }
}

public sealed class ShortcutsModel
{
    public ObservableCollection<ShortcutEntry> Items { get; } = [];
    public ShortcutViewMode Mode { get; set; }
    public string? Wallpaper { get; set; }

    public ShortcutEntry Save(ShortcutEntry draft, ShortcutEntry? original = null)
    {
        if (string.IsNullOrWhiteSpace(draft.Name)) throw new ArgumentException("A name is required.");
        var saved = draft with
        {
            Id = original?.Id ?? Guid.NewGuid(), Name = draft.Name.Trim(),
            Target = ShortcutTarget.Normalize(draft.Target), Background = Color(draft.Background),
            Foreground = Color(draft.Foreground), Icon = string.IsNullOrWhiteSpace(draft.Icon) ? "↗" : draft.Icon.Trim(),
        };
        var index = original is null ? -1 : Items.IndexOf(original);
        if (index < 0) Items.Add(saved);
        else Items[index] = saved;
        return saved;
    }

    public void Move(ShortcutEntry item, int offset)
    {
        var index = Items.IndexOf(item);
        if (index >= 0 && index + offset >= 0 && index + offset < Items.Count) Items.Move(index, index + offset);
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new Document(Mode, Wallpaper, Items.ToArray()));

    public static ShortcutsModel Restore(ReadOnlySpan<byte> state)
    {
        var document = JsonSerializer.Deserialize<Document>(state) ?? new(ShortcutViewMode.Icons, null, []);
        var model = new ShortcutsModel
        {
            Mode = Enum.IsDefined(document.Mode) ? document.Mode : ShortcutViewMode.Icons,
            Wallpaper = document.Wallpaper,
        };
        var ids = new HashSet<Guid>();
        foreach (var item in document.Items ?? [])
        {
            if (item is null) continue;
            var id = item.Id;
            if (id == Guid.Empty || !ids.Add(id)) { id = Guid.NewGuid(); ids.Add(id); }
            model.Items.Add(item with
            {
                Id = id, Name = item.Name ?? "", Target = item.Target ?? "", Icon = item.Icon ?? "↗",
                Background = Color(item.Background), Foreground = Color(item.Foreground),
            });
        }
        return model;
    }

    private static string? Color(string? value) => value is not null && Regex.IsMatch(value, "^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$") ? value : null;
    private sealed record Document(ShortcutViewMode Mode, string? Wallpaper, ShortcutEntry?[]? Items);
}
