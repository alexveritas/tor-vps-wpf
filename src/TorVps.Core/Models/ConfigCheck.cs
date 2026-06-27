namespace TorVps.Core.Models;

/// <summary>One row of the startup/config diagnostics (file presence, port settings, yaml rule checks, ...).</summary>
public sealed record ConfigCheck
{
    public required string Name { get; init; }
    public HealthState State { get; init; } = HealthState.Unknown;
    public string Detail { get; init; } = string.Empty;
}
