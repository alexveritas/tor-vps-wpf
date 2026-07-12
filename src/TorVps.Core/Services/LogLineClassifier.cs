using System.Text.RegularExpressions;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// Derives a severity from one runtime-log line for UI coloring: the app's own "epoch [LEVEL] msg" tag first,
/// then bracketed severities embedded in child-process output (Tor logs "[warn]"/"[err]" inside INFO lines).
/// Plain words like "error" without brackets deliberately do not trigger — too many false positives.
/// </summary>
public static class LogLineClassifier
{
    private static readonly Regex LevelTagPattern = new(@"^\d+\s\[(\w+)\]", RegexOptions.Compiled);

    public static LogLevel Classify(string line)
    {
        var level = LogLevel.Info;
        var match = LevelTagPattern.Match(line);
        if (match.Success)
        {
            level = match.Groups[1].Value.ToUpperInvariant() switch
            {
                "ERROR" => LogLevel.Error,
                "WARN" => LogLevel.Warn,
                "DEBUG" => LogLevel.Debug,
                _ => LogLevel.Info,
            };
        }

        if (level == LogLevel.Error)
            return LogLevel.Error;
        if (line.Contains("[err]", StringComparison.OrdinalIgnoreCase) || line.Contains("[error]", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Error;
        if (level == LogLevel.Warn)
            return LogLevel.Warn;
        if (line.Contains("[warn]", StringComparison.OrdinalIgnoreCase) || line.Contains("[warning]", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Warn;
        return level;
    }
}
