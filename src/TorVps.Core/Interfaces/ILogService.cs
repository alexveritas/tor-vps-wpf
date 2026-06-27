using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>Single rotating runtime log (tor-t.log) shared by the whole app, with 24h retention.</summary>
public interface ILogService
{
    /// <summary>Removes legacy/secondary log files and starts a fresh runtime log for this run.</summary>
    void Initialize(string baseDirectory);

    /// <summary>Appends one line to the runtime log. Prunes entries older than 24h, throttled to once per minute.</summary>
    void Append(string baseDirectory, LogLevel level, string message);

    /// <summary>Reads up to maxLines from the end of the runtime log, oldest first.</summary>
    IReadOnlyList<string> ReadTail(string baseDirectory, int maxLines);

    /// <summary>Reads and parses up to maxLines from the end of the runtime log, oldest first.</summary>
    IReadOnlyList<LogEntry> ReadTailEntries(string baseDirectory, int maxLines);
}
