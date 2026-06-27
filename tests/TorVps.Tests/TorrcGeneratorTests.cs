using TorVps.Core.Config;
using TorVps.Core.Models;
using Xunit;

namespace TorVps.Tests;

public class TorrcGeneratorTests
{
    private static TorrcConfig BuildConfig() => new()
    {
        DataDirectory = @"C:\tor\data",
        GeoIpFile = @"C:\tor\data\geoip",
        GeoIpv6File = @"C:\tor\data\geoip6",
        LyrebirdExePath = @"C:\tor\pluggable_transports\lyrebird.exe",
        OutputPath = @"C:\tor\torrc.generated",
        SocksHost = "127.0.0.1",
        SocksPort = 9150,
        ControlHost = "127.0.0.1",
        ControlPort = 9151,
        LogSeverity = "notice",
        CircuitBuildTimeout = 60,
        MaxClientCircuitsPending = 32,
    };

    [Fact]
    public void Generate_SlashifiesBackslashPaths()
    {
        var output = TorrcGenerator.Generate(BuildConfig(), []);

        Assert.Contains("DataDirectory C:/tor/data", output);
        Assert.Contains("ClientTransportPlugin obfs4 exec C:/tor/pluggable_transports/lyrebird.exe", output);
    }

    [Fact]
    public void Generate_IncludesSocksAndControlPorts()
    {
        var output = TorrcGenerator.Generate(BuildConfig(), []);

        Assert.Contains("SocksPort 127.0.0.1:9150", output);
        Assert.Contains("ControlPort 127.0.0.1:9151", output);
        Assert.Contains("CookieAuthentication 1", output);
    }

    [Fact]
    public void Generate_SkipsBlankAndCommentBridgeLines()
    {
        var output = TorrcGenerator.Generate(BuildConfig(), ["", "   ", "# a comment", "obfs4 1.2.3.4:443 ABCDEF"]);

        Assert.DoesNotContain("# a comment", output);
        Assert.Contains("Bridge obfs4 1.2.3.4:443 ABCDEF", output);
    }

    [Fact]
    public void Generate_DoesNotDoublePrefixLinesAlreadyStartingWithBridge()
    {
        var output = TorrcGenerator.Generate(BuildConfig(), ["bridge obfs4 1.2.3.4:443 ABCDEF"]);

        Assert.Contains("bridge obfs4 1.2.3.4:443 ABCDEF", output);
        Assert.DoesNotContain("Bridge bridge", output);
    }

    [Fact]
    public void GenerateToFile_WritesFileAndCreatesParentDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "torvps-tests-" + Guid.NewGuid());
        try
        {
            var config = BuildConfig() with { OutputPath = Path.Combine(tempRoot, "nested", "torrc.generated") };

            var returnedPath = TorrcGenerator.GenerateToFile(config, ["obfs4 1.2.3.4:443 ABCDEF"]);

            Assert.Equal(config.OutputPath, returnedPath);
            Assert.True(File.Exists(returnedPath));
            Assert.Contains("Bridge obfs4 1.2.3.4:443 ABCDEF", File.ReadAllText(returnedPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GenerateToFile_FromBaseDirectory_ReadsBridgesTxtWhenPresent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "torvps-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempRoot);
        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "bridges.txt"), "obfs4 1.2.3.4:443 ABCDEF\n");
            var config = BuildConfig() with { OutputPath = Path.Combine(tempRoot, "torrc.generated") };

            TorrcGenerator.GenerateToFile(tempRoot, config);

            Assert.Contains("Bridge obfs4 1.2.3.4:443 ABCDEF", File.ReadAllText(config.OutputPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
