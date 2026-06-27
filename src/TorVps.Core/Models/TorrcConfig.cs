namespace TorVps.Core.Models;

/// <summary>Settings needed to render a torrc file. Parsed from the [paths]/[tor] sections of torrc_manager.cfg.</summary>
public sealed record TorrcConfig
{
    public required string DataDirectory { get; init; }
    public required string GeoIpFile { get; init; }
    public required string GeoIpv6File { get; init; }
    public required string LyrebirdExePath { get; init; }
    public required string OutputPath { get; init; }
    public string SocksHost { get; init; } = "127.0.0.1";
    public int SocksPort { get; init; } = 9150;
    public string ControlHost { get; init; } = "127.0.0.1";
    public int ControlPort { get; init; } = 9151;
    public string LogSeverity { get; init; } = "notice";
    public int CircuitBuildTimeout { get; init; } = 60;
    public int MaxClientCircuitsPending { get; init; } = 32;
}
