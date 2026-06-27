namespace TorVps.Core.Models;

/// <summary>Status of the "Chain" card: a SOCKS5 probe routed through mihomo's mixed-port.</summary>
public sealed record ChainStatus
{
    public bool MihomoEnabled { get; init; }
    public ProbeResult? Probe { get; init; }
}
