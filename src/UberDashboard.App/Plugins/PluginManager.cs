using UberDashboard.Core.Localization;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Panels;
using UberDashboard.App.Panels;
using UberDashboard.Contracts;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Contracts.Plugins;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Plugins;

public sealed record InstalledPlugin(PluginManifest Manifest, string Directory, string Status)
{
    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public string Id => Manifest.Id;
}

/// <summary>Serializowane instalowanie i usuwanie; publikacja katalogu na wątku wywołującym (UI).</summary>
public sealed class PluginManager(IAppPaths paths, PanelCatalog catalog, PanelHostManager panels, ILoggerFactory loggerFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LoadedPlugin> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger = loggerFactory.CreateLogger<PluginManager>();
    private bool _initialized;
    public ObservableCollection<InstalledPlugin> Installed { get; } = [];
    public Func<Task>? RefreshPanelsAsync { get; set; }
    public static string HostVersion => typeof(PluginManager).Assembly.GetName().Version?.ToString(3) ?? "1.2.0";

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_initialized) return;
            var directories = await Task.Run(() =>
            {
                Directory.CreateDirectory(paths.PluginsDirectory);
                return System.IO.Directory.GetDirectories(paths.PluginsDirectory).Where(d => File.Exists(Path.Combine(d, ".enabled"))).Order(StringComparer.Ordinal).ToArray();
            }, token);
            foreach (var directory in directories)
            {
                PluginManifest? manifest = null;
                LoadedPlugin? loaded = null;
                try
                {
                    manifest = await Task.Run(async () =>
                    {
                        var json = await File.ReadAllTextAsync(Path.Combine(directory, "plugin.json"), token);
                        var value = JsonSerializer.Deserialize<PluginManifest>(json, PluginPackage.JsonOptions) ?? throw new InvalidDataException(UiText.Get("EmptyManifest"));
                        PluginPackage.ValidateManifest(value, HostVersion);
                        return value;
                    }, token);
                    loaded = await LoadAsync(directory, manifest, token);
                    Register(loaded);
                    Installed.Add(new(manifest, directory, UiText.Get("Active2")));
                }
                catch (OperationCanceledException) { loaded?.Context.Unload(); throw; }
                catch (Exception ex)
                {
                    loaded?.Context.Unload();
                    _logger.LogError(ex, "Nie załadowano pluginu {PluginId}.", manifest?.Id);
                    // Uszkodzona instalacja nadal widoczna i możliwa do usunięcia.
                    Installed.Add(new(manifest ?? new PluginManifest
                    {
                        Id = Path.GetFileName(directory), Name = UiText.Get("InvalidPackage"), Version = "?", ApiVersion = "?", MinHostVersion = "?",
                        TargetFramework = "?", EntryAssembly = "?", EntryType = "?",
                    }, directory, UiText.Get("CouldNotLoadThePluginCheckThe")));
                }
            }
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task InstallAsync(string package, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        var staging = Path.Combine(paths.PluginsDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        LoadedPlugin? loaded = null;
        string? committed = null;
        var published = false;
        try
        {
            var manifest = await Task.Run(() => PluginPackage.ExtractAsync(package, staging, HostVersion, token), token);
            if (Installed.Any(p => string.Equals(p.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(UiText.Get("ThisPluginIsAlreadyInstalledRemoveThe"));
            if (manifest.Panels.Any(p => catalog.Contains(p.TypeId))) throw new InvalidDataException(UiText.Get("ThePanelIdIsAlreadyInUse"));
            // Najpierw pełne ładowanie modułu i walidacja rejestracji. Błąd nie publikuje częściowej instalacji.
            loaded = await LoadAsync(staging, manifest, token);
            token.ThrowIfCancellationRequested();
            committed = Path.Combine(paths.PluginsDirectory, manifest.Id + "-" + Guid.NewGuid().ToString("N"));
            await Task.Run(async () =>
            {
                Directory.Move(staging, committed);
                await File.WriteAllTextAsync(Path.Combine(committed, ".enabled"), manifest.Version, CancellationToken.None);
            }, CancellationToken.None);
            loaded.Context.SetDirectory(committed);
            Register(loaded);
            Installed.Add(new(manifest, committed, UiText.Get("Active2")));
            published = true;
            _logger.LogInformation("Zainstalowano plugin {PluginId} {Version}.", manifest.Id, manifest.Version);
        }
        finally
        {
            if (!published) loaded?.Context.Unload();
            await CleanupAsync(staging);
            if (!published && committed is not null) await CleanupAsync(committed);
            _gate.Release();
        }
        await RefreshAsync();
    }

    public async Task RemoveAsync(InstalledPlugin plugin, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Installed.Contains(plugin)) return;
            // Zapis musi się udać przed wyłączeniem kodu. Nie kasujemy rekordów ani PluginData.
            var ownsCode = _loaded.TryGetValue(plugin.Id, out var active) && string.Equals(active.Context.DirectoryPath, plugin.Directory, StringComparison.OrdinalIgnoreCase);
            if (ownsCode) await panels.SuspendPluginAsync(plugin.Id, token);
            await Task.Run(() => File.Delete(Path.Combine(plugin.Directory, ".enabled")), token);
            if (ownsCode)
            {
                catalog.RemovePlugin(plugin.Id);
                if (_loaded.Remove(plugin.Id, out var loaded)) loaded.Context.Unload();
            }
            Installed.Remove(plugin);
            await CleanupAsync(plugin.Directory);
            _logger.LogInformation("Usunięto plugin {PluginId}; zachowano stan instancji.", plugin.Id);
        }
        finally { _gate.Release(); await RefreshAsync(); }
    }

    private async Task RefreshAsync()
    {
        if (RefreshPanelsAsync is null) return;
        try { await RefreshPanelsAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Nie odświeżono paneli po zmianie pluginów. Użyj ponownego wczytania pulpitu."); }
    }

    private Task<LoadedPlugin> LoadAsync(string directory, PluginManifest manifest, CancellationToken token) => Task.Run(async () =>
    {
        if (_loaded.ContainsKey(manifest.Id)) throw new InvalidDataException(UiText.Get("DuplicatePluginId"));
        var context = new PluginLoadContext(directory);
        try
        {
            var assembly = context.LoadManaged(Path.Combine(directory, manifest.EntryAssembly));
            var module = Activator.CreateInstance(assembly.GetType(manifest.EntryType, throwOnError: true)!) as IPluginModule
                ?? throw new InvalidDataException(UiText.Get("TheEntryTypeDoesNotImplementIpluginmodule"));
            var host = new RegistrationContext(manifest, loggerFactory);
            await module.InitializeAsync(host, token);
            host.Seal();
            if (host.Descriptors.Count != manifest.Panels.Count || manifest.Panels.Any(p => !host.Descriptors.Any(d => d.TypeId == p.TypeId)))
                throw new InvalidDataException(UiText.Get("ModuleRegistrationsDoNotMatchTheManifest"));
            return new LoadedPlugin(manifest, context, host.Descriptors.ToArray(), module);
        }
        catch { context.Unload(); throw; }
    }, token);

    private void Register(LoadedPlugin plugin)
    {
        if (plugin.Descriptors.Any(d => catalog.Contains(d.TypeId))) throw new InvalidDataException(UiText.Get("ConflictingPanelTypes"));
        foreach (var descriptor in plugin.Descriptors) catalog.TryRegister(plugin.Manifest.Id, descriptor);
        _loaded.Add(plugin.Manifest.Id, plugin);
    }

    private async Task CleanupAsync(string directory)
    {
        try { await Task.Run(() => { if (Directory.Exists(directory)) Directory.Delete(directory, true); }); }
        catch (IOException ex) { _logger.LogWarning(ex, "Nie usunięto nieaktywnej paczki pluginu; kod nie będzie ładowany."); }
        catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Nie usunięto nieaktywnej paczki pluginu; kod nie będzie ładowany."); }
    }

    private sealed record LoadedPlugin(PluginManifest Manifest, PluginLoadContext Context, IPanelDescriptor[] Descriptors, IPluginModule Module);
    private sealed class RegistrationContext(PluginManifest manifest, ILoggerFactory logs) : IPluginHostContext
    {
        private bool _sealed;
        public List<IPanelDescriptor> Descriptors { get; } = [];
        public string HostVersion => PluginManager.HostVersion;
        public string ContractVersion => ContractVersions.PanelApiVersion;
        public IPanelLogger Logger { get; } = new PanelLogger(logs, manifest.Id, Guid.Empty, PanelInstanceId.New());
        public void Seal() => _sealed = true;
        public void RegisterPanel(IPanelDescriptor descriptor)
        {
            if (_sealed) throw new InvalidOperationException(UiText.Get("RegistrationIsAvailableOnlyDuringInitializeasync"));
            if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.TypeId) || string.IsNullOrWhiteSpace(descriptor.DisplayName)
                || descriptor.Factory is null || descriptor.StateSchemaVersion < 1
                || !double.IsFinite(descriptor.MinSize.Width) || !double.IsFinite(descriptor.MinSize.Height)
                || descriptor.MinSize.Width < 0 || descriptor.MinSize.Height < 0 || Descriptors.Any(d => d.TypeId == descriptor.TypeId))
                throw new InvalidDataException(UiText.Get("InvalidOrDuplicatePanelDescriptor"));
            Descriptors.Add(descriptor);
        }
    }
}
