using System.Text.RegularExpressions;
using TorVps.Core.Models;

namespace TorVps.Core.Config;

/// <summary>
/// Parses torrc_manager.cfg (INI), mihomo.yaml (regex-based, no full YAML parser needed for the
/// handful of keys used here) and bridges.txt into the app's data models.
/// </summary>
public static class ConfigParser
{
    private static readonly Regex ConfigLoadedPattern = new(@"CONFIG loaded\. onion=([^ :]+\.onion):?(\d+)? mixed=(\d+) bridges=(\d+)");
    private static readonly Regex FailedGuardPattern = new(@"with\s+([0-9.\[\]a-fA-F:]+:\d+).*RSA_ID=([0-9A-Fa-f]{16,40})");

    /// <summary>Reads the first value of key under [section] in an INI-style text, or defaultValue if absent.</summary>
    public static string FirstIniValue(string text, string section, string key, string defaultValue)
    {
        var sectionPattern = $@"\[\s*{Regex.Escape(section)}\s*\](.*?)(?:\n\s*\[|\z)";
        var sectionMatch = Regex.Match(text, sectionPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!sectionMatch.Success)
            return defaultValue;

        var keyPattern = $@"^\s*{Regex.Escape(key)}\s*=\s*(.*?)\s*$";
        var keyMatch = Regex.Match(sectionMatch.Groups[1].Value, keyPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return keyMatch.Success ? keyMatch.Groups[1].Value.Trim() : defaultValue;
    }

    /// <summary>Extracts host/port from mihomo.yaml's external-controller value. Defaults to 127.0.0.1:9090.</summary>
    public static (string Host, int Port) ParseController(string yaml)
    {
        var match = Regex.Match(yaml, @"external-controller:\s*['""]?([^'""\s]+)['""]?");
        var value = match.Success ? match.Groups[1].Value : "127.0.0.1:9090";

        if (value.StartsWith("http://", StringComparison.Ordinal))
            value = value["http://".Length..];
        else if (value.StartsWith("https://", StringComparison.Ordinal))
            value = value["https://".Length..];

        var lastColon = value.LastIndexOf(':');
        if (lastColon < 0)
            return ("127.0.0.1", 9090);

        var port = int.TryParse(value[(lastColon + 1)..], out var parsedPort) ? parsedPort : 9090;
        return (value[..lastColon], port);
    }

    /// <summary>Extracts the onion host/port from the "iVPS-Tunnel" proxy entry in mihomo.yaml.</summary>
    public static (string Host, int Port) ParseOnion(string yaml)
    {
        const RegexOptions scoped = RegexOptions.IgnoreCase | RegexOptions.Singleline;

        var hostMatch = Regex.Match(yaml, @"-\s*name:\s*iVPS-Tunnel.*?server:\s*([^\s]+\.onion)", scoped);
        if (!hostMatch.Success)
            hostMatch = Regex.Match(yaml, @"server:\s*([^\s]+\.onion)");
        var host = hostMatch.Success ? hostMatch.Groups[1].Value : "—";

        var portMatch = Regex.Match(yaml, @"-\s*name:\s*iVPS-Tunnel.*?port:\s*(\d+)", scoped);
        var port = portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var parsedPort) ? parsedPort : 80;

        return (host, port);
    }

    /// <summary>Parses one bridges.txt line into (address, fingerprint). Returns null for blank/comment lines.</summary>
    public static (string Address, string Fingerprint)? ParseBridgeLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            return null;

        var content = line.StartsWith("bridge ", StringComparison.OrdinalIgnoreCase) ? line[7..].Trim() : line;
        var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 3 && IsKnownTransport(parts[0]))
            return (parts[1], parts[2].ToUpperInvariant());

        if (parts.Length >= 2)
            return (parts[0], parts[1].ToUpperInvariant());

        return (Truncate(content, 30), string.Empty);
    }

    /// <summary>Matches bridges.txt entries against the active/failed guard lists reported by Tor.</summary>
    public static IReadOnlyList<BridgeRecord> ParseBridges(string bridgesText, IReadOnlyList<string> activeFingerprints, IReadOnlyList<string> failedAddressesOrFingerprints)
    {
        var active = new HashSet<string>(activeFingerprints);
        var activeShort = new HashSet<string>(activeFingerprints.Select(fp => Truncate(fp, 16)));
        var failed = new HashSet<string>(failedAddressesOrFingerprints);

        var records = new List<BridgeRecord>();
        foreach (var raw in bridgesText.Split('\n'))
        {
            if (ParseBridgeLine(raw.TrimEnd('\r')) is not (string address, string fingerprint))
                continue;

            var fingerprintShort = Truncate(fingerprint, 16);
            var state = active.Contains(fingerprint) || activeShort.Contains(fingerprintShort)
                ? HealthState.Ok
                : failed.Contains(address) || failed.Contains(fingerprint) || failed.Contains(fingerprintShort)
                    ? HealthState.Error
                    : HealthState.Unknown;

            var fingerprintShown = Truncate(fingerprint, 22);
            records.Add(new BridgeRecord
            {
                Index = records.Count + 1,
                Text = fingerprintShown.Length == 0 ? address : $"{address} {fingerprintShown}",
                State = state,
            });
        }
        return records;
    }

    /// <summary>Scans recent log lines for "unable to connect"/"failure" entries and extracts the failed guard's address/fingerprint.</summary>
    public static IReadOnlyList<string> ParseFailedFromLogs(IReadOnlyList<string> lines)
    {
        var output = new List<string>();
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (!lower.Contains("unable to connect") && !lower.Contains("failure"))
                continue;

            var match = FailedGuardPattern.Match(line);
            if (!match.Success)
                continue;

            output.Add(match.Groups[1].Value);
            var fingerprint = match.Groups[2].Value.ToUpperInvariant();
            output.Add(fingerprint);
            output.Add(Truncate(fingerprint, 16));
        }
        return output;
    }

    /// <summary>Reads torrc_manager.cfg/mihomo.yaml/bridges.txt under baseDirectory and parses the unified AppConfig.</summary>
    public static AppConfig ParseAppConfig(string baseDirectory, IReadOnlyList<string>? recentLogLines = null)
    {
        var torrcCfgText = ReadFileOrEmpty(Path.Combine(baseDirectory, "torrc_manager.cfg"));
        var yamlText = ReadFileOrEmpty(Path.Combine(baseDirectory, "mihomo.yaml"));
        var bridgeCount = CountBridgeLines(ReadFileOrEmpty(Path.Combine(baseDirectory, "bridges.txt")));

        return ParseAppConfig(baseDirectory, torrcCfgText, yamlText, bridgeCount, recentLogLines);
    }

    /// <summary>Pure variant of <see cref="ParseAppConfig(string,IReadOnlyList{string}?)"/> for already-loaded file contents.</summary>
    public static AppConfig ParseAppConfig(string baseDirectory, string torrcManagerCfgText, string mihomoYamlText, int bridgeCount, IReadOnlyList<string>? recentLogLines = null)
    {
        var socksHost = FirstIniValue(torrcManagerCfgText, "tor", "socks_host", "127.0.0.1");
        var socksPort = ParseIntOrDefault(FirstIniValue(torrcManagerCfgText, "tor", "socks_port", "9150"), 9150);
        var controlHost = FirstIniValue(torrcManagerCfgText, "tor", "control_host", "127.0.0.1");
        var controlPort = ParseIntOrDefault(FirstIniValue(torrcManagerCfgText, "tor", "control_port", "9151"), 9151);

        var mixedPortMatch = Regex.Match(mihomoYamlText, @"mixed-port:\s*(\d+)");
        var mixedPort = mixedPortMatch.Success ? ParseIntOrDefault(mixedPortMatch.Groups[1].Value, 7890) : 7890;

        var (onionHost, onionPort) = ParseOnion(mihomoYamlText);

        if (recentLogLines is not null)
        {
            for (var i = recentLogLines.Count - 1; i >= 0; i--)
            {
                var match = ConfigLoadedPattern.Match(recentLogLines[i]);
                if (!match.Success)
                    continue;

                onionHost = match.Groups[1].Value;
                if (match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out var loggedOnionPort))
                    onionPort = loggedOnionPort;
                if (int.TryParse(match.Groups[3].Value, out var loggedMixedPort))
                    mixedPort = loggedMixedPort;
                if (int.TryParse(match.Groups[4].Value, out var loggedBridgeCount))
                    bridgeCount = loggedBridgeCount;
                break;
            }
        }

        var (controllerHost, controllerPort) = ParseController(mihomoYamlText);
        var secretMatch = Regex.Match(mihomoYamlText, @"^\s*secret:\s*['""]?([^'""\r\n]*)['""]?", RegexOptions.Multiline);
        var secret = secretMatch.Success ? secretMatch.Groups[1].Value : string.Empty;

        return new AppConfig
        {
            BaseDirectory = baseDirectory,
            SocksHost = socksHost,
            SocksPort = socksPort,
            ControlHost = controlHost,
            ControlPort = controlPort,
            TorExePath = FirstIniValue(torrcManagerCfgText, "paths", "tor_exe", Path.Combine(baseDirectory, "tor.exe")),
            LyrebirdExePath = FirstIniValue(torrcManagerCfgText, "paths", "lyrebird_exe", Path.Combine(baseDirectory, "pluggable_transports", "lyrebird.exe")),
            MihomoExePath = FirstIniValue(torrcManagerCfgText, "paths", "mihomo_exe", Path.Combine(baseDirectory, "mihomo.exe")),
            MihomoProfileDir = FirstIniValue(torrcManagerCfgText, "paths", "mihomo_profile_dir", Path.Combine(baseDirectory, "mihomo-profile")),
            MihomoYamlPath = FirstIniValue(torrcManagerCfgText, "paths", "mihomo_yaml", Path.Combine(baseDirectory, "mihomo.yaml")),
            MixedPort = mixedPort,
            ControllerHost = controllerHost,
            ControllerPort = controllerPort,
            Secret = secret,
            OnionHost = onionHost,
            OnionPort = onionPort,
            BridgeCount = bridgeCount,
            MonitorBaseUrl = EnvOrIni(["TOR_CHAIN_MONITOR_URL", "GLANCES_MONITOR_URL"], torrcManagerCfgText, "monitor", "base_url", "https://ingameconnection.com/x9-secret-monitor-7kq").TrimEnd('/'),
            MonitorUsername = EnvOrIni(["TOR_CHAIN_MONITOR_USER", "GLANCES_MONITOR_USER"], torrcManagerCfgText, "monitor", "username", "monitor"),
            MonitorPassword = EnvOrIni(["TOR_CHAIN_MONITOR_PASSWORD", "GLANCES_MONITOR_PASSWORD"], torrcManagerCfgText, "monitor", "password", ""),
            MonitorNetworkInterface = EnvOrIni(["TOR_CHAIN_MONITOR_INTERFACE", "GLANCES_MONITOR_INTERFACE"], torrcManagerCfgText, "monitor", "network_interface", ""),
            MonitorIntervalSec = Math.Max(5, ParseIntOrDefault(EnvOrIni(["TOR_CHAIN_MONITOR_INTERVAL"], torrcManagerCfgText, "monitor", "interval_sec", "10"), 10)),
        };
    }

    /// <summary>Reads torrc_manager.cfg under baseDirectory and parses the [paths]/[tor] settings needed to render a torrc file.</summary>
    public static TorrcConfig ParseTorrcConfig(string baseDirectory)
        => ParseTorrcConfig(baseDirectory, ReadFileOrEmpty(Path.Combine(baseDirectory, "torrc_manager.cfg")));

    /// <summary>Pure variant of <see cref="ParseTorrcConfig(string)"/> for an already-loaded torrc_manager.cfg.</summary>
    public static TorrcConfig ParseTorrcConfig(string baseDirectory, string torrcManagerCfgText) => new()
    {
        DataDirectory = FirstIniValue(torrcManagerCfgText, "paths", "data_dir", Path.Combine(baseDirectory, "data")),
        GeoIpFile = FirstIniValue(torrcManagerCfgText, "paths", "geoip_file", Path.Combine(baseDirectory, "data", "geoip")),
        GeoIpv6File = FirstIniValue(torrcManagerCfgText, "paths", "geoipv6_file", Path.Combine(baseDirectory, "data", "geoip6")),
        LyrebirdExePath = FirstIniValue(torrcManagerCfgText, "paths", "lyrebird_exe", Path.Combine(baseDirectory, "pluggable_transports", "lyrebird.exe")),
        OutputPath = FirstIniValue(torrcManagerCfgText, "paths", "torrc_generated", Path.Combine(baseDirectory, "torrc.generated")),
        SocksHost = FirstIniValue(torrcManagerCfgText, "tor", "socks_host", "127.0.0.1"),
        SocksPort = ParseIntOrDefault(FirstIniValue(torrcManagerCfgText, "tor", "socks_port", "9150"), 9150),
        ControlHost = FirstIniValue(torrcManagerCfgText, "tor", "control_host", "127.0.0.1"),
        ControlPort = ParseIntOrDefault(FirstIniValue(torrcManagerCfgText, "tor", "control_port", "9151"), 9151),
        LogSeverity = FirstIniValue(torrcManagerCfgText, "tor", "log_severity", "notice"),
        CircuitBuildTimeout = ParseIntOrDefault(FirstIniValue(torrcManagerCfgText, "tor", "circuit_build_timeout", "60"), 60),
        MaxClientCircuitsPending = ParseIntOrDefault(FirstIniValue(torrcManagerCfgText, "tor", "max_client_circuits_pending", "32"), 32),
    };

    private static bool IsKnownTransport(string name) =>
        name.Equals("obfs4", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("snowflake", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("meek", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("webtunnel", StringComparison.OrdinalIgnoreCase);

    private static string EnvOrIni(IEnumerable<string> envNames, string iniText, string section, string key, string defaultValue)
    {
        foreach (var name in envNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return FirstIniValue(iniText, section, key, defaultValue).Trim();
    }

    private static int CountBridgeLines(string bridgesText) =>
        bridgesText.Split('\n').Count(line =>
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && !trimmed.StartsWith('#');
        });

    private static string ReadFileOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static int ParseIntOrDefault(string text, int defaultValue) => int.TryParse(text, out var value) ? value : defaultValue;

    private static string Truncate(string value, int maxChars) => value.Length <= maxChars ? value : value[..maxChars];
}
