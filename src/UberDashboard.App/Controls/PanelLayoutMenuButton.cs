using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace UberDashboard.App.Controls;

public sealed class PanelLayoutMenuButton : System.Windows.Controls.Button
{
    protected override void OnClick()
    {
        base.OnClick();
        if (ContextMenu is null) return;
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = PlacementMode.Bottom;
        ContextMenu.IsOpen = true;
    }
}
