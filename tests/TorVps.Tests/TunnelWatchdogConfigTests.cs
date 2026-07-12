using TorVps.Core.Config;
using Xunit;

namespace TorVps.Tests;

public class TunnelWatchdogConfigTests
{
    private const string Cfg =
        "[mihomo]\n" +
        "mixed_port = 7890\n" +
        "probe_interval_sec = 8\n" +
        "onion_vps_host = abc.onion\n" +
        "onion_vps_exit_ip = 45.39.12.176\n" +
        "exit_ip_probe_url = http://example.test/ip\n" +
        "vps_tunnel_group = My-Group\n" +
        "vps_tunnel_proxy = My-Tunnel\n" +
        "vps_tor_proxy = My-Tor\n" +
        "[watchdog]\n" +
        "self_heal = false\n" +
        "check_interval_sec = 20\n" +
        "check_interval_warn_sec = 7\n" +
        "check_interval_error_sec = 3\n" +
        "slow_ping_ms = 2500\n" +
        "tor_inet_warn_ms = 4000\n" +
        "tor_inet_error_ms = 20000\n" +
        "recheck_delay_sec = 4\n" +
        "newnym_wait_sec = 12\n" +
        "bootstrap_timeout_sec = 240\n" +
        "restart_settle_sec = 6\n" +
        "exit_probe_timeout_sec = 25\n" +
        "l4_fail_limit = 5\n" +
        "backoff_sec = 900\n";

    [Fact]
    public void ParseText_ReadsExitIpAndProbeUrl()
    {
        var config = TunnelWatchdogConfig.ParseText(Cfg);
        Assert.Equal("45.39.12.176", config.ExitIp);
        Assert.Equal("http://example.test/ip", config.ExitIpProbeUrl);
    }

    [Fact]
    public void ParseText_ReadsSelfHealFlag()
    {
        Assert.False(TunnelWatchdogConfig.ParseText(Cfg).SelfHealEnabled);
        Assert.True(TunnelWatchdogConfig.ParseText("[watchdog]\nself_heal = true\n").SelfHealEnabled);
        Assert.True(TunnelWatchdogConfig.ParseText("[watchdog]\nself_heal = 1\n").SelfHealEnabled);
    }

    [Fact]
    public void ParseText_ReadsEveryTimingKnob()
    {
        var config = TunnelWatchdogConfig.ParseText(Cfg);
        Assert.Equal(20, config.CheckIntervalSec);
        Assert.Equal(7, config.CheckIntervalWarnSec);
        Assert.Equal(3, config.CheckIntervalErrorSec);
        Assert.Equal(2500, config.SlowPingMs);
        Assert.Equal(4000, config.TorInetWarnMs);
        Assert.Equal(20000, config.TorInetErrorMs);
        Assert.Equal(8, config.ProbeIntervalSec);
        Assert.Equal(4, config.RecheckDelaySec);
        Assert.Equal(12, config.NewnymWaitSec);
        Assert.Equal(240, config.BootstrapTimeoutSec);
        Assert.Equal(6, config.RestartSettleSec);
        Assert.Equal(25, config.ExitProbeTimeoutSec);
        Assert.Equal(5, config.L4FailLimit);
        Assert.Equal(900, config.BackoffSec);
    }

    [Fact]
    public void ParseText_ReadsSelectorNames()
    {
        var config = TunnelWatchdogConfig.ParseText(Cfg);
        Assert.Equal("My-Group", config.VpsTunnelGroup);
        Assert.Equal("My-Tunnel", config.VpsTunnelProxy);
        Assert.Equal("My-Tor", config.VpsTorProxy);
    }

    [Fact]
    public void ParseText_AppliesDefaultsWhenMissing()
    {
        var config = TunnelWatchdogConfig.ParseText("[tor]\nsocks_port = 9150\n");
        Assert.Equal(string.Empty, config.ExitIp);
        Assert.Equal("http://api.ipify.org", config.ExitIpProbeUrl);
        Assert.True(config.SelfHealEnabled); // defaults to enabled
        Assert.Equal(15, config.CheckIntervalSec);
        Assert.Equal(5, config.CheckIntervalWarnSec);
        Assert.Equal(2, config.CheckIntervalErrorSec);
        Assert.Equal(3000, config.SlowPingMs);
        Assert.Equal(5000, config.TorInetWarnMs);
        Assert.Equal(30000, config.TorInetErrorMs);
        Assert.Equal(15, config.ProbeIntervalSec);
        Assert.Equal(3, config.RecheckDelaySec);
        Assert.Equal(10, config.NewnymWaitSec);
        Assert.Equal(180, config.BootstrapTimeoutSec);
        Assert.Equal(5, config.RestartSettleSec);
        Assert.Equal(20, config.ExitProbeTimeoutSec);
        Assert.Equal(15, config.L4FailLimit);
        Assert.Equal(300, config.BackoffSec);
        Assert.Equal("Personal-Chain", config.VpsTunnelGroup);
        Assert.Equal("iVPS-Tunnel", config.VpsTunnelProxy);
        Assert.Equal("Tor-Local", config.VpsTorProxy);
    }

    [Fact]
    public void ParseText_RejectsGarbageNumbers()
    {
        var config = TunnelWatchdogConfig.ParseText("[watchdog]\ncheck_interval_sec = potato\nnewnym_wait_sec = 0\n");
        Assert.Equal(15, config.CheckIntervalSec); // not a number → default
        Assert.Equal(10, config.NewnymWaitSec);    // below 1 → default

        // Probe interval has a 3s floor so it can't be set low enough to hammer Tor.
        Assert.Equal(15, TunnelWatchdogConfig.ParseText("[mihomo]\nprobe_interval_sec = 2\n").ProbeIntervalSec);
        Assert.Equal(6, TunnelWatchdogConfig.ParseText("[mihomo]\nprobe_interval_sec = 6\n").ProbeIntervalSec);
    }
}
