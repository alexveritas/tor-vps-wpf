using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// In-memory availability tracker for bridges. Each refresh where Tor is healthy contributes one observation per
/// bridge: an active guard counts as "up", a failed guard as "down", anything else is ignored (the bridge simply
/// wasn't exercised this tick). A bridge is then classified from its accumulated counts:
/// <list type="bullet">
/// <item>never up and never down → gray (Tor never used it)</item>
/// <item>up, with a down share of at most <see cref="GreenMaxDownRatio"/> → green (reliably reachable)</item>
/// <item>up, but failing more often than that → yellow (sometimes unreachable)</item>
/// <item>only down → red (used but never reachable)</item>
/// </list>
/// Green tolerates rare failures on purpose: counters persist across runs (bridges-autostat.info) and a single
/// transient blip — or one failure log line re-sampled for many ticks while it sits in the log tail — used to
/// demote a bridge to yellow forever, making green unreachable for any long-lived bridge.
/// To keep the ratio reflecting recent behaviour rather than all-time history, per-bridge counters are capped at
/// <see cref="SampleCap"/> observations: once the total exceeds it, both counters are halved (exponential decay),
/// so old evidence fades and a current outage shows up within minutes instead of drowning in months of uptime.
/// Sampling is gated on Tor being healthy so a down/bootstrapping Tor can't paint every bridge red.
/// </summary>
public sealed class BridgeAvailabilityMonitor : IBridgeAvailabilityMonitor
{
    /// <summary>Largest share of "down" observations a bridge may have and still be green (2%).</summary>
    public const double GreenMaxDownRatio = 0.02;

    /// <summary>Per-bridge observation cap: above this, both counters are halved. At the dashboard's ~1 Hz
    /// sampling this is roughly 5–6 hours of history.</summary>
    public const long SampleCap = 20_000;

    private sealed class Counter
    {
        public long Up;
        public long Down;

        /// <summary>Halves both counters until their sum is back under <see cref="SampleCap"/> (a loop because
        /// seeded legacy stats can be many times over the cap).</summary>
        public void Decay()
        {
            while (Up + Down > SampleCap)
            {
                Up /= 2;
                Down /= 2;
            }
        }
    }

    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public BridgeAvailabilityReport Update(IReadOnlyList<BridgeRecord> records, bool torHealthy)
    {
        lock (_gate)
        {
            var bridges = new List<BridgeRecord>(records.Count);
            int green = 0, yellow = 0, red = 0, gray = 0;

            foreach (var record in records)
            {
                if (!_counters.TryGetValue(record.Text, out var counter))
                {
                    counter = new Counter();
                    _counters[record.Text] = counter;
                }

                if (torHealthy)
                {
                    if (record.State == HealthState.Ok)
                        counter.Up++;
                    else if (record.State == HealthState.Error)
                        counter.Down++;
                    counter.Decay();
                }

                var availability = Classify(counter);
                switch (availability)
                {
                    case HealthState.Ok:
                        green++;
                        break;
                    case HealthState.Warn:
                        yellow++;
                        break;
                    case HealthState.Error:
                        red++;
                        break;
                    default:
                        gray++;
                        break;
                }

                bridges.Add(record with { State = availability });
            }

            return new BridgeAvailabilityReport
            {
                Bridges = bridges,
                GreenCount = green,
                YellowCount = yellow,
                RedCount = red,
                GrayCount = gray,
            };
        }
    }

    public void Seed(IReadOnlyDictionary<string, BridgeStat> stats)
    {
        lock (_gate)
        {
            foreach (var (bridge, stat) in stats)
            {
                // Normalize legacy stats saved before the cap existed (they grew unbounded).
                var counter = new Counter { Up = stat.Up, Down = stat.Down };
                counter.Decay();
                _counters[bridge] = counter;
            }
        }
    }

    public IReadOnlyDictionary<string, BridgeStat> Snapshot()
    {
        lock (_gate)
        {
            var snapshot = new Dictionary<string, BridgeStat>(_counters.Count, StringComparer.Ordinal);
            foreach (var (bridge, counter) in _counters)
                snapshot[bridge] = new BridgeStat(counter.Up, counter.Down);
            return snapshot;
        }
    }

    public IReadOnlyList<string> ConfirmedRed(int minFailures)
    {
        lock (_gate)
        {
            return _counters
                .Where(pair => pair.Value.Up == 0 && pair.Value.Down >= minFailures)
                .Select(pair => pair.Key)
                .ToList();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _counters.Clear();
        }
    }

    private static HealthState Classify(Counter counter)
    {
        if (counter.Up == 0 && counter.Down == 0)
            return HealthState.Unknown; // gray: never used
        if (counter.Up == 0)
            return HealthState.Error;   // red: used but never reachable

        // Green tolerates rare failures (see class docs); yellow means it fails noticeably often.
        var downRatio = (double)counter.Down / (counter.Up + counter.Down);
        return downRatio <= GreenMaxDownRatio ? HealthState.Ok : HealthState.Warn;
    }
}
