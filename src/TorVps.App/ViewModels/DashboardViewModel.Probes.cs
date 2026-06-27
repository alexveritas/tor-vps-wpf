using TorVps.Core.Models;

namespace TorVps.App.ViewModels;

public partial class DashboardViewModel
{
    private void MaybeStartProbes(AppConfig config, bool mihomoAlive)
    {
        if (_probeRunning)
            return;
        if (DateTimeOffset.UtcNow - _lastProbeStart < ProbeInterval)
            return;

        _probeRunning = true;
        _lastProbeStart = DateTimeOffset.UtcNow;
        _ = RunProbesAsync(config, mihomoAlive);
    }

    private async Task RunProbesAsync(AppConfig config, bool mihomoAlive)
    {
        try
        {
            var facebookTask = _socksProbe.ProbeAsync(config.SocksHost, config.SocksPort, "www.facebook.com", 443, 9000);

            var onionTask = config.OnionHost != "—" && !string.IsNullOrWhiteSpace(config.OnionHost)
                ? _socksProbe.ProbeAsync(config.SocksHost, config.SocksPort, config.OnionHost, config.OnionPort, 12000)
                : Task.FromResult(new ProbeResult { Success = false, Message = "missing onion", CheckedAt = DateTimeOffset.UtcNow });

            var chainTask = mihomoAlive
                ? _socksProbe.ProbeAsync("127.0.0.1", config.MixedPort, "cloudflare.com", 443, 12000)
                : Task.FromResult(new ProbeResult { Success = false, Message = "mihomo disabled", CheckedAt = DateTimeOffset.UtcNow });

            await Task.WhenAll(facebookTask, onionTask, chainTask);

            _facebookProbe = facebookTask.Result;
            _onionProbe = onionTask.Result;
            _chainProbe = chainTask.Result;

            LogProbeResult("CHAIN", _chainProbe);
            LogProbeResult("ONION", _onionProbe);
            LogProbeResult("TOR_FACEBOOK", _facebookProbe);
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
