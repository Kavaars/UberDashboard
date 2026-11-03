using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Opis typu panelu pogody.
/// </summary>
public sealed class WeatherPanelDescriptor : IPanelDescriptor
{
    /// <summary>Identyfikator typu panelu pogody.</summary>
    public const string TypeId = "weather";

    /// <summary>Tworzy deskryptor panelu pogody.</summary>
    public WeatherPanelDescriptor(IPanelFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
    }

    string IPanelDescriptor.TypeId => TypeId;

    /// <inheritdoc />
    public string DisplayName => "Pogoda";

    /// <inheritdoc />
    public string? IconKey => "panel.weather";

    /// <inheritdoc />
    public MinSize MinSize => new(320d, 150d);

    /// <inheritdoc />
    public int StateSchemaVersion => WeatherPanelDocument.SchemaVersion;

    /// <inheritdoc />
    public IPanelFactory Factory { get; }
}
