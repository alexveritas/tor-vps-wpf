using System.IO;

namespace TorVps.Core.Config;

/// <summary>Bridge-maintenance settings read from torrc_manager.cfg ([bridges] + [tor]).</summary>
public sealed record BridgesConfig
{
    /// <summary>
    /// A bridge that was never up and has accumulated at least this many failed samples is moved from
    /// bridges.txt to bridges-red.txt. Samples are taken ~1/s while Tor is healthy, so the default is
    /// roughly 10 minutes of continuous evidence.
    /// </summary>
    public int RedMoveMinFailures { get; init; } = 600;

    /// <summary>[tor] bridges_per_run — the red-move never shrinks bridges.txt below this many active lines.</summary>
    public int BridgesPerRun { get; init; } = 8;

    public static BridgesConfig Parse(string baseDirectory) => ParseText(ReadCfg(baseDirectory));

    public static BridgesConfig ParseText(string torrcManagerCfgText) => new()
    {
        RedMoveMinFailures = ParseInt(ConfigParser.FirstIniValue(torrcManagerCfgText, "bridges", "red_move_min_failures", "600"), 600),
        BridgesPerRun = ParseInt(ConfigParser.FirstIniValue(torrcManagerCfgText, "tor", "bridges_per_run", "8"), 8),
    };

    private static int ParseInt(string raw, int defaultValue) =>
        int.TryParse(raw, out var value) && value >= 1 ? value : defaultValue;

    private static string ReadCfg(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "torrc_manager.cfg");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
