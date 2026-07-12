namespace TorVps.Core.Interfaces;

/// <summary>Owns the tor.exe child process: torrc generation/config parsing and start/stop lifecycle.</summary>
public interface ITorService
{
    bool IsAlive { get; }
    DateTimeOffset? StartedAt { get; }
    int RestartCount { get; }

    /// <summary>Generates torrc from torrc_manager.cfg + bridges.txt and starts tor.exe. No-op if already running.</summary>
    Task<string> StartAsync(string baseDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the whole chain in the required order: mihomo + system proxy first, then tor.exe/lyrebird.exe.
    /// Never throws; mihomo/proxy shutdown failures are logged and do not prevent Tor from stopping.
    /// </summary>
    Task StopAsync(string baseDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounces only tor.exe/lyrebird.exe; mihomo and the system proxy stay up (mihomo fails closed while its
    /// upstream SOCKS is gone and reconnects once Tor is back). Used by the watchdog's L2/L3 rungs. The caller
    /// is responsible for waiting for bootstrap before relying on the new Tor instance.
    /// </summary>
    Task<string> RestartTorOnlyAsync(string baseDirectory, CancellationToken cancellationToken = default);
}
