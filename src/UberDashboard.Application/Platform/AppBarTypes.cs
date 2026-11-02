namespace UberDashboard.Application.Platform;

/// <summary>
/// Krawędź ekranu, do której dokowany jest pasek. Wartości odpowiadają stałym
/// <c>ABE_*</c> z AppBar API, aby mapowanie pozostało jednoznaczne.
/// </summary>
public enum AppBarEdge
{
    /// <summary>Lewa krawędź monitora.</summary>
    Left = 0,

    /// <summary>Górna krawędź monitora.</summary>
    Top = 1,

    /// <summary>Prawa krawędź monitora.</summary>
    Right = 2,

    /// <summary>Dolna krawędź monitora.</summary>
    Bottom = 3,
}

/// <summary>Stan paska zgłoszony przez powłokę; wartości odpowiadają stałym <c>ABS_*</c>.</summary>
[Flags]
public enum AppBarState
{
    /// <summary>Brak flag — pasek zadokowany, zawsze widoczny.</summary>
    None = 0,

    /// <summary>Powłoka zgłasza tryb automatycznego ukrywania (w v1 nieobsługiwany).</summary>
    AutoHide = 0x00000001,

    /// <summary>Pasek jest utrzymywany nad innymi oknami.</summary>
    AlwaysOnTop = 0x00000002,
}
