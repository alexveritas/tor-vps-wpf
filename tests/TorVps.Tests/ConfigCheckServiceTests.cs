using TorVps.Core.Models;
using TorVps.Core.Services;
using Xunit;

namespace TorVps.Tests;

public class ConfigCheckServiceTests : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "torvps-tests-" + Guid.NewGuid());
    private readonly ConfigCheckService _service = new();

    public ConfigCheckServiceTests() => Directory.CreateDirectory(_baseDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }

    private AppConfig BuildConfig(int bridgeCount = 0, string onionHost = "—", string monitorPassword = "") => new()
    {
        BaseDirectory = _baseDirectory,
        MihomoYamlPath = Path.Combine(_baseDirectory, "mihomo.yaml"),
        TorExePath = Path.Combine(_baseDirectory, "tor.exe"),
        LyrebirdExePath = Path.Combine(_baseDirectory, "lyrebird.exe"),
        MihomoExePath = Path.Combine(_baseDirectory, "mihomo.exe"),
        BridgeCount = bridgeCount,
        OnionHost = onionHost,
        MonitorPassword = monitorPassword,
    };

    [Fact]
    public void Run_ReportsErrorForMissingRequiredFiles()
    {
        var checks = _service.Run(BuildConfig());

        Assert.Contains(checks, c => c.Name == "torrc_manager.cfg" && c.State == HealthState.Error);
        Assert.Contains(checks, c => c.Name == "tor.exe" && c.State == HealthState.Error);
        Assert.Contains(checks, c => c.Name == "bridges.txt" && c.State == HealthState.Error);
    }

    [Fact]
    public void Run_ReportsOkWhenRequiredFilesArePresent()
    {
        File.WriteAllText(Path.Combine(_baseDirectory, "torrc_manager.cfg"), "[tor]\n");
        File.WriteAllText(Path.Combine(_baseDirectory, "tor.exe"), string.Empty);

        var checks = _service.Run(BuildConfig());

        Assert.Contains(checks, c => c.Name == "torrc_manager.cfg" && c.State == HealthState.Ok);
        Assert.Contains(checks, c => c.Name == "tor.exe" && c.State == HealthState.Ok);
    }

    [Fact]
    public void Run_WarnsWhenBridgesFileExistsButIsEmpty()
    {
        File.WriteAllText(Path.Combine(_baseDirectory, "bridges.txt"), string.Empty);

        var checks = _service.Run(BuildConfig(bridgeCount: 0));

        Assert.Contains(checks, c => c.Name == "bridges.txt" && c.State == HealthState.Warn);
    }

    [Fact]
    public void Run_OkWhenBridgesFilePresentAndNonEmpty()
    {
        File.WriteAllText(Path.Combine(_baseDirectory, "bridges.txt"), "obfs4 1.2.3.4:443 ABCDEF\n");

        var checks = _service.Run(BuildConfig(bridgeCount: 1));

        Assert.Contains(checks, c => c.Name == "bridges.txt" && c.State == HealthState.Ok);
    }

    [Fact]
    public void Run_WarnsWhenOnionTargetNotConfigured()
    {
        var checks = _service.Run(BuildConfig(onionHost: "—"));
        Assert.Contains(checks, c => c.Name == "onion target" && c.State == HealthState.Warn);
    }

    [Fact]
    public void Run_OkWhenOnionTargetConfigured()
    {
        var checks = _service.Run(BuildConfig(onionHost: "vps.onion"));
        Assert.Contains(checks, c => c.Name == "onion target" && c.State == HealthState.Ok);
    }

    [Fact]
    public void Run_WarnsWhenMonitorPasswordMissing()
    {
        var checks = _service.Run(BuildConfig(monitorPassword: ""));
        Assert.Contains(checks, c => c.Name == "monitor password" && c.State == HealthState.Warn);
    }

    [Fact]
    public void Run_OkWhenMonitorPasswordConfigured()
    {
        var checks = _service.Run(BuildConfig(monitorPassword: "secret"));
        Assert.Contains(checks, c => c.Name == "monitor password" && c.State == HealthState.Ok);
    }
}
