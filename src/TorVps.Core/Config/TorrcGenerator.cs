using TorVps.Core.Models;

namespace TorVps.Core.Config;

/// <summary>Renders a torrc file from a <see cref="TorrcConfig"/> plus the lines of bridges.txt.</summary>
public static class TorrcGenerator
{
    /// <summary>Builds the torrc file contents. Pure and side-effect free.</summary>
    public static string Generate(TorrcConfig config, IEnumerable<string> bridgeLines)
    {
        var lines = new List<string>
        {
            $"DataDirectory {Slashify(config.DataDirectory)}",
            $"GeoIPFile {Slashify(config.GeoIpFile)}",
            $"GeoIPv6File {Slashify(config.GeoIpv6File)}",
            $"Log {config.LogSeverity} stdout",
            "",
            "UseBridges 1",
            $"ClientTransportPlugin obfs4 exec {Slashify(config.LyrebirdExePath)}",
            "",
            $"SocksPort {config.SocksHost}:{config.SocksPort}",
            $"ControlPort {config.ControlHost}:{config.ControlPort}",
            "CookieAuthentication 1",
            "",
            "LearnCircuitBuildTimeout 1",
            $"CircuitBuildTimeout {config.CircuitBuildTimeout}",
            $"MaxClientCircuitsPending {config.MaxClientCircuitsPending}",
            "",
        };

        foreach (var raw in bridgeLines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            lines.Add(trimmed.StartsWith("bridge ", StringComparison.OrdinalIgnoreCase) ? trimmed : $"Bridge {trimmed}");
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Generates the torrc text and writes it to config.OutputPath, creating the parent directory as needed. Returns the output path.</summary>
    public static string GenerateToFile(TorrcConfig config, IEnumerable<string> bridgeLines)
    {
        var outputDirectory = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(config.OutputPath, Generate(config, bridgeLines));
        return config.OutputPath;
    }

    /// <summary>Reads bridges.txt under baseDirectory, then generates and writes the torrc file. Returns the output path.</summary>
    public static string GenerateToFile(string baseDirectory, TorrcConfig config)
    {
        var bridgesPath = Path.Combine(baseDirectory, "bridges.txt");
        var bridgeLines = File.Exists(bridgesPath) ? File.ReadAllLines(bridgesPath) : Array.Empty<string>();
        return GenerateToFile(config, bridgeLines);
    }

    private static string Slashify(string path) => path.Replace('\\', '/');
}
