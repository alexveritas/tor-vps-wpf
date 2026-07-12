namespace TorVps.Core.Models;

/// <summary>Cumulative availability counters of one bridge (samples taken only while Tor is healthy).</summary>
public sealed record BridgeStat(long Up, long Down);
