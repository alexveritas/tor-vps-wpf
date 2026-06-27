using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>
/// Speaks the Tor Control Protocol (cookie AUTHENTICATE, GETINFO) over a fresh TCP connection per query.
/// </summary>
public interface ITorControlClient
{
    /// <summary>
    /// Authenticates with the cookie at cookieFilePath and queries bootstrap-phase, circuit-status,
    /// entry-guards and traffic counters. Only control-protocol-derived fields are populated on the
    /// returned <see cref="TorStatus"/> (ControlAuthOk, BootstrapPercent, BuiltCircuits,
    /// ActiveGuardFingerprints, TrafficReadBytes/WrittenBytes, DownMbit/UpMbit); Alive, SocksPortOpen,
    /// StartedAt and RestartCount are left at their defaults for the caller to fill in from process/port
    /// state. Returns a default (unreachable) status if the control port cannot be reached.
    /// </summary>
    Task<TorStatus> GetStatusAsync(string controlHost, int controlPort, string cookieFilePath, CancellationToken cancellationToken = default);
}
