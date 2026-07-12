using TorVps.Core.Models;

namespace TorVps.Core.Interfaces;

/// <summary>Accumulates per-bridge reachability over time and classifies each bridge as green/yellow/red/gray.</summary>
public interface IBridgeAvailabilityMonitor
{
    /// <summary>
    /// Folds one refresh into the running statistics and returns the current classification.
    /// </summary>
    /// <param name="records">Bridges parsed this tick, each carrying an instantaneous state
    /// (Ok = active guard, Error = failed, Unknown = idle).</param>
    /// <param name="torHealthy">Whether Tor is fully working right now. When false, no sample is recorded so a
    /// down/bootstrapping Tor never counts against the bridges — only the last-known classification is returned.</param>
    BridgeAvailabilityReport Update(IReadOnlyList<BridgeRecord> records, bool torHealthy);

    /// <summary>Pre-loads counters persisted by an earlier run (bridges-autostat.info). Call before the first Update.</summary>
    void Seed(IReadOnlyDictionary<string, BridgeStat> stats);

    /// <summary>A copy of the current cumulative counters, for persisting.</summary>
    IReadOnlyDictionary<string, BridgeStat> Snapshot();

    /// <summary>Bridges that were never up and have at least <paramref name="minFailures"/> failed samples —
    /// candidates for quarantine into bridges-red.txt.</summary>
    IReadOnlyList<string> ConfirmedRed(int minFailures);

    /// <summary>Clears all accumulated statistics.</summary>
    void Reset();
}
