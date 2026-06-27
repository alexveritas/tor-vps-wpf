namespace TorVps.Core.Models;

/// <summary>Latest instantaneous speed/latency sample shown next to the graphs.</summary>
public sealed record CurrentMetrics
{
    public double Down { get; init; }
    public double Up { get; init; }
    public double Ping { get; init; }
}
