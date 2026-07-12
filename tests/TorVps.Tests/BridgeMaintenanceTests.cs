using TorVps.Core.Config;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class BridgeMaintenanceTests : IDisposable
{
    private const string BridgeA = "obfs4 1.1.1.1:443 AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA cert=aa iat-mode=0";
    private const string BridgeB = "obfs4 2.2.2.2:443 BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB cert=bb iat-mode=0";
    private const string BridgeC = "obfs4 3.3.3.3:443 CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC cert=cc iat-mode=0";

    private readonly string _dir = Directory.CreateTempSubdirectory("torvps-bridges-").FullName;

    private string BridgesPath => Path.Combine(_dir, "bridges.txt");
    private string RedPath => Path.Combine(_dir, "bridges-red.txt");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LineKey_MatchesParseBridgesRecordText()
    {
        var text = BridgeA + "\n" + BridgeB + "\n";
        var records = ConfigParser.ParseBridges(text, [], []);

        Assert.Equal(records[0].Text, BridgeMaintenance.LineKey(BridgeA));
        Assert.Equal(records[1].Text, BridgeMaintenance.LineKey(BridgeB));
        Assert.Null(BridgeMaintenance.LineKey("# comment"));
        Assert.Null(BridgeMaintenance.LineKey("   "));
    }

    [Fact]
    public void RemoveDuplicateLines_KeepsFirst_DropsExactAndWhitespaceVariants()
    {
        File.WriteAllText(BridgesPath, "# header\n" + BridgeA + "\n" + BridgeB + "\n" + BridgeA + "\n" + BridgeB.Replace(" ", "  ") + "\n");

        var removed = BridgeMaintenance.RemoveDuplicateLines(BridgesPath);

        Assert.Equal(2, removed.Count);
        var lines = File.ReadAllLines(BridgesPath);
        Assert.Equal(["# header", BridgeA, BridgeB], lines);
    }

    [Fact]
    public void RemoveDuplicateLines_CleanFile_IsUntouched()
    {
        var original = "# header\n" + BridgeA + "\n\n" + BridgeB + "\n";
        File.WriteAllText(BridgesPath, original);

        var removed = BridgeMaintenance.RemoveDuplicateLines(BridgesPath);

        Assert.Empty(removed);
        Assert.Equal(original, File.ReadAllText(BridgesPath));
    }

    [Fact]
    public void RemoveDuplicateLines_PreservesCrlfStyle()
    {
        File.WriteAllText(BridgesPath, BridgeA + "\r\n" + BridgeA + "\r\n" + BridgeB + "\r\n");

        var removed = BridgeMaintenance.RemoveDuplicateLines(BridgesPath);

        Assert.Single(removed);
        Assert.Equal(BridgeA + "\r\n" + BridgeB + "\r\n", File.ReadAllText(BridgesPath));
    }

    [Fact]
    public void RemoveDuplicateLines_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(BridgeMaintenance.RemoveDuplicateLines(Path.Combine(_dir, "nope.txt")));
    }

    [Fact]
    public void MoveRedBridges_MovesMatchingLines_AppendsToRedFile()
    {
        File.WriteAllText(BridgesPath, BridgeA + "\n" + BridgeB + "\n" + BridgeC + "\n");
        var redKey = BridgeMaintenance.LineKey(BridgeB)!;

        var moved = BridgeMaintenance.MoveRedBridges(BridgesPath, RedPath, [redKey], keepAtLeast: 1);

        Assert.Equal([BridgeB], moved);
        Assert.Equal([BridgeA, BridgeC], File.ReadAllLines(BridgesPath).Where(l => l.Trim().Length > 0));
        var redContent = File.ReadAllText(RedPath);
        Assert.Contains(BridgeB, redContent);
        Assert.Contains("# moved from bridges.txt", redContent);
    }

    [Fact]
    public void MoveRedBridges_NeverDropsBelowKeepAtLeast()
    {
        File.WriteAllText(BridgesPath, BridgeA + "\n" + BridgeB + "\n" + BridgeC + "\n");
        var redKeys = new[] { BridgeMaintenance.LineKey(BridgeA)!, BridgeMaintenance.LineKey(BridgeB)!, BridgeMaintenance.LineKey(BridgeC)! };

        // 3 active, keep ≥ 2 → only 1 may be moved even though all 3 are red.
        var moved = BridgeMaintenance.MoveRedBridges(BridgesPath, RedPath, redKeys, keepAtLeast: 2);

        Assert.Single(moved);
        Assert.Equal(2, File.ReadAllLines(BridgesPath).Count(l => l.Trim().Length > 0));
    }

    [Fact]
    public void MoveRedBridges_KeepAtLeastAlreadyReached_MovesNothing()
    {
        File.WriteAllText(BridgesPath, BridgeA + "\n" + BridgeB + "\n");
        var moved = BridgeMaintenance.MoveRedBridges(BridgesPath, RedPath, [BridgeMaintenance.LineKey(BridgeA)!], keepAtLeast: 2);

        Assert.Empty(moved);
        Assert.False(File.Exists(RedPath));
    }

    [Fact]
    public void MoveRedBridges_NoKeys_NoOp()
    {
        File.WriteAllText(BridgesPath, BridgeA + "\n");
        Assert.Empty(BridgeMaintenance.MoveRedBridges(BridgesPath, RedPath, [], keepAtLeast: 0));
    }
}
