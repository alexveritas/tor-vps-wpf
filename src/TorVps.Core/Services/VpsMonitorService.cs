using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>Polls a remote Glances API (CPU/RAM/network) over HTTP Basic auth.</summary>
/// <remarks>The endpoint is TLS 1.3-only, so this expects an HttpClient built by <see cref="ManagedTls"/>.</remarks>
public sealed class VpsMonitorService : IVpsMonitorService, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    public VpsMonitorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<ServerMetrics> FetchAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.MonitorPassword))
        {
            return new ServerMetrics { Ok = false, State = HealthState.Warn, Error = "no password" };
        }

        try
        {
            // Each call uses its own fresh TLS connection (managed TLS isn't pooled), so run them concurrently
            // to keep a poll to roughly one handshake instead of three sequential ones.
            var cpuTask = GetJsonAsync(config, "/api/4/cpu", cancellationToken);
            var memTask = GetJsonAsync(config, "/api/4/mem", cancellationToken);
            var netTask = GetJsonAsync(config, "/api/4/network", cancellationToken);
            await Task.WhenAll(cpuTask, memTask, netTask).ConfigureAwait(false);

            var cpu = PickCpuPercent(await cpuTask.ConfigureAwait(false));
            var mem = PickMemPercent(await memTask.ConfigureAwait(false));
            var (down, up, iface) = CalcServerNetSpeed(await netTask.ConfigureAwait(false), config.MonitorNetworkInterface);
            var state = CombineState(PercentState(cpu), PercentState(mem));

            return new ServerMetrics { Ok = true, State = state, Down = down, Up = up, Cpu = cpu, Mem = mem, Iface = iface };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ServerMetrics { Ok = false, State = HealthState.Error, Error = Truncate(ex.Message, 80) };
        }
    }

    private async Task<JsonElement> GetJsonAsync(AppConfig config, string path, CancellationToken cancellationToken)
    {
        // http:// scheme: TLS is performed inside the ManagedTls connect callback, not by HttpClient (Schannel).
        var url = ManagedTls.ToPlaintextScheme(config.MonitorBaseUrl.TrimEnd('/')) + path;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // A fresh connection per request: the managed-TLS (BouncyCastle) stream is not reused across
        // pooled keep-alive requests, so force close to run the TLS handshake anew each call.
        request.Headers.ConnectionClose = true;
        request.Headers.UserAgent.ParseAdd("TorVps-Dashboard/1.0");
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.MonitorUsername}:{config.MonitorPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: linkedCts.Token).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    private static double? ValueAsDouble(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetDouble(out var number) ? number : null,
        JsonValueKind.String => double.TryParse(element.GetString(), out var parsed) ? parsed : null,
        _ => null,
    };

    private static double? CounterValue(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var value))
            {
                var number = ValueAsDouble(value);
                if (number is not null)
                    return number;
            }
        }
        return null;
    }

    private static double? PickCpuPercent(JsonElement cpu)
    {
        if (cpu.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "total", "percent", "cpu", "user" })
            {
                if (cpu.TryGetProperty(key, out var value))
                {
                    var number = ValueAsDouble(value);
                    if (number is not null)
                        return number;
                }
            }
        }
        return ValueAsDouble(cpu);
    }

    private static double? PickMemPercent(JsonElement mem)
    {
        if (mem.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in new[] { "percent", "used_percent" })
        {
            if (mem.TryGetProperty(key, out var value))
            {
                var number = ValueAsDouble(value);
                if (number is not null)
                    return number;
            }
        }

        if (mem.TryGetProperty("used", out var usedElement) && mem.TryGetProperty("total", out var totalElement))
        {
            var used = ValueAsDouble(usedElement);
            var total = ValueAsDouble(totalElement);
            if (used is not null && total is > 0.0)
                return used * 100.0 / total;
        }

        return null;
    }

    private static HealthState PercentState(double? value) => value switch
    {
        null => HealthState.Unknown,
        >= 90.0 => HealthState.Error,
        >= 70.0 => HealthState.Warn,
        _ => HealthState.Ok,
    };

    private static HealthState CombineState(HealthState cpuState, HealthState memState)
    {
        if (cpuState == HealthState.Error || memState == HealthState.Error)
            return HealthState.Error;
        if (cpuState == HealthState.Warn || memState == HealthState.Warn)
            return HealthState.Warn;
        return HealthState.Ok;
    }

    private static (double? Down, double? Up, string Iface) CalcServerNetSpeed(JsonElement net, string preferredIface)
    {
        var root = net;
        if (net.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "network", "interfaces" })
            {
                if (net.TryGetProperty(key, out var nested))
                {
                    root = nested;
                    break;
                }
            }
        }

        var candidates = new List<NetCandidate>();

        void PushCandidate(string fallbackName, JsonElement obj)
        {
            var name = FirstNonEmptyString(obj, "interface_name", "interface", "name", "key") ?? fallbackName;
            if (string.IsNullOrEmpty(name))
                return;

            var recvRate = CounterValue(obj, "bytes_recv_rate_per_sec", "recv_rate_per_sec", "rx_rate_per_sec");
            var sentRate = CounterValue(obj, "bytes_sent_rate_per_sec", "sent_rate_per_sec", "tx_rate_per_sec");
            if (recvRate is null || sentRate is null)
                return;

            var recvGauge = CounterValue(obj, "bytes_recv_gauge", "bytes_recv", "cumulative_rx", "rx_bytes", "recv", "bytes_received") ?? 0.0;
            var sentGauge = CounterValue(obj, "bytes_sent_gauge", "bytes_sent", "cumulative_tx", "tx_bytes", "sent", "bytes_sent") ?? 0.0;

            candidates.Add(new NetCandidate(name, recvRate.Value, sentRate.Value, recvGauge, sentGauge));
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                    PushCandidate(property.Name, property.Value);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    PushCandidate(string.Empty, item);
            }
        }

        if (candidates.Count == 0)
            return (null, null, string.Empty);

        NetCandidate chosen;
        if (!string.IsNullOrWhiteSpace(preferredIface))
        {
            chosen = candidates.FirstOrDefault(c => c.Name == preferredIface)
                ?? candidates.FirstOrDefault(c => c.Name.Contains(preferredIface, StringComparison.OrdinalIgnoreCase))
                ?? candidates.MaxBy(c => c.RecvRate + c.SentRate)!;
        }
        else
        {
            var goodCandidates = candidates.Where(c => !IsBadInterfaceName(c.Name)).ToList();
            chosen = (goodCandidates.Count > 0 ? goodCandidates.MaxBy(c => c.RecvRate + c.SentRate + c.RecvGauge + c.SentGauge) : null)
                ?? candidates.MaxBy(c => c.RecvRate + c.SentRate)!;
        }

        var down = Math.Max(0.0, chosen.RecvRate) * 8.0 / 1_000_000.0;
        var up = Math.Max(0.0, chosen.SentRate) * 8.0 / 1_000_000.0;
        return (down, up, chosen.Name);
    }

    private static bool IsBadInterfaceName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower == "lo"
            || lower.StartsWith("docker", StringComparison.Ordinal)
            || lower.StartsWith("br-", StringComparison.Ordinal)
            || lower.StartsWith("veth", StringComparison.Ordinal)
            || lower.StartsWith("tun", StringComparison.Ordinal)
            || lower.StartsWith("wg", StringComparison.Ordinal)
            || lower.StartsWith("zt", StringComparison.Ordinal)
            || lower.StartsWith("tailscale", StringComparison.Ordinal);
    }

    private static string? FirstNonEmptyString(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }
        return null;
    }

    private static string Truncate(string value, int maxChars) => value.Length <= maxChars ? value : value[..maxChars];

    private sealed record NetCandidate(string Name, double RecvRate, double SentRate, double RecvGauge, double SentGauge);
}
