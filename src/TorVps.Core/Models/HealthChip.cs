namespace TorVps.Core.Models;

/// <summary>One small health indicator (Boot, Ctrl, SOCKS, Circuits, Bridges, Watchdog, ...).</summary>
public sealed record HealthChip
{
    public required string Label { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional per-color breakdown rendered in place of <see cref="Text"/> (e.g. bridge green/yellow/red counts).</summary>
    public IReadOnlyList<HealthSegment>? Segments { get; init; }
}

/// <summary>One colored value inside a <see cref="HealthChip"/>'s breakdown.</summary>
public sealed record HealthSegment
{
    public required string Text { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
}
