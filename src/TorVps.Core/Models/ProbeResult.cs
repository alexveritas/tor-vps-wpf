namespace TorVps.Core.Models;

/// <summary>Outcome of a single SOCKS5 reachability probe (e.g. onion, chain, clearnet target).</summary>
public sealed record ProbeResult
{
    public required bool Success { get; init; }
    public double ElapsedMs { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
}
