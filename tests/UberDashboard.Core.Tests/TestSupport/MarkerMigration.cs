using System.Text.Json.Nodes;
using UberDashboard.Core.Layout.Serialization;

namespace UberDashboard.Core.Tests.TestSupport;

/// <summary>
/// Migracja testowa: dopisuje znacznik wersji, nie zmieniając kształtu węzłów.
/// Pozwala sprawdzić łańcuch migracji bez zmiany formatu obsługiwanego przez mapper.
/// </summary>
internal sealed class MarkerMigration(int fromVersion) : ILayoutSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion { get; } = fromVersion;

    /// <inheritdoc />
    public int ToVersion { get; } = fromVersion + 1;

    /// <inheritdoc />
    public JsonObject Apply(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var clone = (JsonObject)document.DeepClone();
        clone["schemaVersion"] = ToVersion;
        clone["migratedTo"] = ToVersion;
        return clone;
    }
}
