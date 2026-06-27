namespace TorVps.Core.Models;

/// <summary>One top-level status card shown on the dashboard (Tor, mihomo, Chain, ...).</summary>
public sealed record CardState
{
    public required string Title { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
    public string Text { get; init; } = string.Empty;
}
