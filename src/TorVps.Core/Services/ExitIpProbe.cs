using System.Diagnostics;
using System.Net;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// Fetches a plain-text IP-echo endpoint (e.g. http://api.ipify.org) through a given proxy to observe the exit IP.
/// The proxy URL selects the path: <c>http://127.0.0.1:7890</c> (mihomo mixed port → the VPS tunnel),
/// <c>socks5://127.0.0.1:9150</c> (raw Tor → a random Tor exit) or an empty string (direct, no proxy → the real
/// ISP egress IP, used as the leak baseline). The request latency doubles as an end-to-end ping. Plain HTTP is used
/// on purpose so no TLS is negotiated over the (slow) tunnel. Clients are cached per proxy URL.
/// </summary>
public sealed class ExitIpProbe : IExitIpProbe, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);

    public async Task<ExitIpResult> CheckAsync(string proxyUrl, string probeUrl, string expectedIp, int timeoutMs, CancellationToken cancellationToken = default)
    {
        var client = GetClient(proxyUrl);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            request.Headers.UserAgent.ParseAdd("TorVps-Watchdog/1.0");

            using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = (await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false)).Trim();

            var matches = !string.IsNullOrWhiteSpace(expectedIp) && string.Equals(body, expectedIp.Trim(), StringComparison.Ordinal);
            return new ExitIpResult
            {
                Success = matches,
                Reachable = true,
                ObservedIp = body,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                Message = matches ? "OK" : $"exit {body}",
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ExitIpResult
            {
                Success = false,
                Reachable = false,
                ObservedIp = string.Empty,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                Message = Truncate(ex.Message, 80),
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    private HttpClient GetClient(string proxyUrl)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(proxyUrl, out var existing))
                return existing;

            var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) };
            if (string.IsNullOrWhiteSpace(proxyUrl))
            {
                // Direct: never honor the Windows system proxy (mihomo) — this must observe the real ISP egress IP.
                handler.UseProxy = false;
            }
            else
            {
                handler.Proxy = new WebProxy(new Uri(proxyUrl));
                handler.UseProxy = true;
            }

            var client = new HttpClient(handler, disposeHandler: true);
            _clients[proxyUrl] = client;
            return client;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
        }
    }

    private static string Truncate(string value, int maxChars) => value.Length <= maxChars ? value : value[..maxChars];
}
