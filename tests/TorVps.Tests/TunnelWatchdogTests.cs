using TorVps.Core.Interfaces;
using TorVps.Core.Models;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class TunnelWatchdogTests
{
    private sealed class FakeActions : ITunnelActions
    {
        private readonly Queue<bool> _checks;
        public List<string> Calls { get; } = new();

        public FakeActions(params bool[] checkResults) => _checks = new Queue<bool>(checkResults);

        public Task<bool> CheckExitAsync(CancellationToken ct)
        {
            var result = _checks.Count > 0 && _checks.Dequeue();
            Calls.Add("check:" + result);
            return Task.FromResult(result);
        }

        public Task NewnymAsync(CancellationToken ct) { Calls.Add("newnym"); return Task.CompletedTask; }
        public Task RestartTorAsync(CancellationToken ct) { Calls.Add("tor"); return Task.CompletedTask; }
        public Task RestartChainAsync(CancellationToken ct) { Calls.Add("chain"); return Task.CompletedTask; }
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) { Calls.Add("delay"); return Task.CompletedTask; }
        public void Log(LogLevel level, string message) { }
    }

    private static readonly WatchdogSettings Settings = new() { ChainFailLimit = 2 };
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task HealthyFirstCheck_DoesNotEscalate()
    {
        var a = new FakeActions(true);
        var outcome = await new TunnelWatchdog().RunCycleAsync(a, Settings, Now);

        Assert.Equal(WatchdogOutcome.Healthy, outcome);
        Assert.Equal(new[] { "check:True" }, a.Calls);
    }

    [Fact]
    public async Task SingleGlitch_RecheckPasses_DoesNotEscalate()
    {
        var a = new FakeActions(false, true);
        var outcome = await new TunnelWatchdog().RunCycleAsync(a, Settings, Now);

        Assert.Equal(WatchdogOutcome.RecoveredGlitch, outcome);
        Assert.Equal(new[] { "check:False", "delay", "check:True" }, a.Calls);
        Assert.DoesNotContain("newnym", a.Calls);
    }

    [Fact]
    public async Task L1_Newnym_Recovers_StopsBeforeRestarts()
    {
        var a = new FakeActions(false, false, true);
        var outcome = await new TunnelWatchdog().RunCycleAsync(a, Settings, Now);

        Assert.Equal(WatchdogOutcome.RecoveredNewnym, outcome);
        Assert.Contains("newnym", a.Calls);
        Assert.DoesNotContain("tor", a.Calls);
        Assert.DoesNotContain("chain", a.Calls);
    }

    [Fact]
    public async Task L2_TorRestart_Recovers_WithoutSecondRestart()
    {
        var a = new FakeActions(false, false, false, true);
        var outcome = await new TunnelWatchdog().RunCycleAsync(a, Settings, Now);

        Assert.Equal(WatchdogOutcome.RecoveredTorRestart, outcome);
        Assert.Equal(1, a.Calls.Count(c => c == "tor"));
        Assert.DoesNotContain("chain", a.Calls);
    }

    [Fact]
    public async Task L3_SecondTorRestart_Recovers_BeforeChainRestart()
    {
        var a = new FakeActions(false, false, false, false, true);
        var outcome = await new TunnelWatchdog().RunCycleAsync(a, Settings, Now);

        Assert.Equal(WatchdogOutcome.RecoveredTorRetry, outcome);
        Assert.Equal(2, a.Calls.Count(c => c == "tor"));
        Assert.DoesNotContain("chain", a.Calls);
    }

    [Fact]
    public async Task L4_ChainRestart_Recovers_FullLadderOrder()
    {
        var a = new FakeActions(false, false, false, false, false, true);
        var outcome = await new TunnelWatchdog().RunCycleAsync(a, Settings, Now);

        Assert.Equal(WatchdogOutcome.RecoveredChainRestart, outcome);
        Assert.Equal(
            new[] { "check:False", "delay", "check:False", "newnym", "delay", "check:False", "tor", "check:False", "tor", "check:False", "chain", "check:True" },
            a.Calls);
    }

    [Fact]
    public async Task L5_ConsecutiveFailedLadders_BackOff_ChecksButNeverEscalatesDuringBackoff()
    {
        var watchdog = new TunnelWatchdog();

        // Every check fails across all cycles (queue drains to default-false). ChainFailLimit = 2.
        Assert.Equal(WatchdogOutcome.StillDown, await watchdog.RunCycleAsync(new FakeActions(), Settings, Now));
        Assert.Equal(WatchdogOutcome.BackedOff, await watchdog.RunCycleAsync(new FakeActions(), Settings, Now));
        Assert.True(watchdog.InBackoff(Now));

        // While backing off, only the cheap exit check runs — no NEWNYM/restarts ("don't hammer").
        var a = new FakeActions(false);
        Assert.Equal(WatchdogOutcome.BackingOff, await watchdog.RunCycleAsync(a, Settings, Now));
        Assert.Equal(new[] { "check:False" }, a.Calls);
    }

    [Fact]
    public async Task Backoff_RecoveryDuringBackoff_ClearsEverything()
    {
        var watchdog = new TunnelWatchdog();
        await watchdog.RunCycleAsync(new FakeActions(), Settings, Now);
        await watchdog.RunCycleAsync(new FakeActions(), Settings, Now); // enters backoff

        var a = new FakeActions(true);
        Assert.Equal(WatchdogOutcome.RecoveredInBackoff, await watchdog.RunCycleAsync(a, Settings, Now));
        Assert.False(watchdog.InBackoff(Now));

        // Streak was cleared too: the next failed ladder is 1/2 again, not an instant re-backoff.
        Assert.Equal(WatchdogOutcome.StillDown, await watchdog.RunCycleAsync(new FakeActions(), Settings, Now));
    }

    [Fact]
    public async Task Backoff_StreakSurvives_NextFailedLadderReentersBackoffImmediately()
    {
        var watchdog = new TunnelWatchdog();
        await watchdog.RunCycleAsync(new FakeActions(), Settings, Now);
        await watchdog.RunCycleAsync(new FakeActions(), Settings, Now); // streak 2 → backoff

        // After the backoff window expires the ladder runs again; one more failure re-enters backoff at once.
        var afterBackoff = Now + Settings.Backoff + TimeSpan.FromSeconds(1);
        Assert.False(watchdog.InBackoff(afterBackoff));
        Assert.Equal(WatchdogOutcome.BackedOff, await watchdog.RunCycleAsync(new FakeActions(), Settings, afterBackoff));
        Assert.True(watchdog.InBackoff(afterBackoff));
    }

    [Fact]
    public async Task Recovery_ResetsTheConsecutiveFailureStreak()
    {
        var watchdog = new TunnelWatchdog();

        await watchdog.RunCycleAsync(new FakeActions(), Settings, Now);            // failed ladder #1
        await watchdog.RunCycleAsync(new FakeActions(true), Settings, Now);        // healthy → reset
        Assert.Equal(WatchdogOutcome.StillDown, await watchdog.RunCycleAsync(new FakeActions(), Settings, Now)); // back to #1, not backoff
        Assert.False(watchdog.InBackoff(Now));
    }
}
