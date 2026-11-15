using System.Text.RegularExpressions;

namespace UberDashboard.Application.HomeAssistant;

/// <summary>Klient REST API Home Assistant.</summary>
public interface IHomeAssistantService
{
    /// <summary>Pobiera stan encji (np. sensor.temperatura).</summary>
    Task<HomeAssistantEntityState> GetStateAsync(HomeAssistantConnection connection, string entityId, CancellationToken cancellationToken);

    /// <summary>Wykonuje akcję na encji (przełącz, skrypt, scena albo naciśnięcie przycisku).</summary>
    Task CallServiceAsync(HomeAssistantConnection connection, string entityId, CancellationToken cancellationToken);
}

/// <summary>Adres bazowy i token dostępu do instancji Home Assistant.</summary>
public sealed record HomeAssistantConnection(Uri BaseAddress, string Token);

/// <summary>Stan encji zwrócony przez Home Assistant.</summary>
public sealed record HomeAssistantEntityState(string State, string? Unit, string? FriendlyName);

/// <summary>Walidacja danych wejściowych Home Assistant przed wysłaniem do sieci.</summary>
public static class HomeAssistantValidation
{
    private static readonly Regex EntityPattern = new("^[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

    /// <summary>Zwraca adres http(s) z końcowym ukośnikiem; rzuca <see cref="ArgumentException"/> dla błędnego adresu.</summary>
    public static Uri NormalizeBaseUrl(string? value)
    {
        if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            uri.Scheme == "http" && !IsLocalHost(uri))
            throw new ArgumentException("Invalid Home Assistant address.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/') + "/");
    }

    // The token is sent as a Bearer header, so plain http is allowed only inside the local network.
    private static bool IsLocalHost(Uri uri)
    {
        if (uri.IsLoopback || uri.HostNameType == UriHostNameType.Dns && !uri.Host.Contains('.')) return true;
        if (uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)) return true;
        if (!System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254);
    }

    /// <summary>Zwraca poprawne ID encji (np. light.salon) albo rzuca <see cref="ArgumentException"/>.</summary>
    public static string NormalizeEntityId(string? value)
    {
        var entityId = (value ?? "").Trim();
        if (!EntityPattern.IsMatch(entityId)) throw new ArgumentException("Invalid entity id.");
        return entityId;
    }

    /// <summary>Usługa HA wykonująca akcję dla domeny encji.</summary>
    public static (string Domain, string Service) ServiceFor(string entityId)
    {
        var domain = entityId[..entityId.IndexOf('.')];
        return domain switch
        {
            "button" => (domain, "press"),
            "script" or "scene" => (domain, "turn_on"),
            _ => ("homeassistant", "toggle"),
        };
    }
}
