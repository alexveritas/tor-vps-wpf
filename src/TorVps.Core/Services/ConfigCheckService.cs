using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>Checks required files, executable paths, and key settings exist/are sane, for display on the Conf tab.</summary>
public sealed class ConfigCheckService : IConfigCheckService
{
    public IReadOnlyList<ConfigCheck> Run(AppConfig config) => new[]
    {
        FileCheck("torrc_manager.cfg", Path.Combine(config.BaseDirectory, "torrc_manager.cfg")),
        FileCheck("mihomo.yaml", config.MihomoYamlPath),
        BridgesCheck(config),
        FileCheck("tor.exe", config.TorExePath),
        FileCheck("lyrebird.exe", config.LyrebirdExePath),
        FileCheck("mihomo.exe", config.MihomoExePath),
        CookieCheck(config),
        OnionCheck(config),
        MonitorPasswordCheck(config),
    };

    private static ConfigCheck FileCheck(string name, string path) => File.Exists(path)
        ? new ConfigCheck { Name = name, State = HealthState.Ok, Detail = "found" }
        : new ConfigCheck { Name = name, State = HealthState.Error, Detail = "missing" };

    private static ConfigCheck BridgesCheck(AppConfig config)
    {
        var path = Path.Combine(config.BaseDirectory, "bridges.txt");
        if (!File.Exists(path))
            return new ConfigCheck { Name = "bridges.txt", State = HealthState.Error, Detail = "missing" };

        return config.BridgeCount > 0
            ? new ConfigCheck { Name = "bridges.txt", State = HealthState.Ok, Detail = $"{config.BridgeCount} bridges" }
            : new ConfigCheck { Name = "bridges.txt", State = HealthState.Warn, Detail = "0 bridges" };
    }

    private static ConfigCheck CookieCheck(AppConfig config)
    {
        var path = Path.Combine(config.BaseDirectory, "data", "control_auth_cookie");
        return File.Exists(path)
            ? new ConfigCheck { Name = "control_auth_cookie", State = HealthState.Ok, Detail = "found" }
            : new ConfigCheck { Name = "control_auth_cookie", State = HealthState.Warn, Detail = "not yet created" };
    }

    private static ConfigCheck OnionCheck(AppConfig config) => config.OnionHost != "—" && !string.IsNullOrWhiteSpace(config.OnionHost)
        ? new ConfigCheck { Name = "onion target", State = HealthState.Ok, Detail = $"{config.OnionHost}:{config.OnionPort}" }
        : new ConfigCheck { Name = "onion target", State = HealthState.Warn, Detail = "not configured" };

    private static ConfigCheck MonitorPasswordCheck(AppConfig config) => string.IsNullOrWhiteSpace(config.MonitorPassword)
        ? new ConfigCheck { Name = "monitor password", State = HealthState.Warn, Detail = "not configured" }
        : new ConfigCheck { Name = "monitor password", State = HealthState.Ok, Detail = "configured" };
}
