namespace TorVps.Core.Models;

/// <summary>One small health indicator (Boot, Ctrl, SOCKS, Circuits, Bridges, Watchdog, ...).</summary>
public sealed record HealthChip
{
    public required string Label { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
    public string Text { get; init; } = string.Empty;
}
