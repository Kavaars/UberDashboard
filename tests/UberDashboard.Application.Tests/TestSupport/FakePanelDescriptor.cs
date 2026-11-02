using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Tests.TestSupport;

/// <summary>Minimalny deskryptor panelu używany w testach rejestru typów.</summary>
internal sealed class FakePanelDescriptor : IPanelDescriptor
{
    public FakePanelDescriptor(string typeId, string? displayName = null, MinSize? minSize = null)
    {
        TypeId = typeId;
        DisplayName = displayName ?? typeId;
        MinSize = minSize ?? MinSize.Zero;
    }

    public string TypeId { get; }

    public string DisplayName { get; }

    public string? IconKey => null;

    public MinSize MinSize { get; }

    public int StateSchemaVersion => 1;

    public IPanelFactory Factory => throw new NotSupportedException("Testy nie tworzą instancji panelu.");
}
