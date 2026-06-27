using Microsoft.Win32;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

/// <summary>
/// WindowsProxyService writes directly to Microsoft.Win32.Registry with no abstraction to mock, so these
/// tests exercise the real per-user key. Each test snapshots the original values in the constructor and
/// restores them in Dispose, so running the suite does not permanently change the developer's machine.
/// </summary>
public sealed class WindowsProxyServiceTests : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private readonly object? _originalProxyEnable;
    private readonly object? _originalProxyServer;
    private readonly object? _originalProxyOverride;
    private readonly WindowsProxyService _service = new();

    public WindowsProxyServiceTests()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        _originalProxyEnable = key?.GetValue("ProxyEnable");
        _originalProxyServer = key?.GetValue("ProxyServer");
        _originalProxyOverride = key?.GetValue("ProxyOverride");
    }

    [Fact]
    public void Enable_WritesProxyServerAndOverrideForGivenPort()
    {
        _service.Enable(18080);

        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        Assert.Equal(1, key?.GetValue("ProxyEnable"));
        Assert.Equal("http=127.0.0.1:18080;https=127.0.0.1:18080", key?.GetValue("ProxyServer"));
        Assert.Equal("<local>", key?.GetValue("ProxyOverride"));
    }

    [Fact]
    public void Disable_ClearsProxyEnableFlagOnly()
    {
        _service.Enable(18080);
        _service.Disable();

        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        Assert.Equal(0, key?.GetValue("ProxyEnable"));
        Assert.Equal("http=127.0.0.1:18080;https=127.0.0.1:18080", key?.GetValue("ProxyServer"));
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key is null)
            return;

        Restore(key, "ProxyEnable", _originalProxyEnable);
        Restore(key, "ProxyServer", _originalProxyServer);
        Restore(key, "ProxyOverride", _originalProxyOverride);
    }

    private static void Restore(RegistryKey key, string name, object? original)
    {
        if (original is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, original);
    }
}
