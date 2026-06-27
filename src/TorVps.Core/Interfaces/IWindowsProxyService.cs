namespace TorVps.Core.Interfaces;

/// <summary>Controls the per-user Windows HTTP/HTTPS system proxy via the registry.</summary>
public interface IWindowsProxyService
{
    /// <summary>Points the system proxy at 127.0.0.1:port for http and https, with the local subnet bypassed.</summary>
    void Enable(int port);

    /// <summary>Turns the system proxy off.</summary>
    void Disable();
}
