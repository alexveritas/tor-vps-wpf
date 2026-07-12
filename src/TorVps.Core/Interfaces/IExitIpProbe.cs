using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>Fetches an IP-echo endpoint through a proxy to observe an exit IP.</summary>
public interface IExitIpProbe
{
    /// <param name="proxyUrl">Proxy to route through: <c>http://127.0.0.1:7890</c> (mihomo → VPS tunnel),
    /// <c>socks5://127.0.0.1:9150</c> (raw Tor) or empty for a direct request (the real ISP egress IP).</param>
    /// <param name="expectedIp">When non-empty, <see cref="ExitIpResult.Success"/> is set only if the observed IP
    /// equals it; callers that just want the observed IP can pass an empty string and read
    /// <see cref="ExitIpResult.ObservedIp"/>/<see cref="ExitIpResult.Reachable"/>.</param>
    Task<ExitIpResult> CheckAsync(string proxyUrl, string probeUrl, string expectedIp, int timeoutMs, CancellationToken cancellationToken = default);
}
