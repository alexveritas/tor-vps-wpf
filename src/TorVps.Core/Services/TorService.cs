using TorVps.Core.Config;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>Owns the tor.exe child process: torrc generation from torrc_manager.cfg, and start/stop lifecycle.</summary>
public sealed class TorService : ITorService
{
    private const string TorSlotKey = "tor";
    private const int AlreadyRunningProbeTimeoutMs = 100;

    private readonly IProcessManager _processManager;
    private readonly ILogService _logService;
    private readonly IMihomoService _mihomoService;

    public TorService(IProcessManager processManager, ILogService logService, IMihomoService mihomoService)
    {
        _processManager = processManager;
        _logService = logService;
        _mihomoService = mihomoService;
    }

    public bool IsAlive => _processManager.IsAlive(TorSlotKey);
    public DateTimeOffset? StartedAt { get; private set; }
    public int RestartCount { get; private set; }

    public async Task<string> StartAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        var config = ConfigParser.ParseAppConfig(baseDirectory);

        var alreadyRunning = _processManager.IsAlive(TorSlotKey)
            || await TcpPortCheck.IsOpenAsync(config.SocksHost, config.SocksPort, AlreadyRunningProbeTimeoutMs, cancellationToken).ConfigureAwait(false);
        if (alreadyRunning)
        {
            _logService.Append(baseDirectory, LogLevel.Info, "Tor already running");
            return "tor already running";
        }

        var torrcConfig = ConfigParser.ParseTorrcConfig(baseDirectory);
        var torrcPath = TorrcGenerator.GenerateToFile(baseDirectory, torrcConfig);

        if (StartedAt is not null)
            RestartCount++;
        StartedAt = DateTimeOffset.UtcNow;

        var pid = _processManager.Start(TorSlotKey, config.TorExePath, ["-f", torrcPath], baseDirectory, baseDirectory, "TOR");
        _logService.Append(baseDirectory, LogLevel.Info, "Tor ON requested");
        return $"started pid={pid}";
    }

    public async Task StopAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        // Domain rule: mihomo (+ system proxy) must be stopped and verified before Tor/lyrebird, never the other way around.
        await _mihomoService.StopAsync(baseDirectory, cancellationToken).ConfigureAwait(false);

        await _processManager.StopAsync(TorSlotKey, baseDirectory, "tor", cancellationToken).ConfigureAwait(false);
        await _processManager.KillByImageNameAsync("tor.exe", cancellationToken).ConfigureAwait(false);
        await _processManager.KillByImageNameAsync("lyrebird.exe", cancellationToken).ConfigureAwait(false);

        StartedAt = null;
        _logService.Append(baseDirectory, LogLevel.Info, "Tor OFF requested");
    }
}
