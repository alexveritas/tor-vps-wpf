using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>The side-effecting operations the <see cref="Services.TunnelWatchdog"/> ladder drives. Injected so the
/// ladder logic can be unit-tested with fakes (no real network, processes, or waits).</summary>
public interface ITunnelActions
{
    /// <summary>Checks the tunnel: true when traffic exits via the expected VPS IP.</summary>
    Task<bool> CheckExitAsync(CancellationToken cancellationToken);

    /// <summary>Requests fresh Tor circuits (SIGNAL NEWNYM).</summary>
    Task NewnymAsync(CancellationToken cancellationToken);

    /// <summary>Restarts only tor.exe (mihomo keeps running) and waits for bootstrap to complete.</summary>
    Task RestartTorAsync(CancellationToken cancellationToken);

    /// <summary>Restarts the full chain (mihomo stopped first, then Tor; Tor bootstraps; mihomo started again).</summary>
    Task RestartChainAsync(CancellationToken cancellationToken);

    /// <summary>Waits the given duration (real time in production, instant in tests).</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    void Log(LogLevel level, string message);
}
