namespace TorVps.Core.Models;

/// <summary>One sample in the VPS bandwidth/CPU/RAM rolling history graph.</summary>
public sealed record VpsHistoryPoint
{
    public DateTimeOffset Timestamp { get; init; }
    public double Down { get; init; }
    public double Up { get; init; }
    public double Ping { get; init; }
    public double Cpu { get; init; }
    public double Mem { get; init; }
    public bool Valid { get; init; }
}
