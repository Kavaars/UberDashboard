namespace UberDashboard.App.Theming;

/// <summary>
/// Wariant motywu interfejsu wybierany przez użytkownika. Wartość jest zapisywana
/// w ustawieniach aplikacji jako dokument JSON, dlatego kolejność wartości jest
/// częścią trwałego kontraktu.
/// </summary>
public enum ThemeKind
{
    /// <summary>Motyw jasny.</summary>
    Light = 0,

    /// <summary>Motyw ciemny.</summary>
    Dark = 1,

    /// <summary>Motyw zgodny z ustawieniem systemowym Windows.</summary>
    System = 2,
}
