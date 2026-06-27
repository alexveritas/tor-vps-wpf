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
}
