using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>What one watchdog cycle concluded.</summary>
public enum WatchdogOutcome
{
    BackingOff,
    RecoveredInBackoff,
    Healthy,
    RecoveredGlitch,
    RecoveredNewnym,
    RecoveredTorRestart,
    RecoveredTorRetry,
    RecoveredChainRestart,
    StillDown,
    BackedOff,
}

/// <summary>Timings/limits for the self-heal ladder. Populated from torrc_manager.cfg [watchdog] in production.</summary>
public sealed record WatchdogSettings
{
    /// <summary>Delay before the confirmation re-check (recheck_delay_sec).</summary>
    public TimeSpan RecheckDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Wait after NEWNYM before re-checking (newnym_wait_sec).</summary>
    public TimeSpan NewnymWait { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to stop escalating after the fail limit is hit (backoff_sec).</summary>
    public TimeSpan Backoff { get; init; } = TimeSpan.FromSeconds(300);

    /// <summary>Consecutive fully-failed ladders (L4 didn't help) before backing off (l4_fail_limit).</summary>
    public int ChainFailLimit { get; init; } = 15;
}

/// <summary>
/// Keeps the Onion-VPS tunnel alive. One cycle: verify the exit IP; on failure recheck once (to shrug off a single
/// glitch), then escalate L1 NEWNYM → L2 Tor restart (mihomo kept) → L3 Tor restart again → L4 Tor + mihomo restart,
/// stopping as soon as a step restores the exit. If the whole ladder fails
/// <see cref="WatchdogSettings.ChainFailLimit"/> cycles in a row the VPS/onion is presumed down (L5): escalation
/// pauses for <see cref="WatchdogSettings.Backoff"/> so a dead endpoint isn't hammered, but the cheap exit check
/// still runs each cycle so recovery is noticed immediately. The failure streak survives the backoff — once it has
/// tripped, every further failed ladder re-enters backoff at once; only an actual recovery clears it.
/// Pure orchestration — all effects go through <see cref="ITunnelActions"/>.
/// </summary>
public sealed class TunnelWatchdog
{
    private int _consecutiveChainFailures;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;

    public bool InBackoff(DateTimeOffset now) => now < _backoffUntil;

    public async Task<WatchdogOutcome> RunCycleAsync(ITunnelActions actions, WatchdogSettings settings, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (now < _backoffUntil)
        {
            // Detection continues during backoff so a returning VPS is noticed; only the escalation is paused.
            if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
            {
                Reset();
                actions.Log(LogLevel.Info, "WATCHDOG tunnel recovered while backing off — resuming normal checks");
                return WatchdogOutcome.RecoveredInBackoff;
            }
            return WatchdogOutcome.BackingOff;
        }

        if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
        {
            _consecutiveChainFailures = 0;
            return WatchdogOutcome.Healthy;
        }

        // A single failed check is often just a transient hiccup — wait and confirm before touching anything.
        await actions.DelayAsync(settings.RecheckDelay, cancellationToken).ConfigureAwait(false);
        if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
        {
            _consecutiveChainFailures = 0;
            return WatchdogOutcome.RecoveredGlitch;
        }

        actions.Log(LogLevel.Warn, $"TUNNEL DOWN: exit IP lost twice (recheck_delay_sec={settings.RecheckDelay.TotalSeconds:0}) — starting self-heal ladder");

        actions.Log(LogLevel.Warn, $"SELF-HEAL L1: NEWNYM (new Tor circuits), waiting {settings.NewnymWait.TotalSeconds:0}s (newnym_wait_sec)");
        await actions.NewnymAsync(cancellationToken).ConfigureAwait(false);
        await actions.DelayAsync(settings.NewnymWait, cancellationToken).ConfigureAwait(false);
        if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
        {
            _consecutiveChainFailures = 0;
            actions.Log(LogLevel.Info, "SELF-HEAL OK at L1: recovered after NEWNYM");
            return WatchdogOutcome.RecoveredNewnym;
        }

        actions.Log(LogLevel.Warn, "SELF-HEAL L2: restarting Tor (mihomo kept running), waiting for bootstrap");
        await actions.RestartTorAsync(cancellationToken).ConfigureAwait(false);
        if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
        {
            _consecutiveChainFailures = 0;
            actions.Log(LogLevel.Info, "SELF-HEAL OK at L2: recovered after Tor restart");
            return WatchdogOutcome.RecoveredTorRestart;
        }

        actions.Log(LogLevel.Warn, "SELF-HEAL L3: restarting Tor again, waiting for bootstrap");
        await actions.RestartTorAsync(cancellationToken).ConfigureAwait(false);
        if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
        {
            _consecutiveChainFailures = 0;
            actions.Log(LogLevel.Info, "SELF-HEAL OK at L3: recovered after second Tor restart");
            return WatchdogOutcome.RecoveredTorRetry;
        }

        actions.Log(LogLevel.Warn, "SELF-HEAL L4: restarting Tor + mihomo (full chain), waiting for bootstrap");
        await actions.RestartChainAsync(cancellationToken).ConfigureAwait(false);
        if (await actions.CheckExitAsync(cancellationToken).ConfigureAwait(false))
        {
            _consecutiveChainFailures = 0;
            actions.Log(LogLevel.Info, "SELF-HEAL OK at L4: recovered after full chain restart");
            return WatchdogOutcome.RecoveredChainRestart;
        }

        _consecutiveChainFailures++;
        if (_consecutiveChainFailures >= settings.ChainFailLimit)
        {
            _backoffUntil = now + settings.Backoff;
            actions.Log(LogLevel.Error, $"SELF-HEAL L5: VPS/onion likely down — {_consecutiveChainFailures} consecutive failed ladders (l4_fail_limit={settings.ChainFailLimit}); pausing escalation for {settings.Backoff.TotalSeconds:0}s (backoff_sec)");
            return WatchdogOutcome.BackedOff;
        }

        actions.Log(LogLevel.Warn, $"SELF-HEAL still down after full ladder ({_consecutiveChainFailures}/{settings.ChainFailLimit} before long backoff) — will retry next cycle");
        return WatchdogOutcome.StillDown;
    }

    public void Reset()
    {
        _consecutiveChainFailures = 0;
        _backoffUntil = DateTimeOffset.MinValue;
    }
}
