using Microsoft.Win32;

namespace UberDashboard.App.SystemIntegration;

/// <summary>Autostart przez klucz Run w rejestrze bieżącego użytkownika.</summary>
public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UberDashboard";

    public bool IsEnabled(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return string.Equals(value, Quote(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void Apply(bool enabled, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, Quote(executablePath));
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string Quote(string executablePath) => $"\"{executablePath}\"";
}
