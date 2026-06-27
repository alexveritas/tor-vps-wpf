using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>Performs a raw, no-auth SOCKS5 CONNECT to measure reachability/latency through a proxy.</summary>
public interface ISocks5Probe
{
    /// <summary>Connects to proxyHost:proxyPort, issues a SOCKS5 CONNECT to destHost:destPort, and reports the outcome.</summary>
    Task<ProbeResult> ProbeAsync(string proxyHost, int proxyPort, string destHost, int destPort, int timeoutMs, CancellationToken cancellationToken = default);
}
