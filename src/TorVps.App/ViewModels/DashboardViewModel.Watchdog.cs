using System.IO;
using TorVps.Core.Config;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;
using TorVps.Core.Services;

namespace TorVps.App.ViewModels;

public partial class DashboardViewModel
{
    private bool _watchdogRunning;
    private DateTimeOffset _lastWatchdogTickAt = DateTimeOffset.MinValue;
    private ExitIpResult? _exitIpResult; // latest exit-IP outcome; the Onion-VPS card reads it for color
    private bool _lastExitCheckOk = true; // so recoveries are logged once instead of every OK check

    /// <summary>
    /// Runs the tunnel exit-IP check (and the self-heal ladder on failure) while the chain is up and the
    /// VPS-tunnel toggle is ON. All timings come from torrc_manager.cfg [watchdog].
    /// </summary>
    private void MaybeRunWatchdog(TunnelWatchdogConfig watchdogConfig, AppConfig config, bool torAlive, bool mihomoAlive)
    {
        if (!torAlive || !mihomoAlive || !VpsTunnelOn || Busy || _watchdogRunning)
            return;
        if (string.IsNullOrWhiteSpace(watchdogConfig.ExitIp))
            return; // exit-IP check not configured (onion_vps_exit_ip missing)
        if (DateTimeOffset.UtcNow - _lastWatchdogTickAt < NextWatchdogInterval(watchdogConfig))
            return;

        _watchdogRunning = true;
        _lastWatchdogTickAt = DateTimeOffset.UtcNow;
        _ = RunWatchdogAsync(config, watchdogConfig);
    }

    /// <summary>Check cadence adapts to the card state: green → check_interval_sec, yellow (slow) →
    /// check_interval_warn_sec, red → check_interval_error_sec. During L5 backoff the normal cadence applies
    /// (escalation is paused, so there is no point hammering the probe either).</summary>
    private TimeSpan NextWatchdogInterval(TunnelWatchdogConfig watchdogConfig)
    {
        if (_exitIpResult is null || _tunnelWatchdog.InBackoff(DateTimeOffset.UtcNow))
            return TimeSpan.FromSeconds(watchdogConfig.CheckIntervalSec);
        if (!_exitIpResult.Success)
            return TimeSpan.FromSeconds(watchdogConfig.CheckIntervalErrorSec);

        var rttMs = _onionProbe?.Success == true ? _onionProbe.ElapsedMs : _exitIpResult.ElapsedMs;
        return rttMs > watchdogConfig.SlowPingMs
            ? TimeSpan.FromSeconds(watchdogConfig.CheckIntervalWarnSec)
            : TimeSpan.FromSeconds(watchdogConfig.CheckIntervalSec);
    }

    private async Task RunWatchdogAsync(AppConfig config, TunnelWatchdogConfig watchdogConfig)
    {
        try
        {
            var actions = new TunnelActions(this, config, watchdogConfig);
            if (watchdogConfig.SelfHealEnabled)
            {
                var settings = new WatchdogSettings
                {
                    RecheckDelay = TimeSpan.FromSeconds(watchdogConfig.RecheckDelaySec),
                    NewnymWait = TimeSpan.FromSeconds(watchdogConfig.NewnymWaitSec),
                    Backoff = TimeSpan.FromSeconds(watchdogConfig.BackoffSec),
                    ChainFailLimit = watchdogConfig.L4FailLimit,
                };
                await _tunnelWatchdog.RunCycleAsync(actions, settings, DateTimeOffset.UtcNow).ConfigureAwait(false);
            }
            else
            {
                await actions.CheckExitAsync(CancellationToken.None).ConfigureAwait(false); // detect + log only
            }
        }
        catch (Exception ex)
        {
            _logService.Append(BaseDirectory, LogLevel.Error, $"WATCHDOG failed: {ex.Message}");
        }
        finally
        {
            _watchdogRunning = false;
        }
    }

    /// <summary>Binds the <see cref="TunnelWatchdog"/> ladder to the real probe/control/services for one episode.</summary>
    private sealed class TunnelActions : ITunnelActions
    {
        private static readonly TimeSpan BootstrapPollInterval = TimeSpan.FromSeconds(2);

        private readonly DashboardViewModel _vm;
        private readonly AppConfig _config;
        private readonly TunnelWatchdogConfig _watchdogConfig;
        private readonly string _cookiePath;

        public TunnelActions(DashboardViewModel vm, AppConfig config, TunnelWatchdogConfig watchdogConfig)
        {
            _vm = vm;
            _config = config;
            _watchdogConfig = watchdogConfig;
            _cookiePath = Path.Combine(DashboardViewModel.BaseDirectory, "data", "control_auth_cookie");
        }

        public async Task<bool> CheckExitAsync(CancellationToken cancellationToken)
        {
            var result = await _vm._exitIpProbe
                .CheckAsync($"http://127.0.0.1:{_config.MixedPort}", _watchdogConfig.ExitIpProbeUrl, _watchdogConfig.ExitIp, _watchdogConfig.ExitProbeTimeoutSec * 1000, cancellationToken)
                .ConfigureAwait(false);
            _vm._exitIpResult = result; // plain field; the next UI refresh reads it for the Onion-VPS color

            if (!result.Success)
            {
                Log(LogLevel.Warn, result.Reachable
                    ? $"WATCHDOG exit check FAIL: exit={result.ObservedIp} expected={_watchdogConfig.ExitIp} ({result.ElapsedMs:0} ms)"
                    : $"WATCHDOG exit check FAIL: {result.Message} ({result.ElapsedMs:0} ms, exit_probe_timeout_sec={_watchdogConfig.ExitProbeTimeoutSec})");
            }
            else if (!_vm._lastExitCheckOk)
            {
                Log(LogLevel.Info, $"WATCHDOG exit check OK: {result.ObservedIp} ({result.ElapsedMs:0} ms)");
            }

            _vm._lastExitCheckOk = result.Success;
            return result.Success;
        }

        public Task NewnymAsync(CancellationToken cancellationToken) =>
            _vm._torControlClient.SendNewnymAsync(_config.ControlHost, _config.ControlPort, _cookiePath, cancellationToken);

        public async Task RestartTorAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _vm._torService.RestartTorOnlyAsync(DashboardViewModel.BaseDirectory, cancellationToken).ConfigureAwait(false);
                await WaitForBootstrapAsync(cancellationToken).ConfigureAwait(false);
                await SettleAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log(LogLevel.Warn, $"SELF-HEAL Tor restart error: {ex.Message}");
            }
        }

        public async Task RestartChainAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _vm._torService.StopAsync(DashboardViewModel.BaseDirectory, cancellationToken).ConfigureAwait(false); // mihomo first, then Tor
                await _vm._torService.StartAsync(DashboardViewModel.BaseDirectory, cancellationToken).ConfigureAwait(false);
                await WaitForBootstrapAsync(cancellationToken).ConfigureAwait(false);
                await _vm._mihomoService.StartAsync(DashboardViewModel.BaseDirectory, cancellationToken).ConfigureAwait(false);
                await SettleAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log(LogLevel.Warn, $"SELF-HEAL chain restart error: {ex.Message}");
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

        public void Log(LogLevel level, string message) => _vm._logService.Append(DashboardViewModel.BaseDirectory, level, message);

        private Task SettleAsync(CancellationToken cancellationToken) =>
            Task.Delay(TimeSpan.FromSeconds(_watchdogConfig.RestartSettleSec), cancellationToken);

        private async Task WaitForBootstrapAsync(CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(_watchdogConfig.BootstrapTimeoutSec);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var status = await _vm._torControlClient
                    .GetStatusAsync(_config.ControlHost, _config.ControlPort, _cookiePath, cancellationToken)
                    .ConfigureAwait(false);
                if (status.BootstrapPercent >= 100.0)
                    return;
                await Task.Delay(BootstrapPollInterval, cancellationToken).ConfigureAwait(false);
            }
            Log(LogLevel.Warn, $"SELF-HEAL bootstrap did not reach 100% within {_watchdogConfig.BootstrapTimeoutSec}s (bootstrap_timeout_sec)");
        }
    }
}
