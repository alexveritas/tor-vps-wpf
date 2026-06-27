using TorVps.Core.Config;
using TorVps.Core.Models;
using Xunit;

namespace TorVps.Tests;

public class ConfigParserTests
{
    [Fact]
    public void FirstIniValue_ReturnsValueWhenSectionAndKeyExist()
    {
        const string text = "[tor]\nsocks_port = 9150\ncontrol_port=9151\n";
        Assert.Equal("9150", ConfigParser.FirstIniValue(text, "tor", "socks_port", "default"));
    }

    [Fact]
    public void FirstIniValue_IsCaseInsensitiveForSectionAndKey()
    {
        const string text = "[TOR]\nSocks_Port = 9150\n";
        Assert.Equal("9150", ConfigParser.FirstIniValue(text, "tor", "socks_port", "default"));
    }

    [Fact]
    public void FirstIniValue_ReturnsDefaultWhenKeyMissing()
    {
        const string text = "[tor]\nsocks_port = 9150\n";
        Assert.Equal("default", ConfigParser.FirstIniValue(text, "tor", "control_port", "default"));
    }

    [Fact]
    public void FirstIniValue_ReturnsDefaultWhenSectionMissing()
    {
        const string text = "[mihomo]\nport = 1\n";
        Assert.Equal("default", ConfigParser.FirstIniValue(text, "tor", "socks_port", "default"));
    }

    [Fact]
    public void FirstIniValue_DoesNotBleedIntoNextSection()
    {
        const string text = "[tor]\nsocks_port = 9150\n[paths]\nsocks_port = 9999\n";
        Assert.Equal("9150", ConfigParser.FirstIniValue(text, "tor", "socks_port", "default"));
    }

    [Theory]
    [InlineData("external-controller: '127.0.0.1:9090'", "127.0.0.1", 9090)]
    [InlineData("external-controller: http://127.0.0.1:9999", "127.0.0.1", 9999)]
    [InlineData("external-controller: \"0.0.0.0:9090\"", "0.0.0.0", 9090)]
    [InlineData("no controller here", "127.0.0.1", 9090)]
    public void ParseController_ExtractsHostAndPort(string yaml, string expectedHost, int expectedPort)
    {
        var (host, port) = ConfigParser.ParseController(yaml);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Fact]
    public void ParseOnion_PrefersTheNamedIVpsTunnelEntry()
    {
        const string yaml = "proxies:\n" +
            "  - name: other\n    server: wrong.onion\n    port: 1234\n" +
            "  - name: iVPS-Tunnel\n    server: correct.onion\n    port: 8080\n";

        var (host, port) = ConfigParser.ParseOnion(yaml);
        Assert.Equal("correct.onion", host);
        Assert.Equal(8080, port);
    }

    [Fact]
    public void ParseOnion_FallsBackToAnyOnionHostWhenNamedEntryMissing()
    {
        var (host, port) = ConfigParser.ParseOnion("server: fallback.onion\n");
        Assert.Equal("fallback.onion", host);
        Assert.Equal(80, port);
    }

    [Fact]
    public void ParseOnion_ReturnsDefaultsWhenNoOnionPresent()
    {
        var (host, port) = ConfigParser.ParseOnion("proxies: []\n");
        Assert.Equal("—", host);
        Assert.Equal(80, port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    public void ParseBridgeLine_ReturnsNullForBlankOrCommentLines(string line)
    {
        Assert.Null(ConfigParser.ParseBridgeLine(line));
    }

    [Fact]
    public void ParseBridgeLine_ParsesKnownTransportLine()
    {
        var result = ConfigParser.ParseBridgeLine("obfs4 1.2.3.4:443 ABCDEF0123456789 cert=xyz");
        Assert.Equal(("1.2.3.4:443", "ABCDEF0123456789"), result);
    }

    [Fact]
    public void ParseBridgeLine_ParsesPlainAddressFingerprintLine()
    {
        var result = ConfigParser.ParseBridgeLine("1.2.3.4:443 abcdef0123456789");
        Assert.Equal(("1.2.3.4:443", "ABCDEF0123456789"), result);
    }

    [Fact]
    public void ParseBridgeLine_StripsLeadingBridgeKeyword()
    {
        var result = ConfigParser.ParseBridgeLine("bridge obfs4 1.2.3.4:443 ABCDEF0123456789");
        Assert.Equal(("1.2.3.4:443", "ABCDEF0123456789"), result);
    }

    [Fact]
    public void ParseBridges_MarksActiveGuardAsOk()
    {
        var bridges = ConfigParser.ParseBridges(
            "1.2.3.4:443 ABCDEF0123456789\n",
            activeFingerprints: ["ABCDEF0123456789"],
            failedAddressesOrFingerprints: []);

        Assert.Single(bridges);
        Assert.Equal(HealthState.Ok, bridges[0].State);
    }

    [Fact]
    public void ParseBridges_MarksFailedGuardAsError()
    {
        var bridges = ConfigParser.ParseBridges(
            "1.2.3.4:443 ABCDEF0123456789\n",
            activeFingerprints: [],
            failedAddressesOrFingerprints: ["ABCDEF0123456789"]);

        Assert.Single(bridges);
        Assert.Equal(HealthState.Error, bridges[0].State);
    }

    [Fact]
    public void ParseBridges_MarksUnknownGuardAsUnknown()
    {
        var bridges = ConfigParser.ParseBridges("1.2.3.4:443 ABCDEF0123456789\n", [], []);

        Assert.Single(bridges);
        Assert.Equal(HealthState.Unknown, bridges[0].State);
    }

    [Fact]
    public void ParseFailedFromLogs_ExtractsAddressAndFingerprintFromFailureLines()
    {
        var lines = new[]
        {
            "unable to connect to bridge with 1.2.3.4:443 RSA_ID=ABCDEF0123456789ABCDEF0123456789ABCDEF01: general failure",
            "this line has nothing to do with failures",
        };

        var failed = ConfigParser.ParseFailedFromLogs(lines);

        Assert.Contains("1.2.3.4:443", failed);
        Assert.Contains("ABCDEF0123456789ABCDEF0123456789ABCDEF01", failed);
    }

    [Fact]
    public void ParseFailedFromLogs_IgnoresLinesWithoutFailureMarkers()
    {
        var failed = ConfigParser.ParseFailedFromLogs(["all is well"]);
        Assert.Empty(failed);
    }

    [Fact]
    public void ParseAppConfig_PureOverload_UsesTorrcValuesAndYamlOnion()
    {
        const string torrcText = "[tor]\nsocks_port = 9150\ncontrol_port = 9151\n";
        const string yamlText = "mixed-port: 7891\n" +
            "proxies:\n  - name: iVPS-Tunnel\n    server: vps.onion\n    port: 443\n";

        var config = ConfigParser.ParseAppConfig("C:\\tor", torrcText, yamlText, bridgeCount: 3);

        Assert.Equal(9150, config.SocksPort);
        Assert.Equal(9151, config.ControlPort);
        Assert.Equal(7891, config.MixedPort);
        Assert.Equal("vps.onion", config.OnionHost);
        Assert.Equal(443, config.OnionPort);
        Assert.Equal(3, config.BridgeCount);
    }

    [Fact]
    public void ParseAppConfig_RecentConfigLoadedLogLineOverridesOnionAndBridgeCount()
    {
        const string yamlText = "proxies:\n  - name: iVPS-Tunnel\n    server: stale.onion\n    port: 80\n";
        var logLines = new[] { "CONFIG loaded. onion=fresh.onion:8080 mixed=7892 bridges=7" };

        var config = ConfigParser.ParseAppConfig("C:\\tor", string.Empty, yamlText, bridgeCount: 1, logLines);

        Assert.Equal("fresh.onion", config.OnionHost);
        Assert.Equal(8080, config.OnionPort);
        Assert.Equal(7892, config.MixedPort);
        Assert.Equal(7, config.BridgeCount);
    }

    [Fact]
    public void ParseAppConfig_EnvironmentVariableOverridesIniForMonitorPassword()
    {
        const string envName = "TOR_CHAIN_MONITOR_PASSWORD";
        var original = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, "from-env");
        try
        {
            var config = ConfigParser.ParseAppConfig("C:\\tor", "[monitor]\npassword = from-ini\n", string.Empty, bridgeCount: 0);
            Assert.Equal("from-env", config.MonitorPassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }
}
