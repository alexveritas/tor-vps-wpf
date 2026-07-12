using TorVps.Core.Models;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class BridgeStatStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("torvps-stats-").FullName;
    private string StatsPath => Path.Combine(_dir, "bridges-autostat.info");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var stats = new Dictionary<string, BridgeStat>
        {
            ["1.1.1.1:443 AAAAAAAAAAAAAAAAAAAAAA"] = new(120, 0),
            ["2.2.2.2:443 BBBBBBBBBBBBBBBBBBBBBB"] = new(5, 17),
        };

        BridgeStatStore.Save(StatsPath, stats);
        var loaded = BridgeStatStore.Load(StatsPath);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(new BridgeStat(120, 0), loaded["1.1.1.1:443 AAAAAAAAAAAAAAAAAAAAAA"]);
        Assert.Equal(new BridgeStat(5, 17), loaded["2.2.2.2:443 BBBBBBBBBBBBBBBBBBBBBB"]);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(BridgeStatStore.Load(StatsPath));
    }

    [Fact]
    public void Load_SkipsHeaderAndGarbageLines()
    {
        File.WriteAllText(StatsPath, "# header\n\nnot numbers at all\n-1 2 negative\n3 4 5.5.5.5:443 CCCC\n");

        var loaded = BridgeStatStore.Load(StatsPath);

        Assert.Single(loaded);
        Assert.Equal(new BridgeStat(3, 4), loaded["5.5.5.5:443 CCCC"]);
    }
}
