using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorVps.Core.Config;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;

namespace TorVps.Core.Services;

/// <summary>Owns the mihomo.exe child process: start/stop lifecycle and config hot-reload via its HTTP API.</summary>
public sealed class MihomoService : IMihomoService
{
    private const string MihomoSlotKey = "mihomo";
    private const int PortProbeTimeoutMs = 120;
    private const int ControllerReachableProbeTimeoutMs = 650;
    private static readonly TimeSpan PortPollInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan StartPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StartReadyTimeout = TimeSpan.FromMilliseconds(6000);
    private static readonly TimeSpan StopPortsClosedTimeout = TimeSpan.FromMilliseconds(6000);
    private static readonly TimeSpan HotReloadHttpTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessManager _processManager;
    private readonly ILogService _logService;
    private readonly IWindowsProxyService _windowsProxyService;
    private readonly HttpClient _httpClient;

    public MihomoService(IProcessManager processManager, ILogService logService, IWindowsProxyService windowsProxyService, HttpClient httpClient)
    {
        _processManager = processManager;
        _logService = logService;
        _windowsProxyService = windowsProxyService;
        _httpClient = httpClient;
    }

    public bool IsAlive => _processManager.IsAlive(MihomoSlotKey);

    public async Task<string> StartAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        var config = ConfigParser.ParseAppConfig(baseDirectory);

        var torReachable = await TcpPortCheck.IsOpenAsync(config.SocksHost, config.SocksPort, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false)
            || await TcpPortCheck.IsOpenAsync(config.ControlHost, config.ControlPort, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!torReachable)
        {
            const string message = "Tor is not running; mihomo start skipped";
            _logService.Append(baseDirectory, LogLevel.Warn, $"MIHOMO START blocked: {message}");
            throw new InvalidOperationException(message);
        }

        var alreadyRunning = await TcpPortCheck.IsOpenAsync("127.0.0.1", config.MixedPort, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false)
            || await TcpPortCheck.IsOpenAsync(config.ControllerHost, config.ControllerPort, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false)
            || _processManager.IsAlive(MihomoSlotKey);
        if (alreadyRunning)
        {
            _windowsProxyService.Enable(config.MixedPort);
            _logService.Append(baseDirectory, LogLevel.Info, "mihomo already running; System Proxy ON requested");
            return "mihomo already running";
        }

        Directory.CreateDirectory(config.MihomoProfileDir);
        _logService.Append(baseDirectory, LogLevel.Info, $"MIHOMO START exe={config.MihomoExePath} yaml={config.MihomoYamlPath} profile={config.MihomoProfileDir}");
        _processManager.Start(MihomoSlotKey, config.MihomoExePath, ["-d", config.MihomoProfileDir, "-f", config.MihomoYamlPath], baseDirectory, baseDirectory, "MIHOMO");

        try
        {
            var readyMessage = await WaitForMihomoReadyAsync(baseDirectory, config, cancellationToken).ConfigureAwait(false);
            _windowsProxyService.Enable(config.MixedPort);
            _logService.Append(baseDirectory, LogLevel.Info, $"mihomo ON verified; System Proxy ON requested; {readyMessage}");
            return readyMessage;
        }
        catch (Exception)
        {
            await _processManager.StopAsync(MihomoSlotKey, baseDirectory, "mihomo", cancellationToken).ConfigureAwait(false);
            await _processManager.KillByImageNameAsync("mihomo.exe", cancellationToken).ConfigureAwait(false);
            _windowsProxyService.Disable();
            throw;
        }
    }

    public async Task<string> StopAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        var config = ConfigParser.ParseAppConfig(baseDirectory);

        await _processManager.StopAsync(MihomoSlotKey, baseDirectory, "mihomo", cancellationToken).ConfigureAwait(false);
        await _processManager.KillByImageNameAsync("mihomo.exe", cancellationToken).ConfigureAwait(false);
        _windowsProxyService.Disable();

        var stopped = await WaitForPortsClosedAsync(config, cancellationToken).ConfigureAwait(false);
        if (stopped)
        {
            _logService.Append(baseDirectory, LogLevel.Info, "mihomo OFF verified; System Proxy OFF requested");
            return "mihomo stopped and ports closed";
        }

        _logService.Append(baseDirectory, LogLevel.Warn, "mihomo OFF requested but ports are still open after timeout");
        return "mihomo stop requested; ports still open";
    }

    public async Task<string> HotReloadRulesAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        _logService.Append(baseDirectory, LogLevel.Info, "MIHOMO RULES HOT UPDATE requested from mihomo.yaml");
        var config = ConfigParser.ParseAppConfig(baseDirectory);

        if (!File.Exists(config.MihomoYamlPath))
        {
            _logService.Append(baseDirectory, LogLevel.Warn, $"MIHOMO RULES HOT UPDATE skipped: source file not found {config.MihomoYamlPath}");
            return $"not applied: source file not found: {config.MihomoYamlPath}";
        }

        var controllerHost = NormalizeControllerHost(config.ControllerHost);
        if (!await TcpPortCheck.IsOpenAsync(controllerHost, config.ControllerPort, ControllerReachableProbeTimeoutMs, cancellationToken).ConfigureAwait(false))
        {
            _logService.Append(baseDirectory, LogLevel.Warn, $"MIHOMO RULES HOT UPDATE skipped: external-controller {controllerHost}:{config.ControllerPort} unavailable; mihomo not restarted");
            return $"not applied: external-controller {controllerHost}:{config.ControllerPort} is unavailable; mihomo was not restarted";
        }

        try
        {
            var detail = await PutConfigsReloadAsync(controllerHost, config, cancellationToken).ConfigureAwait(false);
            _logService.Append(baseDirectory, LogLevel.Info, $"MIHOMO RULES HOT UPDATE OK: rules applied without restarting mihomo; {detail}");
            return $"rules applied without mihomo restart; {detail}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logService.Append(baseDirectory, LogLevel.Warn, $"MIHOMO RULES HOT UPDATE FAILED: mihomo not restarted; {ex.Message}");
            return $"not applied; mihomo was not restarted: {ex.Message}";
        }
    }

    private async Task<string> PutConfigsReloadAsync(string controllerHost, AppConfig config, CancellationToken cancellationToken)
    {
        var url = $"http://{controllerHost}:{config.ControllerPort}/configs?force=true";
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(config.Secret))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Secret.Trim());

        var payload = JsonSerializer.Serialize(new { path = config.MihomoYamlPath });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var timeoutCts = new CancellationTokenSource(HotReloadHttpTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"mihomo controller returned HTTP {(int)response.StatusCode}: {Truncate(body, 500)}");

        return $"controller={controllerHost}:{config.ControllerPort}; PUT /configs?force=true; HTTP {(int)response.StatusCode}; source={config.MihomoYamlPath}";
    }

    private async Task<string> WaitForMihomoReadyAsync(string baseDirectory, AppConfig config, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(StartReadyTimeout);
        while (!timeoutCts.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var portsOpen = await TcpPortCheck.IsOpenAsync("127.0.0.1", config.MixedPort, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false)
                || await TcpPortCheck.IsOpenAsync(config.ControllerHost, config.ControllerPort, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (portsOpen)
                return "mihomo ready: ports open";

            if (!_processManager.IsAlive(MihomoSlotKey))
            {
                const string message = "mihomo exited before ready";
                _logService.Append(baseDirectory, LogLevel.Error, $"MIHOMO START FAIL {message}");
                throw new InvalidOperationException(message);
            }

            try
            {
                await Task.Delay(StartPollInterval, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        if (_processManager.IsAlive(MihomoSlotKey))
        {
            _logService.Append(baseDirectory, LogLevel.Warn, "MIHOMO START: process alive but ports are not ready yet");
            return "mihomo started: warming up";
        }

        const string failMessage = "mihomo did not stay alive";
        _logService.Append(baseDirectory, LogLevel.Error, $"MIHOMO START FAIL {failMessage}");
        throw new InvalidOperationException(failMessage);
    }

    private static async Task<bool> WaitForPortsClosedAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var mixedClosed = await WaitForPortStateAsync("127.0.0.1", config.MixedPort, shouldBeOpen: false, StopPortsClosedTimeout, cancellationToken).ConfigureAwait(false);
        var controllerClosed = await WaitForPortStateAsync(config.ControllerHost, config.ControllerPort, shouldBeOpen: false, StopPortsClosedTimeout, cancellationToken).ConfigureAwait(false);
        return mixedClosed && controllerClosed;
    }

    private static async Task<bool> WaitForPortStateAsync(string host, int port, bool shouldBeOpen, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (!timeoutCts.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TcpPortCheck.IsOpenAsync(host, port, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false) == shouldBeOpen)
                return true;

            try
            {
                await Task.Delay(PortPollInterval, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
        return await TcpPortCheck.IsOpenAsync(host, port, PortProbeTimeoutMs, cancellationToken).ConfigureAwait(false) == shouldBeOpen;
    }

    private static string NormalizeControllerHost(string host) => host.Trim() switch
    {
        "" or "0.0.0.0" or "::" or "[::]" => "127.0.0.1",
        var value => value,
    };

    private static string Truncate(string value, int maxChars) => value.Length <= maxChars ? value : value[..maxChars];
}
