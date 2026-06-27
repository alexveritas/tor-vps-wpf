namespace TorVps.Core.Models;

/// <summary>One parsed line from bridges.txt, matched against active/failed Tor guards.</summary>
public sealed record BridgeRecord
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
}
