namespace TorVps.Core.Models;

/// <summary>Live status of the mihomo process and its ports.</summary>
public sealed record MihomoStatus
{
    public bool Alive { get; init; }
    public bool ControllerReachable { get; init; }
    public bool MixedPortOpen { get; init; }
}
