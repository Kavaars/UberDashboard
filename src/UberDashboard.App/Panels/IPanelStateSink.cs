using UberDashboard.Core.Layout;

namespace UberDashboard.App.Panels;

/// <summary>
/// Ujście zapisu stanu pojedynczej instancji panelu. Pozwala harmonogramowi
/// autozapisu utrwalić stan bez znajomości typu panelu ani jego modelu widoku.
/// </summary>
public interface IPanelStateSink
{
    /// <summary>Identyfikator instancji, której stan jest utrwalany.</summary>
    PanelInstanceId InstanceId { get; }

    /// <summary>Przechwytuje bieżący stan instancji i zapisuje go w magazynie danych.</summary>
    Task PersistAsync(CancellationToken cancellationToken);
}
