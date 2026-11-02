using System.Text.Json;
using System.Text.Json.Nodes;

namespace UberDashboard.Core.Layout.Serialization;

/// <summary>
/// Wersjonowana serializacja dokumentu układu. Zapis i odczyt są rozdzielone od
/// modelu domenowego, dzięki czemu migracje mogą działać na kształcie dokumentu,
/// a nie na typach runtime.
/// </summary>
public static class LayoutSerializer
{
    /// <summary>Bieżąca wersja schematu dokumentu układu.</summary>
    public const int CurrentSchemaVersion = 1;

    private const string KindProperty = "kind";
    private const string KindLeaf = "leaf";
    private const string KindSplit = "split";
    private const string AxisHorizontal = "horizontal";
    private const string AxisVertical = "vertical";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Serializuje układ do dokumentu JSON w bieżącej wersji schematu.</summary>
    public static string Serialize(LayoutTree tree, LayoutLimits limits)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var document = new JsonObject
        {
            ["schemaVersion"] = CurrentSchemaVersion,
        };

        if (tree.Root is not null)
        {
            document["root"] = ToJson(tree.Root, limits, 1);
        }

        return document.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Odczytuje dokument układu: sprawdza wersję, stosuje migracje na kopii
    /// w pamięci i mapuje wynik na drzewo. Dokument o nieznanej lub uszkodzonej
    /// postaci nie jest interpretowany i musi zostać zachowany w oryginale.
    /// </summary>
    public static LayoutDeserializeResult Deserialize(
        string json,
        LayoutLimits limits,
        LayoutSchemaMigrations? migrations = null,
        int targetSchemaVersion = CurrentSchemaVersion)
    {
        migrations ??= LayoutSchemaMigrations.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            return LayoutDeserializeResult.Fail(LayoutErrorCode.MalformedDocument, 0, "Dokument układu jest pusty.");
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(
                json,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = limits.MaxDepth + 16,
                });
        }
        catch (JsonException ex)
        {
            return LayoutDeserializeResult.Fail(LayoutErrorCode.MalformedDocument, 0, $"Niepoprawny JSON: {ex.Message}");
        }

        if (parsed is not JsonObject document)
        {
            return LayoutDeserializeResult.Fail(LayoutErrorCode.MalformedDocument, 0, "Dokument układu musi być obiektem JSON.");
        }

        if (!TryReadInt(document, "schemaVersion", out var version) || version <= 0)
        {
            return LayoutDeserializeResult.Fail(LayoutErrorCode.MalformedDocument, 0, "Brak poprawnego pola schemaVersion.");
        }

        if (version > targetSchemaVersion)
        {
            return LayoutDeserializeResult.Fail(
                LayoutErrorCode.UnknownSchemaVersion,
                version,
                $"Wersja schematu {version} jest nowsza niż obsługiwana ({targetSchemaVersion}).");
        }

        var migrated = document;
        while (version < targetSchemaVersion)
        {
            if (!migrations.TryGetMigration(version, out var migration))
            {
                return LayoutDeserializeResult.Fail(
                    LayoutErrorCode.UnknownSchemaVersion,
                    version,
                    $"Brak migracji z wersji {version} do {targetSchemaVersion}.");
            }

            try
            {
                migrated = migration.Apply(migrated);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return LayoutDeserializeResult.Fail(
                    LayoutErrorCode.MalformedDocument,
                    version,
                    $"Migracja z wersji {version} nie powiodła się: {ex.Message}");
            }

            version = migration.ToVersion;
        }

        try
        {
            if (migrated["root"] is not { } rootNode)
            {
                return LayoutDeserializeResult.Ok(LayoutTree.Empty, version);
            }

            var context = new MappingContext();
            var root = MapNode(rootNode, limits, context, 1);

            if (context.HasError || root is null)
            {
                return LayoutDeserializeResult.Fail(
                    context.HasError ? context.ErrorCode : LayoutErrorCode.MalformedDocument,
                    version,
                    context.ErrorMessage ?? "Nie udało się odczytać korzenia układu.");
            }

            return LayoutDeserializeResult.Ok(new LayoutTree(root), version);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return LayoutDeserializeResult.Fail(LayoutErrorCode.MalformedDocument, version, ex.Message);
        }
    }

    private static JsonObject ToJson(LayoutNode node, LayoutLimits limits, int depth)
    {
        if (depth > limits.MaxDepth)
        {
            throw new InvalidOperationException("Układ przekracza limit głębokości i nie może zostać zapisany.");
        }

        switch (node)
        {
            case LeafNode leaf:
                return new JsonObject
                {
                    [KindProperty] = KindLeaf,
                    ["instanceId"] = leaf.InstanceId.ToString(),
                };

            case SplitNode split:
                return new JsonObject
                {
                    [KindProperty] = KindSplit,
                    ["splitId"] = split.SplitId.ToString("D"),
                    ["axis"] = split.Axis == SplitAxis.Horizontal ? AxisHorizontal : AxisVertical,
                    ["ratio"] = split.Ratio,
                    ["first"] = ToJson(split.First, limits, depth + 1),
                    ["second"] = ToJson(split.Second, limits, depth + 1),
                };

            default:
                throw new InvalidOperationException($"Nieznany typ węzła: {node.GetType().Name}.");
        }
    }

    private static LayoutNode? MapNode(JsonNode? node, LayoutLimits limits, MappingContext context, int depth)
    {
        if (context.HasError)
        {
            return null;
        }

        if (depth > limits.MaxDepth)
        {
            context.Fail(LayoutErrorCode.DepthLimitExceeded, $"Przekroczono limit głębokości dokumentu ({limits.MaxDepth}).");
            return null;
        }

        context.NodeCount++;
        if (context.NodeCount > limits.MaxNodes)
        {
            context.Fail(LayoutErrorCode.NodeLimitExceeded, $"Przekroczono limit liczby węzłów ({limits.MaxNodes}).");
            return null;
        }

        if (node is not JsonObject obj)
        {
            context.Fail(LayoutErrorCode.MalformedDocument, "Węzeł układu musi być obiektem JSON.");
            return null;
        }

        var kind = ReadString(obj, KindProperty);
        switch (kind)
        {
            case KindLeaf:
            {
                if (!TryReadGuid(obj, "instanceId", out var instanceId) || instanceId == Guid.Empty)
                {
                    context.Fail(LayoutErrorCode.MalformedDocument, "Liść nie ma poprawnego identyfikatora instancji.");
                    return null;
                }

                return new LeafNode(new PanelInstanceId(instanceId));
            }

            case KindSplit:
            {
                if (!TryReadGuid(obj, "splitId", out var splitId) || splitId == Guid.Empty)
                {
                    context.Fail(LayoutErrorCode.MalformedDocument, "Podział nie ma poprawnego identyfikatora.");
                    return null;
                }

                var axis = ReadString(obj, "axis") switch
                {
                    AxisHorizontal => SplitAxis.Horizontal,
                    AxisVertical => SplitAxis.Vertical,
                    _ => (SplitAxis?)null,
                };

                if (axis is null)
                {
                    context.Fail(LayoutErrorCode.MalformedDocument, "Podział ma nieznaną oś.");
                    return null;
                }

                if (!TryReadDouble(obj, "ratio", out var ratio) || !double.IsFinite(ratio) || ratio <= 0d || ratio >= 1d)
                {
                    context.Fail(LayoutErrorCode.InvalidRatio, "Podział ma niepoprawną proporcję.");
                    return null;
                }

                var firstNode = obj["first"];
                var secondNode = obj["second"];
                if (firstNode is null || secondNode is null)
                {
                    context.Fail(LayoutErrorCode.MissingChild, "Podział nie ma dwójki dzieci.");
                    return null;
                }

                var first = MapNode(firstNode, limits, context, depth + 1);
                var second = MapNode(secondNode, limits, context, depth + 1);

                if (context.HasError || first is null || second is null)
                {
                    return null;
                }

                return new SplitNode(splitId, axis.Value, ratio, first, second);
            }

            default:
                context.Fail(LayoutErrorCode.MalformedDocument, $"Nieznany rodzaj węzła: {kind ?? "(brak)"}.");
                return null;
        }
    }

    private static string? ReadString(JsonObject obj, string property) =>
        obj[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool TryReadInt(JsonObject obj, string property, out int result)
    {
        result = 0;
        return obj[property] is JsonValue value && value.TryGetValue<int>(out result);
    }

    private static bool TryReadDouble(JsonObject obj, string property, out double result)
    {
        result = 0d;
        return obj[property] is JsonValue value && value.TryGetValue<double>(out result);
    }

    private static bool TryReadGuid(JsonObject obj, string property, out Guid result)
    {
        result = Guid.Empty;
        return obj[property] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Guid.TryParse(text, out result);
    }

    private sealed class MappingContext
    {
        public int NodeCount { get; set; }

        public LayoutErrorCode ErrorCode { get; private set; } = LayoutErrorCode.None;

        public string? ErrorMessage { get; private set; }

        public bool HasError => ErrorCode != LayoutErrorCode.None;

        public void Fail(LayoutErrorCode code, string message)
        {
            if (HasError)
            {
                return;
            }

            ErrorCode = code;
            ErrorMessage = message;
        }
    }
}
