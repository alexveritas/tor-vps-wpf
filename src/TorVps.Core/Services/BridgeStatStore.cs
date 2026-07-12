using System.Globalization;
using System.Text;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// Persists the cumulative per-bridge availability counters to bridges-autostat.info so yellow/red statistics
/// survive app restarts. Format: one bridge per line, "&lt;up&gt; &lt;down&gt; &lt;bridge key&gt;".
/// </summary>
public static class BridgeStatStore
{
    private const string Header = "# TorVps bridge availability stats v1: <up> <down> <bridge>";

    public static IReadOnlyDictionary<string, BridgeStat> Load(string path)
    {
        var stats = new Dictionary<string, BridgeStat>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return stats;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var up)
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var down))
                continue;

            stats[parts[2]] = new BridgeStat(up, down);
        }
        return stats;
    }

    public static void Save(string path, IReadOnlyDictionary<string, BridgeStat> stats)
    {
        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        foreach (var (bridge, stat) in stats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append(stat.Up).Append(' ').Append(stat.Down).Append(' ').Append(bridge).Append('\n');
        File.WriteAllText(path, builder.ToString());
    }
}
