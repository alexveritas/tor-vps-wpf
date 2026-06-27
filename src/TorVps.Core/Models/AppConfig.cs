namespace TorVps.Core.Models;

/// <summary>Unified effective configuration: torrc_manager.cfg paths/tor section + values parsed out of mihomo.yaml.</summary>
public sealed record AppConfig
{
    public required string BaseDirectory { get; init; }

    // Tor
    public string SocksHost { get; init; } = "127.0.0.1";
    public int SocksPort { get; init; } = 9150;
    public string ControlHost { get; init; } = "127.0.0.1";
    public int ControlPort { get; init; } = 9151;
    public string TorExePath { get; init; } = string.Empty;
    public string LyrebirdExePath { get; init; } = string.Empty;

    // mihomo
    public string MihomoExePath { get; init; } = string.Empty;
    public string MihomoProfileDir { get; init; } = string.Empty;
    public string MihomoYamlPath { get; init; } = string.Empty;
    public int MixedPort { get; init; } = 7890;
    public string ControllerHost { get; init; } = "127.0.0.1";
    public int ControllerPort { get; init; } = 9090;
    public string Secret { get; init; } = string.Empty;

    // Onion target parsed from the "iVPS-Tunnel" proxy entry in mihomo.yaml
    public string OnionHost { get; init; } = "—";
    public int OnionPort { get; init; } = 80;

    public int BridgeCount { get; init; }

    // Glances monitor
    public string MonitorBaseUrl { get; init; } = string.Empty;
    public string MonitorUsername { get; init; } = "monitor";
    public string MonitorPassword { get; init; } = string.Empty;
    public string MonitorNetworkInterface { get; init; } = string.Empty;
    public int MonitorIntervalSec { get; init; } = 10;
}
