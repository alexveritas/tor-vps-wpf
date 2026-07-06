namespace TorVps.Core.Models;

/// <summary>
/// Result of one <see cref="Interfaces.IBridgeAvailabilityMonitor"/> sample: the bridge records re-stated by
/// accumulated availability plus a green/yellow/red/gray breakdown.
/// </summary>
public sealed record BridgeAvailabilityReport
{
    /// <summary>The input records with <see cref="BridgeRecord.State"/> replaced by accumulated availability
    /// (Ok = always up, Warn = sometimes down, Error = never up, Unknown = not used).</summary>
    public required IReadOnlyList<BridgeRecord> Bridges { get; init; }

    /// <summary>Bridges that were always reachable when used (green).</summary>
    public required int GreenCount { get; init; }

    /// <summary>Bridges reachable only some of the time (yellow).</summary>
    public required int YellowCount { get; init; }

    /// <summary>Bridges that were used but never reachable (red).</summary>
    public required int RedCount { get; init; }

    /// <summary>Configured bridges Tor never selected/tried while healthy (gray).</summary>
    public required int GrayCount { get; init; }

    public int Total => GreenCount + YellowCount + RedCount + GrayCount;
}
