using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public sealed record SyncMergeResult(JsonObject? Value, IReadOnlyList<string> Conflicts);

/// <summary>Three-way merge of writable fields. Null documents represent deletion.</summary>
public static class SyncMerge
{
    public static SyncMergeResult Merge(JsonObject? baseline, JsonObject? local, JsonObject? remote,
        IReadOnlyList<string[]>? atomicGroups = null)
    {
        if (JsonNode.DeepEquals(local, baseline)) return new(Clone(remote), []);
        if (JsonNode.DeepEquals(remote, baseline) || JsonNode.DeepEquals(local, remote)) return new(Clone(local), []);
        if (local is null || remote is null) return new(Clone(local), ["$deleted"]);

        var result = Clone(remote)!;
        var conflicts = new List<string>();
        var keys = (baseline?.Select(p => p.Key) ?? []).Concat(local.Select(p => p.Key))
            .Concat(remote.Select(p => p.Key)).Distinct(StringComparer.Ordinal).ToArray();
        var grouped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in atomicGroups ?? [])
        {
            grouped.UnionWith(group);
            var localChanged = group.Any(k => !Equal(baseline, local, k));
            var remoteChanged = group.Any(k => !Equal(baseline, remote, k));
            if (localChanged && remoteChanged && group.Any(k => !Equal(local, remote, k)))
                conflicts.AddRange(group);
            if (localChanged) foreach (var key in group) Copy(local, result, key);
        }
        foreach (var key in keys.Where(k => !grouped.Contains(k)))
        {
            if (Equal(baseline, local, key)) continue;
            if (!Equal(baseline, remote, key) && !Equal(local, remote, key))
            {
                if (local[key] is JsonArray la && remote[key] is JsonArray ra &&
                    (baseline?[key] is JsonArray || baseline?[key] is null) && CanMergeById(la, ra))
                {
                    var array = MergeArray(baseline?[key] as JsonArray ?? [], la, ra, key, conflicts);
                    result[key] = array;
                    continue;
                }
                conflicts.Add(key);
            }
            Copy(local, result, key);
        }
        return new(result, conflicts.Distinct(StringComparer.Ordinal).ToArray());
    }

    public static JsonObject Patch(JsonObject baseline, JsonObject desired)
    {
        var patch = new JsonObject();
        foreach (var key in baseline.Select(p => p.Key).Concat(desired.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
            if (!Equal(baseline, desired, key)) patch[key] = desired[key]?.DeepClone();
        return patch;
    }

    private static string? ItemId(JsonNode? node) => node is JsonObject obj
        ? obj["id"] is { } id ? "id:" + id.GetValue<string>() : obj["email"] is { } email
            ? "email:" + email.GetValue<string>().ToUpperInvariant() : null : null;

    private static bool CanMergeById(JsonArray a, JsonArray b) => new[] { a, b }.All(array =>
        array.All(n => ItemId(n) is not null) && array.Select(ItemId).Distinct(StringComparer.Ordinal).Count() == array.Count);

    private static JsonArray MergeArray(JsonArray baseline, JsonArray local, JsonArray remote, string key, List<string> conflicts)
    {
        if (!CanMergeById(baseline, baseline))
        {
            conflicts.Add(key);
            return (JsonArray)local.DeepClone();
        }
        var result = new JsonArray();
        foreach (var id in remote.Select(ItemId).Concat(local.Select(ItemId)).Concat(baseline.Select(ItemId)).Distinct(StringComparer.Ordinal))
        {
            JsonObject? Find(JsonArray array) => array.FirstOrDefault(n => ItemId(n) == id) as JsonObject;
            var merged = Merge(Find(baseline), Find(local), Find(remote));
            conflicts.AddRange(merged.Conflicts.Select(c => $"{key}/{id}/{c}"));
            if (merged.Value is not null) result.Add(merged.Value);
        }
        return result;
    }

    private static bool Equal(JsonObject? a, JsonObject? b, string key) =>
        (a?.ContainsKey(key) ?? false) == (b?.ContainsKey(key) ?? false) && JsonNode.DeepEquals(a?[key], b?[key]);

    private static void Copy(JsonObject source, JsonObject target, string key)
    {
        if (source.ContainsKey(key)) target[key] = source[key]?.DeepClone();
        else target.Remove(key);
    }

    private static JsonObject? Clone(JsonObject? value) => (JsonObject?)value?.DeepClone();
}
