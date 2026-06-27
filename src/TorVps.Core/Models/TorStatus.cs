namespace TorVps.Core.Models;

/// <summary>Aggregate live status of the Tor process and its ControlPort.</summary>
public sealed record TorStatus
{
    public bool Alive { get; init; }
    public bool SocksPortOpen { get; init; }
    public bool ControlPortOpen { get; init; }
    public bool ControlAuthOk { get; init; }
    public double BootstrapPercent { get; init; }
    public int BuiltCircuits { get; init; }
    public IReadOnlyList<string> ActiveGuardFingerprints { get; init; } = Array.Empty<string>();
    public ulong? TrafficReadBytes { get; init; }
    public ulong? TrafficWrittenBytes { get; init; }
    public double DownMbit { get; init; }
    public double UpMbit { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public int RestartCount { get; init; }
}
