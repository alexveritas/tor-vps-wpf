namespace TorVps.Core.Models;

/// <summary>Outcome of fetching an IP-echo endpoint through the tunnel to observe/verify the exit IP.</summary>
public sealed record ExitIpResult
{
    /// <summary>The request completed and the observed IP equals the expected VPS exit IP.</summary>
    public bool Success { get; init; }

    /// <summary>The request completed (some IP came back), regardless of whether it matched.</summary>
    public bool Reachable { get; init; }

    /// <summary>The public IP the tunnel actually exited from (empty if the request failed).</summary>
    public string ObservedIp { get; init; } = string.Empty;

    /// <summary>End-to-end request latency through the tunnel, in milliseconds.</summary>
    public double ElapsedMs { get; init; }

    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
}
