namespace TorVps.Core.Models;

/// <summary>VPS metrics fetched from the remote Glances API.</summary>
public sealed record ServerMetrics
{
    public bool Ok { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
    public string Error { get; init; } = string.Empty;
    public double? Down { get; init; }
    public double? Up { get; init; }
    public double? Cpu { get; init; }
    public double? Mem { get; init; }
    public string Iface { get; init; } = string.Empty;
}
