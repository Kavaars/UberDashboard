using CommunityToolkit.Mvvm.ComponentModel;

namespace UberDashboard.App.Shell;

/// <summary>Bazowy model widoku elementu układu pulpitu.</summary>
public abstract class LayoutNodeViewModel : ObservableObject
{
    /// <summary>Minimalny rozmiar panelu w DIP.</summary>
    public UberDashboard.Core.Layout.MinSize Minimum { get; set; }
}
