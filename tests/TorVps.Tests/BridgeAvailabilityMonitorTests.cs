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
    public void OneFailure_DemotesAGreenBridgeToYellow()
    {
        var monitor = new BridgeAvailabilityMonitor();

        for (var i = 0; i < 100; i++)
            monitor.Update([Bridge("mostly-up", HealthState.Ok)], torHealthy: true);
        var report = monitor.Update([Bridge("mostly-up", HealthState.Error)], torHealthy: true);

        Assert.Equal(HealthState.Warn, StateOf(report, "mostly-up"));
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
}
