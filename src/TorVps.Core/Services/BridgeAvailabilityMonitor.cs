using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>
/// In-memory availability tracker for bridges. Each refresh where Tor is healthy contributes one observation per
/// bridge: an active guard counts as "up", a failed guard as "down", anything else is ignored (the bridge simply
/// wasn't exercised this tick). A bridge is then classified from its cumulative counts:
/// <list type="bullet">
/// <item>never up and never down → gray (Tor never used it)</item>
/// <item>only up → green (always reachable)</item>
/// <item>up and down → yellow (sometimes unreachable)</item>
/// <item>only down → red (used but never reachable)</item>
/// </list>
/// Sampling is gated on Tor being healthy so a down/bootstrapping Tor can't paint every bridge red.
/// </summary>
public sealed class BridgeAvailabilityMonitor : IBridgeAvailabilityMonitor
{
    private sealed class Counter
    {
        public long Up;
        public long Down;
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

    public void Reset()
    {
        lock (_gate)
        {
            _counters.Clear();
        }
    }

    private static HealthState Classify(Counter counter) => (counter.Up, counter.Down) switch
    {
        (0, 0) => HealthState.Unknown, // gray: never used
        (_, 0) => HealthState.Ok,      // green: always reachable
        (0, _) => HealthState.Error,   // red: used but never reachable
        _ => HealthState.Warn,         // yellow: sometimes unreachable
    };
}
