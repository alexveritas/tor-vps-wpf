using TorVps.Core.Config;
using TorVps.Core.Models;
using TorVps.Core.Services;

namespace TorVps.App.ViewModels;

public partial class DashboardViewModel
{
    private const int ProviderIpProbeTimeoutMs = 8_000;
    private static readonly TimeSpan ProviderIpTtl = TimeSpan.FromMinutes(10);

    private void MaybeStartProbes(AppConfig config, TunnelWatchdogConfig watchdogConfig, bool mihomoAlive)
    {
        if (_probeRunning)
            return;
        if (DateTimeOffset.UtcNow - _lastProbeStart < TimeSpan.FromSeconds(watchdogConfig.ProbeIntervalSec))
            return;

        _probeRunning = true;
        _lastProbeStart = DateTimeOffset.UtcNow;
        _ = RunProbesAsync(config, watchdogConfig, mihomoAlive);
    }

    private async Task RunProbesAsync(AppConfig config, TunnelWatchdogConfig watchdogConfig, bool mihomoAlive)
    {
        try
        {
            var probeUrl = watchdogConfig.ExitIpProbeUrl;

            // Learn the real ISP egress IP (direct, no Tor/proxy) so the Tor→i-net card can flag a leak.
            await MaybeRefreshProviderIpAsync(probeUrl);

            // "Tor → i-net": fetch our exit IP through raw Tor. Allow it to run a little past the red (too-slow)
            // threshold so a crawling tunnel is measured as slow rather than turned into a timeout "down".
            var torExitTimeoutMs = watchdogConfig.TorInetErrorMs + 5_000;
            var torExitTask = _exitIpProbe.CheckAsync($"socks5://{config.SocksHost}:{config.SocksPort}", probeUrl, string.Empty, torExitTimeoutMs);

            var onionTask = config.OnionHost != "—" && !string.IsNullOrWhiteSpace(config.OnionHost)
                ? _socksProbe.ProbeAsync(config.SocksHost, config.SocksPort, config.OnionHost, config.OnionPort, 12000)
                : Task.FromResult(new ProbeResult { Success = false, Message = "missing onion", CheckedAt = DateTimeOffset.UtcNow });

            var chainTask = mihomoAlive
                ? _socksProbe.ProbeAsync("127.0.0.1", config.MixedPort, "cloudflare.com", 443, 12000)
                : Task.FromResult(new ProbeResult { Success = false, Message = "mihomo disabled", CheckedAt = DateTimeOffset.UtcNow });

            // Don't let a slow exit fetch stall the onion/chain cards — publish those as soon as they finish.
            await Task.WhenAll(onionTask, chainTask);
            _onionProbe = onionTask.Result;
            _chainProbe = chainTask.Result;
            LogProbeResult("CHAIN", _chainProbe);
            LogProbeResult("ONION", _onionProbe);

            _torExitResult = await torExitTask;
            LogTorExitResult(_torExitResult, watchdogConfig);
        }
        catch (Exception ex)
        {
            _logService.Append(BaseDirectory, LogLevel.Error, $"PROBE FAILED unexpectedly: {ex.Message}");
        }
        finally
        {
            _probeRunning = false;
        }
    }

    private async Task MaybeRefreshProviderIpAsync(string probeUrl)
    {
        if (!string.IsNullOrEmpty(_providerIp) && DateTimeOffset.UtcNow - _providerIpAt < ProviderIpTtl)
            return;

        var direct = await _exitIpProbe.CheckAsync(string.Empty, probeUrl, string.Empty, ProviderIpProbeTimeoutMs);
        if (!direct.Reachable || string.IsNullOrWhiteSpace(direct.ObservedIp))
            return;

        if (!string.Equals(_providerIp, direct.ObservedIp, StringComparison.Ordinal))
            _logService.Append(BaseDirectory, LogLevel.Info, $"PROVIDER IP (direct, no Tor) = {direct.ObservedIp}");
        _providerIp = direct.ObservedIp;
        _providerIpAt = DateTimeOffset.UtcNow;
    }

    private void LogTorExitResult(ExitIpResult result, TunnelWatchdogConfig watchdogConfig)
    {
        var verdict = ExitIpAssessment.Assess(result.Reachable, result.ObservedIp, _providerIp, result.ElapsedMs, watchdogConfig.TorInetWarnMs, watchdogConfig.TorInetErrorMs);
        switch (verdict)
        {
            case ExitIpVerdict.Leak:
                _logService.Append(BaseDirectory, LogLevel.Error, $"PROBE TOR_EXIT LEAK: exit IP {result.ObservedIp} equals the ISP IP — traffic is NOT going through Tor");
                break;
            case ExitIpVerdict.Down:
                _logService.Append(BaseDirectory, LogLevel.Warn, $"PROBE TOR_EXIT FAIL {result.ElapsedMs:F0} ms {result.Message}");
                break;
            case ExitIpVerdict.TooSlow:
                _logService.Append(BaseDirectory, LogLevel.Error, $"PROBE TOR_EXIT SLOW {result.ElapsedMs:F0} ms exit={result.ObservedIp} (over tor_inet_error_ms={watchdogConfig.TorInetErrorMs})");
                break;
            case ExitIpVerdict.Slow:
                _logService.Append(BaseDirectory, LogLevel.Warn, $"PROBE TOR_EXIT SLOW {result.ElapsedMs:F0} ms exit={result.ObservedIp} (over tor_inet_warn_ms={watchdogConfig.TorInetWarnMs})");
                break;
            default:
                _logService.Append(BaseDirectory, LogLevel.Info, $"PROBE TOR_EXIT OK {result.ElapsedMs:F0} ms exit={result.ObservedIp}");
                break;
        }
    }

    private void LogProbeResult(string label, ProbeResult result)
    {
        var status = result.Success ? "OK" : "FAIL";
        var level = result.Success ? LogLevel.Info : LogLevel.Warn;
        _logService.Append(BaseDirectory, level, $"PROBE {label} {status} {result.ElapsedMs:F0} ms {result.Message}");
    }

    private void MaybeFetchVpsMetrics(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.MonitorPassword))
        {
            Server = new ServerMetrics { Ok = false, State = HealthState.Warn, Error = "no password" };
            return;
        }

        if (_vpsFetchRunning)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(5, config.MonitorIntervalSec));
        if (DateTimeOffset.UtcNow - _lastVpsFetchAt < interval)
            return;

        _vpsFetchRunning = true;
        _lastVpsFetchAt = DateTimeOffset.UtcNow;
        _ = FetchVpsMetricsAsync(config);
    }

    private async Task FetchVpsMetricsAsync(AppConfig config)
    {
        try
        {
            var metrics = await _vpsMonitorService.FetchAsync(config);
            if (metrics.Ok)
            {
                _lastGoodServerMetrics = metrics;
                _lastGoodServerMetricsAt = DateTimeOffset.UtcNow;
            }
            Server = metrics;
        }
        catch (Exception ex)
        {
            Server = new ServerMetrics { Ok = false, State = HealthState.Error, Error = ex.Message };
        }
        finally
        {
            _vpsFetchRunning = false;
        }
    }
}
