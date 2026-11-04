using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using UberDashboard.App.Toasts;

namespace UberDashboard.App.Plugins;

/// <summary>Wspólna obsługa instalacji z dialogu i przeciągania; rzeczywisty stan operacji.</summary>
public sealed class PluginSettingsViewModel : ObservableObject
{
    private readonly PluginManager _manager;
    private readonly IToastService _toasts;
    private readonly ILogger<PluginSettingsViewModel> _logger;
    private CancellationTokenSource? _operation;
    private Task _pending = Task.CompletedTask;
    private string _status = "Wybierz paczkę .udplugin lub przeciągnij ją na okno aplikacji.";
    public PluginSettingsViewModel(PluginManager manager, IToastService toasts, ILogger<PluginSettingsViewModel> logger)
    {
        _manager = manager; _toasts = toasts; _logger = logger;
        InstallCommand = new AsyncRelayCommand(ChooseAsync, () => !IsBusy);
        RemoveCommand = new AsyncRelayCommand<InstalledPlugin>(RemoveAsync, p => p is not null && !IsBusy);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => IsBusy);
    }
    public ObservableCollection<InstalledPlugin> Installed => _manager.Installed;
    public bool IsBusy => _operation is not null;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public IAsyncRelayCommand InstallCommand { get; }
    public IAsyncRelayCommand<InstalledPlugin> RemoveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    private async Task ChooseAsync()
    {
        var dialog = new UberDashboard.App.Controls.ThemedFileDialog { Filter = "Plugin UberDashboard (*.udplugin)|*.udplugin", Title = "Zainstaluj plugin", Multiselect = false };
        if (dialog.ShowDialog() == true) await InstallFileAsync(dialog.FileName);
    }
    public Task InstallFileAsync(string file) => StartAsync("Sprawdzanie i instalowanie pluginu…", async token =>
    {
        await _manager.InstallAsync(file, token);
        return "Plugin zainstalowany. Panel jest dostępny w menu Dodaj panel.";
    });
    private Task RemoveAsync(InstalledPlugin? plugin) => plugin is null ? Task.CompletedTask : StartAsync("Usuwanie pluginu…", async token =>
    {
        await _manager.RemoveAsync(plugin, token);
        return "Plugin usunięty. Zachowano dane i miejsca paneli; ponowna instalacja je przywróci.";
    });
    public async Task StopAsync()
    {
        _operation?.Cancel();
        await _pending;
    }
    private Task StartAsync(string status, Func<CancellationToken, Task<string>> action)
    {
        if (IsBusy) { _toasts.Show("Trwa inna operacja pluginów.", ToastSeverity.Information); return Task.CompletedTask; }
        _pending = RunAsync(status, action);
        return _pending;
    }
    private async Task RunAsync(string status, Func<CancellationToken, Task<string>> action)
    {
        if (IsBusy) { _toasts.Show("Trwa inna operacja pluginów.", ToastSeverity.Information); return; }
        using var operation = new CancellationTokenSource();
        _operation = operation; NotifyBusy(); Status = status;
        try
        {
            Status = await action(operation.Token);
            _toasts.Show(Status, ToastSeverity.Success);
        }
        catch (OperationCanceledException) { Status = "Operacja anulowana."; _toasts.Show(Status); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operacja pluginu nie powiodła się.");
            Status = ex is System.IO.InvalidDataException ? ex.Message : "Operacja pluginu nie powiodła się. Sprawdź logi i spróbuj ponownie.";
            _toasts.Show(Status, ToastSeverity.Error);
        }
        finally { _operation = null; NotifyBusy(); }
    }
    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy)); InstallCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }
}
