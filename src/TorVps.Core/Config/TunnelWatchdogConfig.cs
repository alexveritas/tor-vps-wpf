using System.IO;

namespace TorVps.Core.Config;

/// <summary>
/// Onion-VPS tunnel/watchdog settings read from torrc_manager.cfg. Every delay in the self-heal scheme is a
/// parameter here so the timings can be tuned in the config (watch tor-t.log to see which knob each step used)
/// without rebuilding the app. Kept separate from <see cref="ConfigParser"/> (which is change-locked) and
/// populated via its public <see cref="ConfigParser.FirstIniValue"/> helper.
/// </summary>
public sealed record TunnelWatchdogConfig
{
    /// <summary>Expected public IP when traffic exits through the tunnel (the VPS). Empty disables the exit-IP check.</summary>
    public string ExitIp { get; init; } = string.Empty;

    /// <summary>Plain-text IP-echo endpoint fetched through the tunnel to observe the exit IP.</summary>
    public string ExitIpProbeUrl { get; init; } = "http://api.ipify.org";

    /// <summary>When true, the watchdog may auto-heal the tunnel (NEWNYM → Tor restart ×2 → chain restart → back off).</summary>
    public bool SelfHealEnabled { get; init; } = true;

    /// <summary>Exit-IP check cadence while the tunnel is healthy (Onion-VPS card green).</summary>
    public int CheckIntervalSec { get; init; } = 15;

    /// <summary>Check cadence while the tunnel is slow (card yellow).</summary>
    public int CheckIntervalWarnSec { get; init; } = 5;

    /// <summary>Check cadence while the tunnel is down (card red).</summary>
    public int CheckIntervalErrorSec { get; init; } = 2;

    /// <summary>Tunnel RTT above this is considered slow: yellow card + the faster check cadence.</summary>
    public int SlowPingMs { get; init; } = 3000;

    /// <summary>"Tor → i-net" exit-IP fetch latency above this many ms shows the card yellow (slow).</summary>
    public int TorInetWarnMs { get; init; } = 5000;

    /// <summary>"Tor → i-net" exit-IP fetch latency above this many ms shows the card red (too slow).</summary>
    public int TorInetErrorMs { get; init; } = 30000;

    /// <summary>How often the dashboard re-probes the Tor→i-net / Chain / Onion-VPS cards (their ping + age refresh).</summary>
    public int ProbeIntervalSec { get; init; } = 15;

    /// <summary>Delay before the confirmation re-check after one failed check (filters single glitches).</summary>
    public int RecheckDelaySec { get; init; } = 3;

    /// <summary>L1: how long to wait after SIGNAL NEWNYM before re-checking.</summary>
    public int NewnymWaitSec { get; init; } = 10;

    /// <summary>L2-L4: maximum time to wait for Tor to report bootstrap 100% after a restart.</summary>
    public int BootstrapTimeoutSec { get; init; } = 180;

    /// <summary>L2-L4: settle time between "restart finished" and the verifying exit-IP check.</summary>
    public int RestartSettleSec { get; init; } = 5;

    /// <summary>HTTP timeout of a single exit-IP probe request.</summary>
    public int ExitProbeTimeoutSec { get; init; } = 20;

    /// <summary>L5: consecutive fully-failed ladders before assuming the VPS/onion is down and backing off.</summary>
    public int L4FailLimit { get; init; } = 15;

    /// <summary>L5: how long to stop escalating once the fail limit is reached (checks continue at the normal cadence).</summary>
    public int BackoffSec { get; init; } = 300;

    /// <summary>mihomo selector group that decides where routed traffic exits.</summary>
    public string VpsTunnelGroup { get; init; } = "Personal-Chain";

    /// <summary>Group member that exits via the onion VPS tunnel (toggle ON).</summary>
    public string VpsTunnelProxy { get; init; } = "iVPS-Tunnel";

    /// <summary>Group member that exits via plain Tor with a random exit (toggle OFF).</summary>
    public string VpsTorProxy { get; init; } = "Tor-Local";

    public static TunnelWatchdogConfig Parse(string baseDirectory) => ParseText(ReadCfg(baseDirectory));

    public static TunnelWatchdogConfig ParseText(string torrcManagerCfgText) => new()
    {
        ExitIp = ConfigParser.FirstIniValue(torrcManagerCfgText, "mihomo", "onion_vps_exit_ip", string.Empty),
        ExitIpProbeUrl = ConfigParser.FirstIniValue(torrcManagerCfgText, "mihomo", "exit_ip_probe_url", "http://api.ipify.org"),
        SelfHealEnabled = ParseBool(ConfigParser.FirstIniValue(torrcManagerCfgText, "watchdog", "self_heal", "true")),
        CheckIntervalSec = ParseInt(torrcManagerCfgText, "check_interval_sec", 15),
        CheckIntervalWarnSec = ParseInt(torrcManagerCfgText, "check_interval_warn_sec", 5),
        CheckIntervalErrorSec = ParseInt(torrcManagerCfgText, "check_interval_error_sec", 2),
        SlowPingMs = ParseInt(torrcManagerCfgText, "slow_ping_ms", 3000),
        TorInetWarnMs = ParseInt(torrcManagerCfgText, "tor_inet_warn_ms", 5000),
        TorInetErrorMs = ParseInt(torrcManagerCfgText, "tor_inet_error_ms", 30000),
        // Card probe cadence lives in [mihomo] (the pre-existing probe_interval_sec key), min 3s to avoid hammering.
        ProbeIntervalSec = ParseInt(torrcManagerCfgText, "mihomo", "probe_interval_sec", 15, 3),
        RecheckDelaySec = ParseInt(torrcManagerCfgText, "recheck_delay_sec", 3),
        NewnymWaitSec = ParseInt(torrcManagerCfgText, "newnym_wait_sec", 10),
        BootstrapTimeoutSec = ParseInt(torrcManagerCfgText, "bootstrap_timeout_sec", 180),
        RestartSettleSec = ParseInt(torrcManagerCfgText, "restart_settle_sec", 5),
        ExitProbeTimeoutSec = ParseInt(torrcManagerCfgText, "exit_probe_timeout_sec", 20),
        L4FailLimit = ParseInt(torrcManagerCfgText, "l4_fail_limit", 15),
        BackoffSec = ParseInt(torrcManagerCfgText, "backoff_sec", 300),
        VpsTunnelGroup = ConfigParser.FirstIniValue(torrcManagerCfgText, "mihomo", "vps_tunnel_group", "Personal-Chain"),
        VpsTunnelProxy = ConfigParser.FirstIniValue(torrcManagerCfgText, "mihomo", "vps_tunnel_proxy", "iVPS-Tunnel"),
        VpsTorProxy = ConfigParser.FirstIniValue(torrcManagerCfgText, "mihomo", "vps_tor_proxy", "Tor-Local"),
    };

    private static int ParseInt(string cfgText, string key, int defaultValue) =>
        ParseInt(cfgText, "watchdog", key, defaultValue, 1);

    private static int ParseInt(string cfgText, string section, string key, int defaultValue, int min)
    {
        var raw = ConfigParser.FirstIniValue(cfgText, section, key, defaultValue.ToString());
        return int.TryParse(raw, out var value) && value >= min ? value : defaultValue;
    }

    private static bool ParseBool(string value) =>
        value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";

    private static string ReadCfg(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "torrc_manager.cfg");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
