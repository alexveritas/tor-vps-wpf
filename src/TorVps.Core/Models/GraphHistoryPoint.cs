namespace TorVps.Core.Models;

/// <summary>One sample in the Tor bandwidth/ping rolling history graph.</summary>
public sealed record GraphHistoryPoint
{
    public DateTimeOffset Timestamp { get; init; }
    public double Down { get; init; }
    public double Up { get; init; }
    public double Ping { get; init; }
    public bool Valid { get; init; }
}
