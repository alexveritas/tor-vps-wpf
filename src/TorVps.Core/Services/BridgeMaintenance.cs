using TorVps.Core.Config;

namespace TorVps.Core.Services;

/// <summary>
/// File-level upkeep of bridges.txt: removing full duplicates and quarantining never-reachable (red) bridges
/// into bridges-red.txt. Comment/blank lines and the file's newline style are preserved; the file is only
/// rewritten when something actually changed.
/// </summary>
public static class BridgeMaintenance
{
    /// <summary>
    /// The identity a bridge line gets in <see cref="ConfigParser.ParseBridges"/> (BridgeRecord.Text) — used to
    /// match availability-monitor keys back to file lines. Null for blank/comment lines.
    /// </summary>
    public static string? LineKey(string rawLine)
    {
        if (ConfigParser.ParseBridgeLine(rawLine) is not (string address, string fingerprint))
            return null;

        var fingerprintShown = fingerprint.Length <= 22 ? fingerprint : fingerprint[..22];
        return fingerprintShown.Length == 0 ? address : $"{address} {fingerprintShown}";
    }

    /// <summary>
    /// Removes full-duplicate bridge lines (same content modulo whitespace/case), keeping the first occurrence.
    /// Returns the removed lines (trimmed).
    /// </summary>
    public static IReadOnlyList<string> RemoveDuplicateLines(string bridgesPath)
    {
        if (!File.Exists(bridgesPath))
            return Array.Empty<string>();

        var text = File.ReadAllText(bridgesPath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>(lines.Count);
        var removed = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                kept.Add(line);
                continue;
            }

            var signature = string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (seen.Add(signature))
                kept.Add(line);
            else
                removed.Add(trimmed);
        }

        if (removed.Count > 0)
            File.WriteAllText(bridgesPath, string.Join(newline, kept));
        return removed;
    }

    /// <summary>
    /// Moves bridge lines whose <see cref="LineKey"/> is in <paramref name="redKeys"/> out of bridges.txt and
    /// appends them to bridges-red.txt under a dated header. Never leaves fewer than
    /// <paramref name="keepAtLeast"/> active bridge lines behind (excess red bridges stay until the list grows).
    /// Returns the moved lines (trimmed).
    /// </summary>
    public static IReadOnlyList<string> MoveRedBridges(string bridgesPath, string redFilePath, IReadOnlyCollection<string> redKeys, int keepAtLeast)
    {
        if (redKeys.Count == 0 || !File.Exists(bridgesPath))
            return Array.Empty<string>();

        var text = File.ReadAllText(bridgesPath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var redSet = new HashSet<string>(redKeys, StringComparer.Ordinal);
        var activeCount = lines.Count(l => LineKey(l) is not null);
        var allowedToMove = Math.Max(0, activeCount - keepAtLeast);
        if (allowedToMove == 0)
            return Array.Empty<string>();

        var kept = new List<string>(lines.Count);
        var moved = new List<string>();
        foreach (var line in lines)
        {
            if (moved.Count < allowedToMove && LineKey(line) is { } key && redSet.Contains(key))
                moved.Add(line.Trim());
            else
                kept.Add(line);
        }

        if (moved.Count == 0)
            return Array.Empty<string>();

        var header = $"# moved from bridges.txt {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC (never reachable while Tor was healthy)";
        var block = string.Join(newline, moved.Prepend(header)) + newline;
        File.AppendAllText(redFilePath, block);
        File.WriteAllText(bridgesPath, string.Join(newline, kept));
        return moved;
    }
}
