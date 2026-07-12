using TorVps.Core.Models;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class LogLineClassifierTests
{
    [Theory]
    [InlineData("1751970000 [INFO] tor-t.log initialized", LogLevel.Info)]
    [InlineData("1751970000 [WARN] MIHOMO START: process alive but ports are not ready yet", LogLevel.Warn)]
    [InlineData("1751970000 [ERROR] WATCHDOG failed: boom", LogLevel.Error)]
    [InlineData("1751970000 [DEBUG] verbose detail", LogLevel.Debug)]
    public void Classify_UsesTheAppLevelTag(string line, LogLevel expected)
    {
        Assert.Equal(expected, LogLineClassifier.Classify(line));
    }

    [Fact]
    public void Classify_TorChildOutput_EmbeddedSeverityUpgradesInfoLines()
    {
        Assert.Equal(LogLevel.Warn, LogLineClassifier.Classify("1751970000 [INFO] [TOR] Jul 08 [warn] Proxy Client: unable to connect"));
        Assert.Equal(LogLevel.Error, LogLineClassifier.Classify("1751970000 [INFO] [TOR] Jul 08 [err] catastrophic failure"));
        Assert.Equal(LogLevel.Info, LogLineClassifier.Classify("1751970000 [INFO] [TOR] Jul 08 [notice] Bootstrapped 100%"));
    }

    [Fact]
    public void Classify_PlainWordsWithoutBrackets_DoNotTrigger()
    {
        Assert.Equal(LogLevel.Info, LogLineClassifier.Classify("1751970000 [INFO] CONFIG reloaded and checked: 12 checks, 0 error(s), 0 warning(s)"));
    }

    [Fact]
    public void Classify_UntaggedLine_DefaultsToInfo()
    {
        Assert.Equal(LogLevel.Info, LogLineClassifier.Classify("free-form line with no tag"));
    }
}
