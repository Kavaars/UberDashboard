namespace UberDashboard.Application.Platform;

/// <summary>Obsługiwane tryby głównego okna aplikacji.</summary>
public enum WindowMode
{
    /// <summary>Zwykłe okno aplikacji.</summary>
    Normal = 0,

    /// <summary>Dokowany pasek rezerwujący obszar roboczy monitora.</summary>
    DockedBar = 1,

    /// <summary>Tryb widgetu pulpitu aktywowany w modelu best effort.</summary>
    DesktopWidget = 2,
}
