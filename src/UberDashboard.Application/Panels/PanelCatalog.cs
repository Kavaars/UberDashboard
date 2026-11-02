using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Panels;

/// <summary>
/// Rejestracja typu panelu: deskryptor wraz z identyfikatorem wtyczki, która go
/// zgłosiła. Wbudowane panele mają wtyczkę hosta.
/// </summary>
public sealed record PanelRegistration(string PluginId, IPanelDescriptor Descriptor);

/// <summary>
/// Rejestr typów paneli dostępnych w hoście. Kluczowany po <c>TypeId</c>, który
/// musi być unikalny w całej aplikacji. Rejestr jest tylko do odczytu w trakcie pracy.
/// </summary>
public sealed class PanelCatalog
{
    private readonly Dictionary<string, PanelRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly List<PanelRegistration> _order = [];

    /// <summary>Zarejestrowane typy paneli w kolejności rejestracji.</summary>
    public IReadOnlyList<PanelRegistration> Registrations => _order;

    /// <summary>Zwraca rejestrację typu albo <see langword="null"/>.</summary>
    public PanelRegistration? Find(string typeId) =>
        _registrations.TryGetValue(typeId, out var registration) ? registration : null;

    /// <summary>Zwraca deskryptor typu albo <see langword="null"/>.</summary>
    public IPanelDescriptor? DescriptorOf(string typeId) => Find(typeId)?.Descriptor;

    /// <summary>Sprawdza, czy typ panelu jest zarejestrowany.</summary>
    public bool Contains(string typeId) => _registrations.ContainsKey(typeId);

    /// <summary>
    /// Rejestruje typ panelu. Zwraca <see langword="false"/>, gdy <c>TypeId</c> już
    /// istnieje — wówczas poprzednia rejestracja zostaje nienaruszona.
    /// </summary>
    public bool TryRegister(string pluginId, IPanelDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TypeId);

        if (_registrations.ContainsKey(descriptor.TypeId))
        {
            return false;
        }

        var registration = new PanelRegistration(pluginId, descriptor);
        _registrations.Add(descriptor.TypeId, registration);
        _order.Add(registration);
        return true;
    }

    /// <summary>Usuwa wszystkie typy paneli zgłoszone przez wskazaną wtyczkę.</summary>
    public void RemovePlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        _order.RemoveAll(registration =>
        {
            if (!string.Equals(registration.PluginId, pluginId, StringComparison.Ordinal))
            {
                return false;
            }

            _registrations.Remove(registration.Descriptor.TypeId);
            return true;
        });
    }

    /// <summary>
    /// Źródło minimów rozmiaru dla układu: mapuje instancję na minimum jej typu
    /// panelu. Nieznane typy dostają minimum zerowe, aby nie blokować edycji.
    /// </summary>
    public sealed class MinSizeSource : IPanelMinSizeSource
    {
        private readonly Dictionary<PanelInstanceId, MinSize> _sizes = [];

        /// <summary>Ustawia minimum instancji.</summary>
        public void Set(PanelInstanceId instanceId, MinSize size) => _sizes[instanceId] = size;

        /// <summary>Usuwa wpis instancji.</summary>
        public bool Remove(PanelInstanceId instanceId) => _sizes.Remove(instanceId);

        /// <inheritdoc />
        public MinSize MinimumOf(PanelInstanceId instanceId) =>
            _sizes.TryGetValue(instanceId, out var size) ? size : MinSize.Zero;
    }
}
