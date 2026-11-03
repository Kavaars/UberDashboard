using System.Windows;
using System.Windows.Controls;
using UberDashboard.App.Shell;
using UberDashboard.Contracts.Panels;

namespace UberDashboard.App.Controls;

/// <summary>Przekazuje bieżący rozmiar widokowi lub modelowi panelu, jeśli obsługuje on kontrakt.</summary>
public sealed class ResponsivePanelContent : ContentControl
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        NotifyPanel();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        NotifyPanel();
    }

    private void NotifyPanel()
    {
        var responsive = Content as IResponsivePanel ?? (DataContext as PanelLeafViewModel)?.ViewModel as IResponsivePanel;
        if (responsive is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        try { responsive.OnPanelSizeChanged(ActualWidth, ActualHeight); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Błąd dostosowania panelu: {0}", ex.GetType().Name); }
    }
}
