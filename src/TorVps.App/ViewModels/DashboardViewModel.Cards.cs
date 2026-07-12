using TorVps.Core.Config;
using TorVps.Core.Models;
using TorVps.Core.Services;

namespace TorVps.App.ViewModels;

public partial class DashboardViewModel
{
    private void UpdateCardsAndHealth(
        bool torAlive,
        double bootstrap,
        TorStatus controlStatus,
        bool socksOpen,
        bool ctrlPortOpen,
        bool mihomoAlive,
        BridgeAvailabilityReport bridgeReport,
        AppConfig config,
        TunnelWatchdogConfig watchdogConfig,
        IReadOnlyList<string> logLines)
    {
        var torState = torAlive && bootstrap >= 100.0 && (controlStatus.ControlAuthOk || ctrlPortOpen)
            ? HealthState.Ok
            : torAlive ? HealthState.Warn : HealthState.Error;
        var torText = torAlive
            ? (bootstrap >= 100.0 ? "online 100%" : $"bootstrap {Math.Round(bootstrap)}%")
            : "stopped";

        var mihomoState = mihomoAlive ? HealthState.Ok : HealthState.Error;
        var mihomoText = mihomoAlive ? "online" : "stopped";

        var (chainState, chainText) = mihomoAlive ? Pcard(_chainProbe) : (HealthState.Warn, "mihomo off");
        var (torInetState, torInetText) = TorInetCard(_torExitResult, _providerIp, watchdogConfig.TorInetWarnMs, watchdogConfig.TorInetErrorMs);
        var (onionState, onionText) = VpsTunnelOn
            ? OnionVpsCard(_exitIpResult, _onionProbe, watchdogConfig.SlowPingMs)
            : (HealthState.Unknown, "off"); // VPS-tunnel toggle OFF: traffic exits via plain Tor

        ReplaceAll(Cards, new[]
        {
            new CardState { Title = "Tor", State = torState, Text = torText },
            new CardState { Title = "mihomo", State = mihomoState, Text = mihomoText },
            new CardState { Title = "Chain", State = chainState, Text = chainText },
            new CardState { Title = "Tor → i-net", State = torInetState, Text = torInetText },
            new CardState { Title = "Onion VPS", State = onionState, Text = onionText },
        });

        var onionMs = _onionProbe?.Success == true ? _onionProbe.ElapsedMs : (double?)null;
        var chainMs = _chainProbe?.Success == true ? _chainProbe.ElapsedMs : (double?)null;
        var ping = onionMs ?? chainMs ?? 0.0;

        var recent80 = logLines.Skip(Math.Max(0, logLines.Count - 80)).ToList();
        var recentTorWarn = recent80.Any(l => l.Contains("[TOR]") && ContainsWarnMarker(l));
        var recentTorErr = recent80.Any(l => l.Contains("[TOR]") && ContainsErrorMarker(l));

        string uptimeText;
        if (_torService.StartedAt is { } startedAt)
        {
            var suffix = _torService.RestartCount > 0 ? $" r{_torService.RestartCount}" : string.Empty;
            uptimeText = FormatDuration(DateTimeOffset.UtcNow - startedAt) + suffix;
        }
        else
        {
            uptimeText = torAlive ? "external" : "—";
        }

        ReplaceAll(Health, new[]
        {
            new HealthChip
            {
                Label = "Boot",
                State = bootstrap >= 100.0 ? HealthState.Ok : torAlive ? HealthState.Warn : HealthState.Unknown,
                Text = torAlive ? $"{Math.Round(bootstrap)}%" : "—",
            },
            new HealthChip
            {
                Label = "Ctrl",
                State = controlStatus.ControlAuthOk ? HealthState.Ok : ctrlPortOpen ? HealthState.Warn : HealthState.Error,
                Text = controlStatus.ControlAuthOk ? "OK" : ctrlPortOpen ? "PORT" : "DOWN",
            },
            new HealthChip
            {
                Label = "SOCKS",
                State = socksOpen ? HealthState.Ok : HealthState.Error,
                Text = socksOpen ? "OK" : "DOWN",
            },
            new HealthChip
            {
                Label = "Circuits",
                State = controlStatus.BuiltCircuits >= 2 ? HealthState.Ok
                    : controlStatus.BuiltCircuits == 1 ? HealthState.Warn
                    : torAlive ? HealthState.Warn : HealthState.Unknown,
                Text = controlStatus.BuiltCircuits > 0 ? $"{controlStatus.BuiltCircuits} BUILT" : torAlive ? "n/a" : "—",
            },
            new HealthChip
            {
                Label = "Bridges",
                // Colored by how many GREEN bridges remain: ≥6 ok, 3–5 warn, 0–2 error; gray until any bridge
                // has been classified at all (fresh start / Tor still warming up).
                State = bridgeReport.GreenCount + bridgeReport.YellowCount + bridgeReport.RedCount == 0 ? HealthState.Unknown
                    : bridgeReport.GreenCount >= 6 ? HealthState.Ok
                    : bridgeReport.GreenCount >= 3 ? HealthState.Warn
                    : HealthState.Error,
                Text = $"{bridgeReport.GreenCount}/{bridgeReport.YellowCount}/{bridgeReport.RedCount}",
                Segments = new[]
                {
                    new HealthSegment { Text = bridgeReport.GreenCount.ToString(), State = HealthState.Ok },
                    new HealthSegment { Text = bridgeReport.YellowCount.ToString(), State = HealthState.Warn },
                    new HealthSegment { Text = bridgeReport.RedCount.ToString(), State = HealthState.Error },
                },
            },
            new HealthChip
            {
                Label = "Watchdog",
                State = !VpsTunnelOn ? HealthState.Unknown
                    : _tunnelWatchdog.InBackoff(DateTimeOffset.UtcNow) ? HealthState.Error
                    : _exitIpResult is { Success: false } ? HealthState.Warn
                    : onionMs > watchdogConfig.SlowPingMs || (mihomoAlive && chainMs > watchdogConfig.SlowPingMs) ? HealthState.Warn
                    : HealthState.Ok,
                Text = !VpsTunnelOn ? "off"
                    : _tunnelWatchdog.InBackoff(DateTimeOffset.UtcNow) ? "backoff"
                    : _exitIpResult is { Success: false } ? (watchdogConfig.SelfHealEnabled ? "healing" : "FAIL")
                    : ping > watchdogConfig.SlowPingMs ? "slow"
                    : watchdogConfig.SelfHealEnabled ? "OK" : "watch",
            },
            new HealthChip
            {
                Label = "Tor log",
                State = recentTorErr ? HealthState.Error : recentTorWarn ? HealthState.Warn : HealthState.Ok,
                Text = recentTorErr ? "ERR" : recentTorWarn ? "WARN" : "none",
            },
            new HealthChip
            {
                Label = "Uptime",
                State = torAlive ? HealthState.Ok : HealthState.Unknown,
                Text = uptimeText,
            },
        });
    }

    private static bool ContainsWarnMarker(string line) => line.Contains("[warn]") || line.ToLowerInvariant().Contains("warn");

    private static bool ContainsErrorMarker(string line) =>
        line.Contains("[ERROR]") || line.ToLowerInvariant().Contains("error") || line.ToLowerInvariant().Contains("err");

    private static (HealthState State, string Text) Pcard(ProbeResult? probe)
    {
        if (probe is null || !probe.Success)
            return (HealthState.Unknown, "—");
        return Lat(probe.ElapsedMs, probe.CheckedAt);
    }

    /// <summary>
    /// "Tor → i-net": we fetch our exit IP through raw Tor and want anything except the address our provider hands
    /// us directly. Only the COLOUR uses the new leak/latency logic — red on a leak (exit == ISP IP), unreachable,
    /// or a fetch slower than the error threshold; yellow over the warn threshold; green otherwise. The DISPLAY is
    /// unchanged from before: the exit round-trip (ping) and the time since that measurement, refreshed each probe
    /// cycle. (The observed IP and exact ms are logged.)
    /// </summary>
    private static (HealthState State, string Text) TorInetCard(ExitIpResult? exit, string providerIp, int warnMs, int errorMs)
    {
        if (exit is null)
            return (HealthState.Unknown, "—"); // not probed yet

        var latency = $"{Math.Round(exit.ElapsedMs)} ms · {Age(exit.CheckedAt)}";
        return ExitIpAssessment.Assess(exit.Reachable, exit.ObservedIp, providerIp, exit.ElapsedMs, warnMs, errorMs) switch
        {
            ExitIpVerdict.Down => (HealthState.Error, "down"),
            ExitIpVerdict.Leak => (HealthState.Error, "leak!"),
            ExitIpVerdict.TooSlow => (HealthState.Error, latency),
            ExitIpVerdict.Slow => (HealthState.Warn, latency),
            _ => (HealthState.Ok, latency),
        };
    }

    /// <summary>
    /// Onion-VPS color is driven by liveness (does traffic exit via our VPS?), not raw latency: red when the exit is
    /// wrong/unreachable, else green/yellow by the onion round-trip against slow_ping_ms. The shown ms is the pure
    /// tunnel RTT (a healthy Tor onion tunnel is inherently ~0.5–3s).
    /// </summary>
    private static (HealthState State, string Text) OnionVpsCard(ExitIpResult? exit, ProbeResult? onion, int slowPingMs)
    {
        if (exit is null)
            return (HealthState.Unknown, "—"); // not checked yet
        if (!exit.Success)
            return (HealthState.Error, exit.Reachable ? "wrong exit" : "down");

        var rttMs = onion?.Success == true ? onion.ElapsedMs : exit.ElapsedMs;
        var text = $"{Math.Round(rttMs)} ms · {Age(exit.CheckedAt)}";
        return (rttMs <= slowPingMs ? HealthState.Ok : HealthState.Warn, text);
    }

    private static (HealthState State, string Text) Lat(double elapsedMs, DateTimeOffset checkedAt)
    {
        var ageText = Age(checkedAt);
        if (elapsedMs < 1000.0)
            return (HealthState.Ok, $"{Math.Round(elapsedMs)} ms · {ageText}");
        if (elapsedMs <= 10000.0)
            return (HealthState.Warn, $"{Math.Round(elapsedMs)} ms · {ageText}");
        return (HealthState.Error, $"{Math.Round(elapsedMs)} ms · {ageText}");
    }

    private static string Age(DateTimeOffset checkedAt)
    {
        var seconds = Math.Max(0, (DateTimeOffset.UtcNow - checkedAt).TotalSeconds);
        return seconds < 60 ? $"{(int)seconds}s" : $"{(int)(seconds / 60)}m";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = (long)Math.Max(0, duration.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        if (hours > 0) return $"{hours}h{minutes:D2}m";
        if (minutes > 0) return $"{minutes}m{seconds:D2}s";
        return $"{seconds}s";
    }

    private void UpdateConfTabStatus()
    {
        if (Checks.Count == 0)
        {
            ConfTabState = HealthState.Unknown;
            ConfTabStatusText = "check";
            return;
        }
        if (Checks.Any(c => c.State == HealthState.Error))
        {
            ConfTabState = HealthState.Error;
            ConfTabStatusText = "error";
            return;
        }
        if (Checks.Any(c => c.State == HealthState.Warn))
        {
            ConfTabState = HealthState.Warn;
            ConfTabStatusText = "warn";
            return;
        }
        ConfTabState = HealthState.Ok;
        ConfTabStatusText = "ok";
    }
}
