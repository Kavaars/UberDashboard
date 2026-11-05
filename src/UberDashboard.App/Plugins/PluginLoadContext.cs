using UberDashboard.Core.Localization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Plugins;

/// <summary>Oddzielne zależności pluginu; kontrakty i framework pozostają wspólne z hostem.</summary>
internal sealed class PluginLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
{
    private string _directory = directory;
    public string DirectoryPath => _directory;
    public void SetDirectory(string value) => _directory = value;
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(IPanelInstance).Assembly.GetName().Name) return typeof(IPanelInstance).Assembly;
        if (name.Name == typeof(PanelInstanceId).Assembly.GetName().Name) return typeof(PanelInstanceId).Assembly;
        if (name.Name?.StartsWith("UberDashboard", StringComparison.Ordinal) == true)
            throw new FileLoadException(UiText.Get("PluginsMayReferenceOnlyThePublicContracts"));
        var path = Path.Combine(_directory, name.Name + ".dll");
        return File.Exists(path) ? LoadManaged(path) : null;
    }
    public Assembly LoadManaged(string path)
    {
        // Nie blokuje paczki na dysku: usuwanie i ponowna instalacja działają w tej samej sesji.
        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        => throw new DllNotFoundException(UiText.Get("PluginApi11DoesNotSupport"));
}
