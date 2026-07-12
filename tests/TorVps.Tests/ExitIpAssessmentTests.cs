using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class ExitIpAssessmentTests
{
    private const string Provider = "203.0.113.7";   // our real ISP IP
    private const string TorExit = "185.220.101.34";  // a random Tor exit
    private const int Warn = 5000;
    private const int Error = 30000;

    [Fact]
    public void Unreachable_IsDown()
    {
        Assert.Equal(ExitIpVerdict.Down, ExitIpAssessment.Assess(reachable: false, observedIp: "", Provider, 100, Warn, Error));
        Assert.Equal(ExitIpVerdict.Down, ExitIpAssessment.Assess(reachable: true, observedIp: "  ", Provider, 100, Warn, Error));
    }

    [Fact]
    public void ExitEqualsProviderIp_IsLeak_EvenWhenFast()
    {
        Assert.Equal(ExitIpVerdict.Leak, ExitIpAssessment.Assess(true, Provider, Provider, 120, Warn, Error));
    }

    [Fact]
    public void Leak_TakesPrecedenceOverLatency()
    {
        // Exit is the ISP IP AND very slow — a leak is the more important signal (still red either way).
        Assert.Equal(ExitIpVerdict.Leak, ExitIpAssessment.Assess(true, Provider, Provider, 40000, Warn, Error));
    }

    [Fact]
    public void FastNonProviderExit_IsOk()
    {
        Assert.Equal(ExitIpVerdict.Ok, ExitIpAssessment.Assess(true, TorExit, Provider, 1200, Warn, Error));
    }

    [Fact]
    public void AboveWarnThreshold_IsSlow()
    {
        Assert.Equal(ExitIpVerdict.Slow, ExitIpAssessment.Assess(true, TorExit, Provider, 6000, Warn, Error));
    }

    [Fact]
    public void AboveErrorThreshold_IsTooSlow()
    {
        Assert.Equal(ExitIpVerdict.TooSlow, ExitIpAssessment.Assess(true, TorExit, Provider, 31000, Warn, Error));
    }

    [Fact]
    public void Thresholds_AreStrictlyGreater()
    {
        Assert.Equal(ExitIpVerdict.Ok, ExitIpAssessment.Assess(true, TorExit, Provider, 5000, Warn, Error));       // == warn → still green
        Assert.Equal(ExitIpVerdict.Slow, ExitIpAssessment.Assess(true, TorExit, Provider, 5001, Warn, Error));     // just over warn
        Assert.Equal(ExitIpVerdict.Slow, ExitIpAssessment.Assess(true, TorExit, Provider, 30000, Warn, Error));    // == error → still yellow
        Assert.Equal(ExitIpVerdict.TooSlow, ExitIpAssessment.Assess(true, TorExit, Provider, 30001, Warn, Error)); // just over error
    }

    [Fact]
    public void ProviderIpUnknown_CannotLeak_JudgesByLatencyOnly()
    {
        // Baseline ISP IP not yet learned → can't prove a leak, so judge purely on latency.
        Assert.Equal(ExitIpVerdict.Ok, ExitIpAssessment.Assess(true, Provider, providerIp: "", 1000, Warn, Error));
        Assert.Equal(ExitIpVerdict.Slow, ExitIpAssessment.Assess(true, TorExit, providerIp: "", 6000, Warn, Error));
    }
}
