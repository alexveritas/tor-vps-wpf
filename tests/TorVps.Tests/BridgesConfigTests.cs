using TorVps.Core.Config;
using Xunit;

namespace TorVps.Tests;

public class BridgesConfigTests
{
    [Fact]
    public void ParseText_ReadsThresholdAndBridgesPerRun()
    {
        const string cfg =
            "[tor]\n" +
            "bridges_per_run = 12\n" +
            "[bridges]\n" +
            "red_move_min_failures = 900\n";

        var config = BridgesConfig.ParseText(cfg);

        Assert.Equal(900, config.RedMoveMinFailures);
        Assert.Equal(12, config.BridgesPerRun);
    }

    [Fact]
    public void ParseText_AppliesDefaultsWhenMissingOrInvalid()
    {
        var config = BridgesConfig.ParseText("[bridges]\nred_move_min_failures = zero\n");

        Assert.Equal(600, config.RedMoveMinFailures);
        Assert.Equal(8, config.BridgesPerRun);
    }
}
