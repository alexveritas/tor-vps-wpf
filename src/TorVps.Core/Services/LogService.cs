using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// File-based runtime log (tor-t.log) with 24h retention. Safe to use as a singleton: the prune
/// throttle is instance state shared across calls.
/// </summary>
public sealed class LogService : ILogService
{
    private const string RuntimeLogName = "tor-t.log";
    private const int TailBlockSize = 8 * 1024;
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);
    private static readonly TimeSpan PruneThrottle = TimeSpan.FromSeconds(60);
    private static readonly Regex LogLinePattern = new(@"^(\d+)\s\[(\w+)\]\s(.*)$", RegexOptions.Singleline);

    private static readonly string[] LegacyLogNames =
    [
        "tor_chain_dashboard_runtime.log",
        "tor_chain_dashboard_boot.log",
        "tor_chain_dashboard_cmd_debug.log",
        "tor_chain_dashboard_speed_ping.csv",
        "tor.log",
    ];

    private readonly Lock _pruneLock = new();
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public void Initialize(string baseDirectory)
    {
        ClearLegacyLogs(baseDirectory);
        TryIo(() => File.WriteAllText(RuntimeLogPath(baseDirectory), string.Empty));
        Append(baseDirectory, LogLevel.Info, "tor-t.log initialized; previous log cleared; retention=24h; single-log mode");
    }

    public void Append(string baseDirectory, LogLevel level, string message)
    {
        PruneIfDue(baseDirectory);
        var line = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()} [{LevelText(level)}] {message}";
        TryIo(() => File.AppendAllText(RuntimeLogPath(baseDirectory), line + "\n"));
    }

    public IReadOnlyList<string> ReadTail(string baseDirectory, int maxLines) => ReadTailLines(RuntimeLogPath(baseDirectory), maxLines);

    public IReadOnlyList<LogEntry> ReadTailEntries(string baseDirectory, int maxLines)
    {
        var lines = ReadTail(baseDirectory, maxLines);
        var entries = new List<LogEntry>(lines.Count);
        foreach (var line in lines)
        {
            if (TryParseEntry(line, out var entry))
                entries.Add(entry);
        }
        return entries;
    }

    private void PruneIfDue(string baseDirectory)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_pruneLock)
        {
            if (now - _lastPrune < PruneThrottle)
                return;
            _lastPrune = now;
        }

        var path = RuntimeLogPath(baseDirectory);
        if (!File.Exists(path))
            return;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return;
        }

        var cutoffEpoch = now.ToUnixTimeSeconds() - (long)RetentionPeriod.TotalSeconds;
        var contentLines = SplitLines(text);
        var kept = contentLines.Where(line => ParseLeadingEpoch(line) is not { } epoch || epoch >= cutoffEpoch).ToArray();

        if (kept.Length == contentLines.Length)
            return;

        var output = kept.Length == 0 ? string.Empty : string.Join("\n", kept) + "\n";
        TryIo(() => File.WriteAllText(path, output));
    }

    private static IReadOnlyList<string> ReadTailLines(string path, int maxLines)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var cursor = file.Length;
            var newlineCount = 0;
            var blocks = new List<byte[]>();

            while (cursor > 0 && newlineCount <= maxLines)
            {
                var take = (int)Math.Min(cursor, TailBlockSize);
                cursor -= take;
                file.Seek(cursor, SeekOrigin.Begin);

                var buffer = new byte[take];
                file.ReadExactly(buffer);

                newlineCount += buffer.Count(b => b == (byte)'\n');
                blocks.Add(buffer);
            }

            using var joined = new MemoryStream();
            for (var i = blocks.Count - 1; i >= 0; i--)
                joined.Write(blocks[i], 0, blocks[i].Length);

            var lines = SplitLines(Encoding.UTF8.GetString(joined.ToArray()));
            var tail = lines.Length <= maxLines ? lines : lines[^maxLines..];
            return Array.ConvertAll(tail, l => l.TrimEnd('\r'));
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static string[] SplitLines(string text)
    {
        var rawLines = text.Split('\n');
        return rawLines.Length > 0 && rawLines[^1].Length == 0 ? rawLines[..^1] : rawLines;
    }

    private static long? ParseLeadingEpoch(string line)
    {
        var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && long.TryParse(parts[0], out var value) ? value : null;
    }

    private static void ClearLegacyLogs(string baseDirectory)
    {
        foreach (var name in LegacyLogNames)
            TryIo(() => File.Delete(Path.Combine(baseDirectory, name)));
    }

    private static bool TryParseEntry(string line, out LogEntry entry)
    {
        var match = LogLinePattern.Match(line);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var epoch))
        {
            entry = null!;
            return false;
        }

        entry = new LogEntry
        {
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(epoch),
            Level = ParseLevel(match.Groups[2].Value),
            Message = match.Groups[3].Value,
        };
        return true;
    }

    private static LogLevel ParseLevel(string text) => text.ToUpperInvariant() switch
    {
        "DEBUG" => LogLevel.Debug,
        "WARN" => LogLevel.Warn,
        "ERROR" => LogLevel.Error,
        _ => LogLevel.Info,
    };

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };

    private static string RuntimeLogPath(string baseDirectory) => Path.Combine(baseDirectory, RuntimeLogName);

    private static void TryIo(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Best-effort logging I/O; a transient failure here must never crash the app.
        }
    }
}
