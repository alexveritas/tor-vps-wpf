using Microsoft.Win32;
using TorVps.Core.Interfaces;

namespace TorVps.Core.Services;

/// <summary>Toggles the per-user Windows HTTP/HTTPS system proxy by writing the Internet Settings registry keys.</summary>
public sealed class WindowsProxyService : IWindowsProxyService
{
    private const string InternetSettingsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public void Enable(int port)
    {
        TryRegistry(key =>
        {
            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", $"http=127.0.0.1:{port};https=127.0.0.1:{port}", RegistryValueKind.String);
            key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
        });
    }

    public void Disable()
    {
        TryRegistry(key => key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord));
    }

    private static void TryRegistry(Action<RegistryKey> write)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true);
            if (key is not null)
                write(key);
        }
        catch (Exception)
        {
            // Best-effort: matches the original behavior of silently discarding reg.exe failures.
        }
    }
}
