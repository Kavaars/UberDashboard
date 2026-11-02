using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Tests.TestSupport;

/// <summary>Magazyn układu w pamięci używany przez testy edytora.</summary>
internal sealed class FakeLayoutStore : ILayoutStore
{
    private LayoutTree _tree = LayoutTree.Empty;
    private int _schemaVersion = 1;
    private LayoutLoadResult? _forcedResult;

    public int SaveCount { get; private set; }

    public LayoutTree LastSaved { get; private set; } = LayoutTree.Empty;

    public void Seed(LayoutTree tree, int schemaVersion = 1)
    {
        _tree = tree;
        _schemaVersion = schemaVersion;
    }

    public void ForceLoadResult(LayoutLoadResult result) => _forcedResult = result;

    public Task<LayoutLoadResult> LoadAsync(Guid dashboardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_forcedResult ?? new LayoutLoadResult(_tree, _schemaVersion, LayoutErrorCode.None, null, false));

    public Task SaveAsync(Guid dashboardId, LayoutTree tree, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        LastSaved = tree;
        _tree = tree;
        return Task.CompletedTask;
    }
}
