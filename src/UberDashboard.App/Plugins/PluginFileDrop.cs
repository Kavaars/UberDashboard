using System.Windows;

namespace UberDashboard.App.Plugins;

/// <summary>Przechwytuje wyłącznie paczki pluginów, zachowuje przeciąganie paneli i obrazów.</summary>
public static class PluginFileDrop
{
    public static void Attach(Window window, PluginSettingsViewModel plugins)
    {
        window.AllowDrop = true;
        window.PreviewDragOver += (sender, e) =>
        {
            if (!TryPackage(e.Data, out _)) return;
            e.Effects = plugins.IsBusy ? System.Windows.DragDropEffects.None : System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        };
        window.PreviewDrop += async (_, e) =>
        {
            if (!TryPackage(e.Data, out var file)) return;
            e.Handled = true;
            await plugins.InstallFileAsync(file);
        };
    }
    private static bool TryPackage(System.Windows.IDataObject data, out string path)
    {
        path = "";
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            || data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: 1 } files
            || !string.Equals(System.IO.Path.GetExtension(files[0]), ".udplugin", StringComparison.OrdinalIgnoreCase)) return false;
        path = files[0]; return true;
    }
}
