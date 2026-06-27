namespace TorVps.Core.Models;

/// <summary>Full snapshot rendered by the dashboard UI.</summary>
public sealed record DashboardState
{
    public bool TunnelOn { get; init; }
    public bool MihomoOn { get; init; }
    public int IntervalSec { get; init; }
    public IReadOnlyList<CardState> Cards { get; init; } = Array.Empty<CardState>();
    public IReadOnlyList<HealthChip> Health { get; init; } = Array.Empty<HealthChip>();
    public IReadOnlyList<BridgeRecord> Bridges { get; init; } = Array.Empty<BridgeRecord>();
    public IReadOnlyList<ConfigCheck> Checks { get; init; } = Array.Empty<ConfigCheck>();
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();
    public ServerMetrics Server { get; init; } = new();
    public CurrentMetrics Current { get; init; } = new();
    public IReadOnlyList<GraphHistoryPoint> GraphHistory { get; init; } = Array.Empty<GraphHistoryPoint>();
    public IReadOnlyList<VpsHistoryPoint> VpsHistory { get; init; } = Array.Empty<VpsHistoryPoint>();
    public required AppConfig Config { get; init; }
}
