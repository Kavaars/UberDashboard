using UberDashboard.Core.Localization;
using System.Text.Json;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;

namespace UberDashboard.App.Features.Notes;

/// <summary>Wynik przygotowania migracji; nie wykonuje żadnego zapisu.</summary>
public sealed record NotesTabMigrationPlan(PanelTabGroup Group, string OriginalJson, IReadOnlyList<NotesTabMigrationDocument> Documents);

/// <summary>Stan pojedynczej zakładki przed zapisaniem przez host.</summary>
public sealed record NotesTabMigrationDocument(PanelInstanceId InstanceId, string StateJson);

/// <summary>Przygotowuje rozdzielenie starszego stanu notatnika bez modyfikowania oryginału.</summary>
public static class NotesTabMigration
{
    public static NotesTabMigrationPlan Prepare(PanelInstanceId existingId, string stateJson, Func<PanelInstanceId>? createId = null)
    {
        ArgumentNullException.ThrowIfNull(stateJson);
        createId ??= PanelInstanceId.New;
        using var json = JsonDocument.Parse(stateJson);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException(UiText.Get("TheNotepadStateMustBeAnObject"));
        var entries = new List<JsonElement>();
        if (root.TryGetProperty("Tabs", out var tabs) && tabs.ValueKind != JsonValueKind.Null)
        {
            if (tabs.ValueKind != JsonValueKind.Array) throw new JsonException(UiText.Get("NotepadTabsMustBeAnArray"));
            foreach (var item in tabs.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new JsonException(UiText.Get("TheTabStateMustBeAnObject"));
                entries.Add(item);
            }
        }
        if (entries.Count == 0) entries.Add(root);
        // Sprawdź znane pola każdego dokumentu przed rozpoczęciem transakcji.
        foreach (var entry in entries)
            _ = JsonSerializer.Deserialize<NotesDocument>(entry.GetRawText()) ?? throw new JsonException(UiText.Get("InvalidNote"));
        var selected = 0;
        if (root.TryGetProperty("SelectedTabIndex", out var selectedValue) && selectedValue.ValueKind != JsonValueKind.Null)
        {
            if (selectedValue.ValueKind != JsonValueKind.Number || !selectedValue.TryGetInt32(out selected)) throw new JsonException(UiText.Get("InvalidActiveTabNumber"));
            selected = Math.Clamp(selected, 0, entries.Count - 1);
        }
        static string Name(JsonElement element)
        {
            if (!element.TryGetProperty("Title", out var title) || title.ValueKind == JsonValueKind.Null) return UiText.Get("Note2");
            if (title.ValueKind != JsonValueKind.String) throw new JsonException(UiText.Get("TheNoteNameMustBeText"));
            var value = title.GetString();
            return string.IsNullOrWhiteSpace(value) ? UiText.Get("Note2") : value.Trim();
        }
        var group = PanelTabGroup.Create(existingId, Name(entries[0]));
        var documents = new List<NotesTabMigrationDocument> { new(existingId, entries[0].GetRawText()) };
        for (var index = 1; index < entries.Count; index++)
        {
            var id = createId();
            group = group.Add(id, Name(entries[index]));
            documents.Add(new NotesTabMigrationDocument(id, entries[index].GetRawText()));
        }
        group = group.Select(documents[selected].InstanceId);
        return new NotesTabMigrationPlan(group, stateJson, documents.AsReadOnly());
    }
}
