using System.Security.Cryptography;
using System.Text;
using UberDashboard.Core.ModularPanels;

namespace UberDashboard.App.Features.HomeAssistant;

/// <summary>Stan utrwalany dla instancji panelu Home Assistant.</summary>
public sealed class HomeAssistantPanelDocument
{
    /// <summary>Wersja schematu stanu panelu.</summary>
    public const int SchemaVersion = 1;

    public ModulePosition[] ModulePositions { get; init; } = [];
    public double ModuleSpacing { get; init; }
    /// <summary>Adres instancji, np. http://192.168.1.10:8123.</summary>
    public string? BaseUrl { get; init; }
    /// <summary>Token dostępu zaszyfrowany DPAPI (zakres bieżącego użytkownika).</summary>
    public string? ProtectedToken { get; init; }
    public int RefreshSeconds { get; init; } = 30;
    public HomeAssistantModuleState[] Modules { get; init; } = [];
}

/// <summary>Trwały opis modułu: encja HA, etykieta i rodzaj (odczyt albo akcja).</summary>
public sealed record HomeAssistantModuleState(string Id, string EntityId, string Label, HomeAssistantModuleKind Kind);

public enum HomeAssistantModuleKind { Sensor, Action }

/// <summary>Szyfruje token dostępu Home Assistant tym samym mechanizmem co tokeny synchronizacji.</summary>
internal static class HomeAssistantTokenProtector
{
    private static readonly byte[] Entropy = SHA256.HashData("UberDashboard/homeassistant/token/v1"u8);

    public static string Protect(string token) =>
        "dpapi1:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>Zwraca token albo <see langword="null"/>, gdy zapis jest uszkodzony lub pochodzi z innego konta.</summary>
    public static string? Unprotect(string? envelope)
    {
        if (envelope is null || !envelope.StartsWith("dpapi1:", StringComparison.Ordinal)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope[7..]), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
