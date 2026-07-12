namespace TorVps.Core.Services;

/// <summary>Health of the "Tor → internet" path, from the observed exit IP and how long the fetch took.</summary>
public enum ExitIpVerdict
{
    /// <summary>The probe did not complete — no exit IP came back (red).</summary>
    Down,

    /// <summary>The exit IP equals the real ISP egress IP: traffic is NOT going through Tor — a leak (red).</summary>
    Leak,

    /// <summary>Reachable via a non-ISP exit, but the fetch took longer than the error threshold (red).</summary>
    TooSlow,

    /// <summary>Reachable via a non-ISP exit, but slower than the warn threshold (yellow).</summary>
    Slow,

    /// <summary>Reachable via a non-ISP exit within the warn threshold (green).</summary>
    Ok,
}

/// <summary>
/// Decides the "Tor → internet" health. We fetch our exit IP through Tor and want anything except the address our
/// provider hands us directly: the ISP IP means traffic bypassed Tor (a leak → red). Otherwise the verdict is by
/// latency — over <paramref name="errorMs"/> is red, over <paramref name="warnMs"/> is yellow, else green — so a
/// working-but-crawling tunnel is visible. Both thresholds come from torrc_manager.cfg so they can be tuned.
/// </summary>
public static class ExitIpAssessment
{
    public static ExitIpVerdict Assess(bool reachable, string? observedIp, string? providerIp, double elapsedMs, int warnMs, int errorMs)
    {
        if (!reachable || string.IsNullOrWhiteSpace(observedIp))
            return ExitIpVerdict.Down;
        if (!string.IsNullOrWhiteSpace(providerIp) && string.Equals(observedIp, providerIp, StringComparison.Ordinal))
            return ExitIpVerdict.Leak;
        if (elapsedMs > errorMs)
            return ExitIpVerdict.TooSlow;
        if (elapsedMs > warnMs)
            return ExitIpVerdict.Slow;
        return ExitIpVerdict.Ok;
    }
}
