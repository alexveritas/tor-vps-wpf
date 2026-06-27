namespace TorVps.Core.Interfaces;

/// <summary>Owns the mihomo.exe child process: start/stop lifecycle and config hot-reload via its HTTP API.</summary>
public interface IMihomoService
{
    bool IsAlive { get; }

    /// <summary>
    /// Starts mihomo.exe and enables the Windows system proxy once ports are ready. Throws
    /// <see cref="InvalidOperationException"/> if Tor is not reachable yet, or if mihomo exits or
    /// never opens its ports before the readiness timeout (the process is killed and the proxy
    /// disabled before the exception propagates).
    /// </summary>
    Task<string> StartAsync(string baseDirectory, CancellationToken cancellationToken = default);

    /// <summary>Stops mihomo.exe, disables the system proxy, and waits for its ports to close.</summary>
    Task<string> StopAsync(string baseDirectory, CancellationToken cancellationToken = default);

    /// <summary>Pushes the on-disk mihomo.yaml to the running process via PUT /configs?force=true. Does not restart the process.</summary>
    Task<string> HotReloadRulesAsync(string baseDirectory, CancellationToken cancellationToken = default);
}
