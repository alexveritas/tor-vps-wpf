using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>Polls the remote Glances API for VPS CPU/RAM/network metrics.</summary>
public interface IVpsMonitorService
{
    /// <summary>
    /// Fetches CPU/RAM/network metrics described by config's Monitor* settings. Never throws for
    /// network/auth failures; those are reported via ServerMetrics.Ok=false and ServerMetrics.Error.
    /// </summary>
    Task<ServerMetrics> FetchAsync(AppConfig config, CancellationToken cancellationToken = default);
}
