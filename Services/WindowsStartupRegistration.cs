using Microsoft.Win32;

namespace SmyruRGB;

internal sealed class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppEntryName = "SmyruRGB";

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppEntryName) is string existingValue && !string.IsNullOrWhiteSpace(existingValue);
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to determine the current executable path.");
            key.SetValue(AppEntryName, $"\"{executablePath}\"");
            return;
        }

        key.DeleteValue(AppEntryName, throwOnMissingValue: false);
    }
}