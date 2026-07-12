using TorVps.Core.Models;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class BridgeAvailabilityMonitorTests
{
    private static BridgeRecord Bridge(string text, HealthState instantaneous) =>
        new() { Index = 1, Text = text, State = instantaneous };

    private static HealthState StateOf(BridgeAvailabilityReport report, string text) =>
        report.Bridges.Single(b => b.Text == text).State;

    [Fact]
    public void NeverExercised_ClassifiesAsGray()
    {
        var monitor = new BridgeAvailabilityMonitor();

        var report = monitor.Update([Bridge("idle", HealthState.Unknown)], torHealthy: true);

        Assert.Equal(HealthState.Unknown, StateOf(report, "idle"));
        Assert.Equal(1, report.GrayCount);
        Assert.Equal(0, report.GreenCount + report.YellowCount + report.RedCount);
    }

    [Fact]
    public void AlwaysActiveGuard_ClassifiesAsGreen()
    {
        var monitor = new BridgeAvailabilityMonitor();

        BridgeAvailabilityReport report = default!;
        for (var i = 0; i < 5; i++)
            report = monitor.Update([Bridge("up", HealthState.Ok)], torHealthy: true);

        Assert.Equal(HealthState.Ok, StateOf(report, "up"));
        Assert.Equal(1, report.GreenCount);
    }

    [Fact]
    public void AlwaysFailed_ClassifiesAsRed()
    {
        var monitor = new BridgeAvailabilityMonitor();

        BridgeAvailabilityReport report = default!;
        for (var i = 0; i < 5; i++)
            report = monitor.Update([Bridge("down", HealthState.Error)], torHealthy: true);

        Assert.Equal(HealthState.Error, StateOf(report, "down"));
        Assert.Equal(1, report.RedCount);
    }

    [Fact]
    public void SometimesUp_SometimesDown_ClassifiesAsYellow()
    {
        var monitor = new BridgeAvailabilityMonitor();

        monitor.Update([Bridge("flaky", HealthState.Ok)], torHealthy: true);
        monitor.Update([Bridge("flaky", HealthState.Error)], torHealthy: true);
        var report = monitor.Update([Bridge("flaky", HealthState.Unknown)], torHealthy: true);

        Assert.Equal(HealthState.Warn, StateOf(report, "flaky"));
        Assert.Equal(1, report.YellowCount);
    }

    [Fact]
    public void RareFailure_KeepsALongLivedBridgeGreen()
    {
        var monitor = new BridgeAvailabilityMonitor();

        // 1 failure in 101 samples ≈ 1% — below the green threshold, so one transient
        // blip must NOT demote a reliable bridge to yellow forever.
        for (var i = 0; i < 100; i++)
            monitor.Update([Bridge("mostly-up", HealthState.Ok)], torHealthy: true);
        var report = monitor.Update([Bridge("mostly-up", HealthState.Error)], torHealthy: true);

        Assert.Equal(HealthState.Ok, StateOf(report, "mostly-up"));
        Assert.Equal(1, report.GreenCount);
    }

    [Fact]
    public void SustainedOutage_DemotesAGreenBridgeToYellow()
    {
        var monitor = new BridgeAvailabilityMonitor();

        for (var i = 0; i < 100; i++)
            monitor.Update([Bridge("degraded", HealthState.Ok)], torHealthy: true);
        // Keeps failing: once the down share crosses the threshold the bridge turns yellow.
        BridgeAvailabilityReport report = default!;
        for (var i = 0; i < 10; i++)
            report = monitor.Update([Bridge("degraded", HealthState.Error)], torHealthy: true);

        Assert.Equal(HealthState.Warn, StateOf(report, "degraded"));
        Assert.Equal(1, report.YellowCount);
    }

    [Fact]
    public void GreenThreshold_BoundaryIsInclusive()
    {
        var monitor = new BridgeAvailabilityMonitor();
        // Exactly at the threshold: down / (up + down) == GreenMaxDownRatio → still green.
        var down = 20L;
        var up = (long)(down / BridgeAvailabilityMonitor.GreenMaxDownRatio) - down;
        monitor.Seed(new Dictionary<string, BridgeStat> { ["edge"] = new(up, down) });

        var report = monitor.Update([Bridge("edge", HealthState.Unknown)], torHealthy: false);

        Assert.Equal(HealthState.Ok, StateOf(report, "edge"));

        // One more failure tips it over the threshold → yellow.
        monitor.Seed(new Dictionary<string, BridgeStat> { ["edge"] = new(up, down + 1) });
        report = monitor.Update([Bridge("edge", HealthState.Unknown)], torHealthy: false);

        Assert.Equal(HealthState.Warn, StateOf(report, "edge"));
    }

    [Fact]
    public void LegacyAutostatCounters_ClassifyByFailureShare()
    {
        // Counter shapes observed in a real bridges-autostat.info (addresses replaced with
        // TEST-NET ones): months of uptime with a sprinkle of transient failures must be green,
        // only genuinely flaky/dead bridges yellow/red.
        var monitor = new BridgeAvailabilityMonitor();
        monitor.Seed(new Dictionary<string, BridgeStat>
        {
            ["192.0.2.10:443"] = new(121083, 246),      // 0.2% down → green
            ["198.51.100.20:1992"] = new(121315, 20),   // 0.02% down → green
            ["203.0.113.30:25565"] = new(85506, 35823), // 30% down → yellow
            ["192.0.2.40:22022"] = new(0, 30),          // never up → red
            ["198.51.100.50:8443"] = new(0, 0),         // never used → gray
        });

        var report = monitor.Update(
        [
            Bridge("192.0.2.10:443", HealthState.Unknown),
            Bridge("198.51.100.20:1992", HealthState.Unknown),
            Bridge("203.0.113.30:25565", HealthState.Unknown),
            Bridge("192.0.2.40:22022", HealthState.Unknown),
            Bridge("198.51.100.50:8443", HealthState.Unknown),
        ], torHealthy: false);

        Assert.Equal(HealthState.Ok, StateOf(report, "192.0.2.10:443"));
        Assert.Equal(HealthState.Ok, StateOf(report, "198.51.100.20:1992"));
        Assert.Equal(HealthState.Warn, StateOf(report, "203.0.113.30:25565"));
        Assert.Equal(HealthState.Error, StateOf(report, "192.0.2.40:22022"));
        Assert.Equal(HealthState.Unknown, StateOf(report, "198.51.100.50:8443"));
        Assert.Equal(2, report.GreenCount);
        Assert.Equal(1, report.YellowCount);
        Assert.Equal(1, report.RedCount);
        Assert.Equal(1, report.GrayCount);
    }

    [Fact]
    public void Seed_NormalizesLegacyCountersUnderTheSampleCap()
    {
        var monitor = new BridgeAvailabilityMonitor();
        monitor.Seed(new Dictionary<string, BridgeStat> { ["big"] = new(121083, 246) });

        var stat = monitor.Snapshot()["big"];

        Assert.True(stat.Up + stat.Down <= BridgeAvailabilityMonitor.SampleCap);
        // Halving preserves the down share (and thus the classification) up to integer rounding.
        Assert.Equal(246.0 / (121083 + 246), (double)stat.Down / (stat.Up + stat.Down), tolerance: 0.001);
    }

    [Fact]
    public void SampleCap_DecaysCountersDuringUpdates()
    {
        var monitor = new BridgeAvailabilityMonitor();
        monitor.Seed(new Dictionary<string, BridgeStat>
        {
            ["b"] = new(BridgeAvailabilityMonitor.SampleCap, 0),
        });

        // Crossing the cap halves the history instead of growing forever.
        monitor.Update([Bridge("b", HealthState.Ok)], torHealthy: true);
        var afterCap = monitor.Snapshot()["b"];
        Assert.Equal(BridgeAvailabilityMonitor.SampleCap / 2, afterCap.Up);

        // And the bridge is still classified green from the decayed counters.
        var report = monitor.Update([Bridge("b", HealthState.Ok)], torHealthy: true);
        Assert.Equal(HealthState.Ok, StateOf(report, "b"));
    }

    [Fact]
    public void Decay_LetsACurrentOutageSurfaceDespiteLongUptimeHistory()
    {
        var monitor = new BridgeAvailabilityMonitor();
        // Months of perfect uptime, saved by an old version way over the cap.
        monitor.Seed(new Dictionary<string, BridgeStat> { ["b"] = new(120_000, 0) });

        // The bridge dies. With capped history (≤ SampleCap samples) the down share crosses
        // the green threshold within GreenMaxDownRatio * SampleCap ticks — minutes, not days.
        var ticksToYellow = (int)(BridgeAvailabilityMonitor.SampleCap * BridgeAvailabilityMonitor.GreenMaxDownRatio) + 1;
        BridgeAvailabilityReport report = default!;
        for (var i = 0; i < ticksToYellow; i++)
            report = monitor.Update([Bridge("b", HealthState.Error)], torHealthy: true);

        Assert.Equal(HealthState.Warn, StateOf(report, "b"));
    }

    [Fact]
    public void WhenTorUnhealthy_NoSampleIsRecorded()
    {
        var monitor = new BridgeAvailabilityMonitor();

        // Tor is down and every bridge looks failed — must NOT be counted.
        var report = monitor.Update([Bridge("b", HealthState.Error)], torHealthy: false);

        Assert.Equal(HealthState.Unknown, StateOf(report, "b"));
        Assert.Equal(1, report.GrayCount);
        Assert.Equal(0, report.RedCount);
    }

    [Fact]
    public void GreenBridge_StaysGreen_WhenTorGoesUnhealthy()
    {
        var monitor = new BridgeAvailabilityMonitor();

        for (var i = 0; i < 10; i++)
            monitor.Update([Bridge("b", HealthState.Ok)], torHealthy: true);

        // Tor drops; the same bridge now reports as failed, but sampling is gated off.
        var report = monitor.Update([Bridge("b", HealthState.Error)], torHealthy: false);

        Assert.Equal(HealthState.Ok, StateOf(report, "b"));
        Assert.Equal(1, report.GreenCount);
        Assert.Equal(0, report.YellowCount);
    }

    [Fact]
    public void Report_CountsEachCategoryAcrossManyBridges()
    {
        var monitor = new BridgeAvailabilityMonitor();

        // seed distinct histories
        monitor.Update(
        [
            Bridge("green", HealthState.Ok),
            Bridge("yellow", HealthState.Ok),
            Bridge("red", HealthState.Error),
            Bridge("gray", HealthState.Unknown),
        ], torHealthy: true);

        var report = monitor.Update(
        [
            Bridge("green", HealthState.Ok),
            Bridge("yellow", HealthState.Error),
            Bridge("red", HealthState.Error),
            Bridge("gray", HealthState.Unknown),
        ], torHealthy: true);

        Assert.Equal(1, report.GreenCount);
        Assert.Equal(1, report.YellowCount);
        Assert.Equal(1, report.RedCount);
        Assert.Equal(1, report.GrayCount);
        Assert.Equal(4, report.Total);
    }

    [Fact]
    public void Statistics_AccumulatePerBridgeAcrossCalls()
    {
        var monitor = new BridgeAvailabilityMonitor();

        monitor.Update([Bridge("shared", HealthState.Ok)], torHealthy: true);
        // A different bridge in the same tick must not disturb "shared".
        monitor.Update([Bridge("other", HealthState.Error)], torHealthy: true);
        var report = monitor.Update([Bridge("shared", HealthState.Unknown)], torHealthy: true);

        Assert.Equal(HealthState.Ok, StateOf(report, "shared"));
    }

    [Fact]
    public void Reset_ClearsAccumulatedHistory()
    {
        var monitor = new BridgeAvailabilityMonitor();

        monitor.Update([Bridge("b", HealthState.Error)], torHealthy: true);
        monitor.Reset();
        var report = monitor.Update([Bridge("b", HealthState.Unknown)], torHealthy: true);

        Assert.Equal(HealthState.Unknown, StateOf(report, "b"));
        Assert.Equal(1, report.GrayCount);
    }

    [Fact]
    public void Seed_RestoresPersistedHistory_WithoutNewSamples()
    {
        var monitor = new BridgeAvailabilityMonitor();
        monitor.Seed(new Dictionary<string, BridgeStat>
        {
            ["green"] = new(50, 0),
            ["flaky"] = new(10, 3),
            ["dead"] = new(0, 700),
        });

        // No new samples (tor unhealthy) — classification must come purely from the seeded counters.
        var report = monitor.Update(
        [
            Bridge("green", HealthState.Unknown),
            Bridge("flaky", HealthState.Unknown),
            Bridge("dead", HealthState.Unknown),
        ], torHealthy: false);

        Assert.Equal(HealthState.Ok, StateOf(report, "green"));
        Assert.Equal(HealthState.Warn, StateOf(report, "flaky"));
        Assert.Equal(HealthState.Error, StateOf(report, "dead"));
    }

    [Fact]
    public void Snapshot_ReturnsAccumulatedCounters()
    {
        var monitor = new BridgeAvailabilityMonitor();
        monitor.Update([Bridge("b", HealthState.Ok)], torHealthy: true);
        monitor.Update([Bridge("b", HealthState.Ok)], torHealthy: true);
        monitor.Update([Bridge("b", HealthState.Error)], torHealthy: true);

        var snapshot = monitor.Snapshot();

        Assert.Equal(new BridgeStat(2, 1), snapshot["b"]);
    }

    [Fact]
    public void ConfirmedRed_RequiresNeverUpAndEnoughFailures()
    {
        var monitor = new BridgeAvailabilityMonitor();
        monitor.Seed(new Dictionary<string, BridgeStat>
        {
            ["dead-confirmed"] = new(0, 600),
            ["dead-young"] = new(0, 10),      // red, but not enough evidence yet
            ["flaky"] = new(1, 9000),          // was up once → yellow, never quarantined
        });

        var confirmed = monitor.ConfirmedRed(minFailures: 600);

        Assert.Equal(["dead-confirmed"], confirmed);
    }
}
