using TorVps.Core.Models;

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
        var (facebookState, facebookText) = Pcard(_facebookProbe);
        var (onionState, onionText) = Pcard(_onionProbe);

        ReplaceAll(Cards, new[]
        {
            new CardState { Title = "Tor", State = torState, Text = torText },
            new CardState { Title = "mihomo", State = mihomoState, Text = mihomoText },
            new CardState { Title = "Chain", State = chainState, Text = chainText },
            new CardState { Title = "Tor → i-net", State = facebookState, Text = facebookText },
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
                State = bridgeReport.RedCount > 0 ? HealthState.Error
                    : bridgeReport.YellowCount > 0 ? HealthState.Warn
                    : bridgeReport.GreenCount > 0 ? HealthState.Ok
                    : config.BridgeCount > 0 ? HealthState.Warn : HealthState.Error,
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
                State = onionMs > 2500 || (mihomoAlive && chainMs > 2500) ? HealthState.Warn : HealthState.Ok,
                Text = ping > 2500 ? "slow" : "OK",
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
