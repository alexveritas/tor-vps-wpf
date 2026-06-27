namespace TorVps.Core.Models;

/// <summary>One parsed line of the rotating runtime log (tor-t.log).</summary>
public sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; } = LogLevel.Info;
    public required string Message { get; init; }
}
