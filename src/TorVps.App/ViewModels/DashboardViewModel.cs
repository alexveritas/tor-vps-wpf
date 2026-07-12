using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorVps.Core.Config;
using TorVps.Core.Interfaces;
using TorVps.Core.Models;
using TorVps.Core.Services;

namespace TorVps.App.ViewModels;

/// <summary>Drives the dashboard: polls Core services on a 1Hz loop and exposes UI-bound state, mirroring the reference app's load_state/collect_backend_sample.</summary>
public partial class DashboardViewModel : ObservableObject
{
    private const string BaseDirectory = @"C:\tor";
    private const int GraphHistoryCapacity = 520;
    private const int VpsGraphCapacity = 260;
    private static readonly TimeSpan BridgeStatSaveInterval = TimeSpan.FromSeconds(30);

    private static string BridgesPath => Path.Combine(BaseDirectory, "bridges.txt");
    private static string RedBridgesPath => Path.Combine(BaseDirectory, "bridges-red.txt");
    private static string BridgeStatsPath => Path.Combine(BaseDirectory, "bridges-autostat.info");

    private readonly ITorService _torService;
    private readonly IMihomoService _mihomoService;
    private readonly ITorControlClient _torControlClient;
    private readonly ISocks5Probe _socksProbe;
    private readonly IVpsMonitorService _vpsMonitorService;
    private readonly ILogService _logService;
    private readonly IConfigCheckService _configCheckService;
    private readonly IBridgeAvailabilityMonitor _bridgeMonitor;
    private readonly IExitIpProbe _exitIpProbe;
    private readonly TunnelWatchdog _tunnelWatchdog;
    private readonly DispatcherTimer _refreshTimer;

    private readonly Queue<GraphHistoryPoint> _graphHistory = new();
    private readonly Queue<VpsHistoryPoint> _vpsHistory = new();

    private bool _refreshing;
    private bool _probeRunning;
    private DateTimeOffset _lastProbeStart = DateTimeOffset.MinValue;
    private bool _vpsFetchRunning;
    private DateTimeOffset _lastVpsFetchAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBridgeStatSaveAt = DateTimeOffset.MinValue;
    private ProbeResult? _onionProbe;
    private ProbeResult? _chainProbe;
    private ExitIpResult? _torExitResult; // "Tor → i-net": our exit IP via raw Tor (leak-checked against the ISP IP)
    private string _providerIp = string.Empty; // real ISP egress IP (direct request); the leak baseline
    private DateTimeOffset _providerIpAt = DateTimeOffset.MinValue;
    private ServerMetrics? _lastGoodServerMetrics;
    private DateTimeOffset _lastGoodServerMetricsAt;
    private double _speedScaleMax = 1;
    private double _pingScaleMax = 250;
    private double _vpsScaleMax = 1;

    public DashboardViewModel(
        ITorService torService,
        IMihomoService mihomoService,
        ITorControlClient torControlClient,
        ISocks5Probe socksProbe,
        IVpsMonitorService vpsMonitorService,
        ILogService logService,
        IConfigCheckService configCheckService,
        IBridgeAvailabilityMonitor bridgeMonitor,
        IExitIpProbe exitIpProbe,
        TunnelWatchdog tunnelWatchdog)
    {
        _torService = torService;
        _mihomoService = mihomoService;
        _torControlClient = torControlClient;
        _socksProbe = socksProbe;
        _vpsMonitorService = vpsMonitorService;
        _logService = logService;
        _configCheckService = configCheckService;
        _bridgeMonitor = bridgeMonitor;
        _exitIpProbe = exitIpProbe;
        _tunnelWatchdog = tunnelWatchdog;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        UpdateConfTabStatus();
    }

    public void StartMonitoring()
    {
        if (_refreshTimer.IsEnabled)
            return;
        LoadUiSettings();
        InitializeBridgeData();
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Startup upkeep: drop full-duplicate lines from bridges.txt, then seed the availability monitor
    /// with the counters persisted by the previous run.</summary>
    private void InitializeBridgeData()
    {
        try
        {
            var removed = BridgeMaintenance.RemoveDuplicateLines(BridgesPath);
            foreach (var line in removed)
                _logService.Append(BaseDirectory, LogLevel.Warn, $"BRIDGES duplicate removed from bridges.txt: {line}");
            if (removed.Count > 0)
                _logService.Append(BaseDirectory, LogLevel.Info, $"BRIDGES dedupe: {removed.Count} duplicate line(s) removed from bridges.txt");

            var stats = BridgeStatStore.Load(BridgeStatsPath);
            if (stats.Count > 0)
            {
                _bridgeMonitor.Seed(stats);
                _logService.Append(BaseDirectory, LogLevel.Info, $"BRIDGES availability stats loaded for {stats.Count} bridge(s) from bridges-autostat.info");
            }
        }
        catch (Exception ex)
        {
            _logService.Append(BaseDirectory, LogLevel.Warn, $"BRIDGES init failed: {ex.Message}");
        }
    }

    /// <summary>Persists the bridge availability counters immediately. Called on app exit.</summary>
    public void FlushBridgeStats()
    {
        try
        {
            BridgeStatStore.Save(BridgeStatsPath, _bridgeMonitor.Snapshot());
        }
        catch (Exception)
        {
            // Best-effort on shutdown.
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MihomoToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTunnelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMihomoCommand))]
    private bool _tunnelOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MihomoToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(VpsTunnelToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMihomoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleVpsTunnelCommand))]
    private bool _mihomoOn;

    /// <summary>Whether routed traffic exits via the onion VPS (mihomo selector on the tunnel proxy) or via
    /// plain Tor with a random exit. Mirrors the live mihomo selector state each refresh.</summary>
    [ObservableProperty]
    private bool _vpsTunnelOn = true;

    /// <summary>"Graphic's:" toggle. ON — both graph panels are shown; OFF — they are replaced by one large
    /// VPS CPU/RAM/net block and the graph geometry building is skipped (history still accumulates, so toggling
    /// back on shows continuous graphs). Persisted as [ui] graphics in torrc_manager.cfg.</summary>
    [ObservableProperty]
    private bool _graphicsOn = true;

    /// <summary>Suppresses persisting GraphicsOn while it is being loaded FROM the cfg.</summary>
    private bool _uiSettingsLoaded;

    partial void OnGraphicsOnChanged(bool value)
    {
        if (!_uiSettingsLoaded)
            return;
        try
        {
            IniWriter.SetValueInFile(Path.Combine(BaseDirectory, "torrc_manager.cfg"), "ui", "graphics", value ? "true" : "false");
            _logService.Append(BaseDirectory, LogLevel.Info, $"UI graphics toggle saved: [ui] graphics = {(value ? "true" : "false")}");
        }
        catch (Exception ex)
        {
            _logService.Append(BaseDirectory, LogLevel.Warn, $"UI failed to save graphics toggle to torrc_manager.cfg: {ex.Message}");
        }
    }

    /// <summary>Restores the "Graphic's:" toggle from torrc_manager.cfg (default: on).</summary>
    private void LoadUiSettings()
    {
        try
        {
            var cfgPath = Path.Combine(BaseDirectory, "torrc_manager.cfg");
            var cfgText = File.Exists(cfgPath) ? File.ReadAllText(cfgPath) : string.Empty;
            var raw = ConfigParser.FirstIniValue(cfgText, "ui", "graphics", "true").Trim().ToLowerInvariant();
            GraphicsOn = raw is "true" or "1" or "yes" or "on";
        }
        catch (Exception)
        {
            GraphicsOn = true;
        }
        finally
        {
            _uiSettingsLoaded = true;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TunnelToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(MihomoToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(VpsTunnelToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTunnelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMihomoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleVpsTunnelCommand))]
    private bool _busy;

    [ObservableProperty]
    private bool _rulesUpdating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainTab))]
    [NotifyPropertyChangedFor(nameof(IsBridgesTab))]
    [NotifyPropertyChangedFor(nameof(IsConfigTab))]
    private DashboardTab _selectedTab = DashboardTab.Main;

    public bool IsMainTab => SelectedTab == DashboardTab.Main;
    public bool IsBridgesTab => SelectedTab == DashboardTab.Bridges;
    public bool IsConfigTab => SelectedTab == DashboardTab.Config;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ServerMetrics _server = new();

    [ObservableProperty]
    private string _confTabStatusText = "check";

    [ObservableProperty]
    private HealthState _confTabState = HealthState.Unknown;

    [ObservableProperty] private int _bridgeGreenCount;
    [ObservableProperty] private int _bridgeYellowCount;
    [ObservableProperty] private int _bridgeRedCount;
    [ObservableProperty] private int _bridgeGrayCount;

    public ObservableCollection<CardState> Cards { get; } = new();
    public ObservableCollection<HealthChip> Health { get; } = new();
    public ObservableCollection<BridgeRecord> Bridges { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ConfigCheck> Checks { get; } = new();
    public ObservableCollection<ConfigCheckGroup> CheckGroups { get; } = new();

    [ObservableProperty]
    private PointCollection _downAreaPoints = new();

    [ObservableProperty]
    private PointCollection _upAreaPoints = new();

    [ObservableProperty]
    private PointCollection _pingAreaPoints = new();

    [ObservableProperty]
    private PointCollection _vpsDownAreaPoints = new();

    [ObservableProperty]
    private PointCollection _vpsUpAreaPoints = new();

    [ObservableProperty]
    private PointCollection _downFillPoints = new();

    [ObservableProperty]
    private PointCollection _upFillPoints = new();

    [ObservableProperty]
    private PointCollection _pingFillPoints = new();

    [ObservableProperty]
    private PointCollection _vpsDownFillPoints = new();

    [ObservableProperty]
    private PointCollection _vpsUpFillPoints = new();

    [ObservableProperty] private string _downPeakText = "0.00";
    [ObservableProperty] private string _downCurrentText = "0.00";
    [ObservableProperty] private string _downAverageText = "0.00";
    [ObservableProperty] private string _upPeakText = "0.00";
    [ObservableProperty] private string _upCurrentText = "0.00";
    [ObservableProperty] private string _upAverageText = "0.00";
    [ObservableProperty] private string _pingPeakText = "— ms";
    [ObservableProperty] private string _pingCurrentText = "— ms";
    [ObservableProperty] private string _pingAverageText = "— ms";

    [ObservableProperty] private string _vpsCpuText = "0%";
    [ObservableProperty] private string _vpsRamText = "0%";
    [ObservableProperty] private string _vpsDownCurrentText = "0.00";
    [ObservableProperty] private string _vpsDownPeakText = "0.00";
    [ObservableProperty] private string _vpsUpCurrentText = "0.00";
    [ObservableProperty] private string _vpsUpPeakText = "0.00";

    public bool TunnelToggleEnabled => !Busy;

    public bool MihomoToggleEnabled => !Busy && (TunnelOn || MihomoOn);

    public bool VpsTunnelToggleEnabled => !Busy && MihomoOn;

    [RelayCommand(CanExecute = nameof(TunnelToggleEnabled))]
    private async Task ToggleTunnelAsync()
    {
        Busy = true;
        try
        {
            if (TunnelOn)
                await _torService.StopAsync(BaseDirectory);
            else
                await _torService.StartAsync(BaseDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            await RefreshAsync();
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(MihomoToggleEnabled))]
    private async Task ToggleMihomoAsync()
    {
        Busy = true;
        try
        {
            if (MihomoOn)
                await _mihomoService.StopAsync(BaseDirectory);
            else
                await _mihomoService.StartAsync(BaseDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            await RefreshAsync();
            Busy = false;
        }
    }

    /// <summary>Switches the mihomo selector between the onion-VPS tunnel proxy and plain Tor (random exit),
    /// live via the controller API — no restarts. The watchdog only runs while the tunnel is ON.</summary>
    [RelayCommand(CanExecute = nameof(VpsTunnelToggleEnabled))]
    private async Task ToggleVpsTunnelAsync()
    {
        Busy = true;
        try
        {
            var watchdogConfig = TunnelWatchdogConfig.Parse(BaseDirectory);
            var turningOn = !VpsTunnelOn;
            var target = turningOn ? watchdogConfig.VpsTunnelProxy : watchdogConfig.VpsTorProxy;
            if (await _mihomoService.SelectProxyAsync(BaseDirectory, watchdogConfig.VpsTunnelGroup, target))
            {
                VpsTunnelOn = turningOn;
                _exitIpResult = null;
                _lastExitCheckOk = true;
                _tunnelWatchdog.Reset();
                _lastWatchdogTickAt = DateTimeOffset.MinValue; // re-check promptly after switching back on
                _logService.Append(BaseDirectory, LogLevel.Info, turningOn
                    ? $"VPS-TUNNEL ON: {watchdogConfig.VpsTunnelGroup} → {watchdogConfig.VpsTunnelProxy} (exit via VPS {watchdogConfig.ExitIp})"
                    : $"VPS-TUNNEL OFF: {watchdogConfig.VpsTunnelGroup} → {watchdogConfig.VpsTorProxy} (random Tor exit; watchdog paused)");
            }
            else
            {
                StatusMessage = "failed to switch VPS tunnel (see log)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            await RefreshAsync();
            Busy = false;
        }
    }

    [RelayCommand]
    private void ShowMainTab() => SelectedTab = DashboardTab.Main;

    [RelayCommand]
    private void ShowBridgesTab() => SelectedTab = DashboardTab.Bridges;

    [RelayCommand]
    private void ShowConfigTab() => SelectedTab = DashboardTab.Config;

    [RelayCommand]
    private async Task UpdateRulesAsync()
    {
        RulesUpdating = true;
        try
        {
            StatusMessage = await _mihomoService.HotReloadRulesAsync(BaseDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            RulesUpdating = false;
        }
    }

    /// <summary>Re-reads the whole bridge list from bridges.txt (after manual edits), deduping on the way.
    /// Accumulated availability stats are kept — they are keyed by bridge line.</summary>
    [RelayCommand]
    private async Task ReloadBridgesAsync()
    {
        _logService.Append(BaseDirectory, LogLevel.Info, "BRIDGES reload from bridges.txt requested");
        try
        {
            var removed = BridgeMaintenance.RemoveDuplicateLines(BridgesPath);
            foreach (var line in removed)
                _logService.Append(BaseDirectory, LogLevel.Warn, $"BRIDGES duplicate removed from bridges.txt: {line}");
        }
        catch (Exception ex)
        {
            _logService.Append(BaseDirectory, LogLevel.Warn, $"BRIDGES reload failed: {ex.Message}");
        }
        await RefreshAsync();
        _logService.Append(BaseDirectory, LogLevel.Info, $"BRIDGES reloaded: {Bridges.Count} bridge(s) in the list");
    }

    /// <summary>Forces an immediate re-read + validation of all config files (they are also re-read every
    /// refresh tick, so edits apply without restarting the app) and logs the check summary.</summary>
    [RelayCommand]
    private async Task ReloadConfigAsync()
    {
        _logService.Append(BaseDirectory, LogLevel.Info, "CONFIG reload requested: re-reading torrc_manager.cfg / mihomo.yaml / bridges.txt");
        await RefreshAsync();
        var errors = Checks.Count(c => c.State == HealthState.Error);
        var warns = Checks.Count(c => c.State == HealthState.Warn);
        var summary = $"CONFIG reloaded and checked: {Checks.Count} checks, {errors} error(s), {warns} warning(s)";
        _logService.Append(BaseDirectory, errors > 0 ? LogLevel.Warn : LogLevel.Info, summary);
        StatusMessage = summary;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var logLines = _logService.ReadTail(BaseDirectory, 200);
            var config = ConfigParser.ParseAppConfig(BaseDirectory, logLines);
            // Re-read every tick on purpose: edits to torrc_manager.cfg apply without restarting the app.
            var watchdogConfig = TunnelWatchdogConfig.Parse(BaseDirectory);
            var bridgesConfig = BridgesConfig.Parse(BaseDirectory);

            var cookiePath = Path.Combine(BaseDirectory, "data", "control_auth_cookie");
            var controlStatus = await _torControlClient.GetStatusAsync(config.ControlHost, config.ControlPort, cookiePath);

            var socksOpen = await TcpPortCheck.IsOpenAsync(config.SocksHost, config.SocksPort, 80);
            var ctrlPortOpen = await TcpPortCheck.IsOpenAsync(config.ControlHost, config.ControlPort, 80);
            var torAlive = _torService.IsAlive || socksOpen || ctrlPortOpen || controlStatus.ControlAuthOk;

            var mixedOpen = await TcpPortCheck.IsOpenAsync("127.0.0.1", config.MixedPort, 80);
            var controllerOpen = await TcpPortCheck.IsOpenAsync(config.ControllerHost, config.ControllerPort, 80);
            var mihomoAlive = _mihomoService.IsAlive || mixedOpen || controllerOpen;

            var bootstrap = controlStatus.BootstrapPercent;
            if (bootstrap < 100.0 && torAlive)
            {
                var recent100 = logLines.Skip(Math.Max(0, logLines.Count - 100));
                // Any reachable Tor exit (OK or just slow) proves Tor got to the internet.
                if (recent100.Any(l => l.Contains("PROBE ONION OK") || l.Contains("PROBE TOR_EXIT OK") || l.Contains("PROBE TOR_EXIT SLOW")))
                    bootstrap = 100.0;
            }

            if (torAlive)
                MaybeStartProbes(config, watchdogConfig, mihomoAlive);
            MaybeFetchVpsMetrics(config);

            // Mirror the live mihomo selector so the VPS-tunnel toggle reflects reality (e.g. after a mihomo
            // restart the group reverts to its first member = tunnel ON).
            if (mihomoAlive)
            {
                var selected = await _mihomoService.GetSelectedProxyAsync(BaseDirectory, watchdogConfig.VpsTunnelGroup);
                if (selected is not null)
                    VpsTunnelOn = string.Equals(selected, watchdogConfig.VpsTunnelProxy, StringComparison.Ordinal);
            }

            MaybeRunWatchdog(watchdogConfig, config, torAlive, mihomoAlive);

            var bridgesPath = Path.Combine(BaseDirectory, "bridges.txt");
            var bridgesText = File.Exists(bridgesPath) ? await File.ReadAllTextAsync(bridgesPath) : string.Empty;
            // Failed = guards Tor reports as down/unusable, plus anything flagged failing in the logs.
            var failed = ConfigParser.ParseFailedFromLogs(logLines).Concat(controlStatus.DownGuardFingerprints).ToList();
            var bridgeRecords = ConfigParser.ParseBridges(bridgesText, controlStatus.ActiveGuardFingerprints, failed);

            // Only fold bridge reachability into the running stats when Tor is fully working and its guard status is
            // readable — otherwise a down/bootstrapping Tor would wrongly mark every bridge as unreachable.
            var torHealthy = torAlive && bootstrap >= 100.0 && controlStatus.ControlAuthOk;
            var bridgeReport = _bridgeMonitor.Update(bridgeRecords, torHealthy);
            BridgeGreenCount = bridgeReport.GreenCount;
            BridgeYellowCount = bridgeReport.YellowCount;
            BridgeRedCount = bridgeReport.RedCount;
            BridgeGrayCount = bridgeReport.GrayCount;

            MaintainBridgeFiles(bridgesConfig, torHealthy, bridgeReport);

            UpdateCardsAndHealth(torAlive, bootstrap, controlStatus, socksOpen, ctrlPortOpen, mihomoAlive, bridgeReport, config, watchdogConfig, logLines);
            RecordGraphHistory(torAlive, controlStatus.DownMbit, controlStatus.UpMbit, config.MonitorIntervalSec);

            ReplaceAll(Bridges, bridgeReport.Bridges);
            ReplaceAll(LogLines, logLines);
            ReplaceAll(Checks, _configCheckService.Run(config));
            UpdateConfTabStatus();
            UpdateCheckGroups();

            TunnelOn = torAlive;
            MihomoOn = mihomoAlive;
        }
        catch (Exception ex)
        {
            StatusMessage = $"refresh failed: {ex.Message}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Runtime bridge upkeep, only while Tor is healthy: quarantine bridges the monitor has confirmed
    /// as never-reachable into bridges-red.txt, and periodically persist the availability counters.</summary>
    private void MaintainBridgeFiles(BridgesConfig bridgesConfig, bool torHealthy, BridgeAvailabilityReport report)
    {
        if (!torHealthy)
            return;

        try
        {
            if (report.RedCount > 0)
            {
                var confirmed = _bridgeMonitor.ConfirmedRed(bridgesConfig.RedMoveMinFailures);
                if (confirmed.Count > 0)
                {
                    var moved = BridgeMaintenance.MoveRedBridges(BridgesPath, RedBridgesPath, confirmed, bridgesConfig.BridgesPerRun);
                    foreach (var line in moved)
                        _logService.Append(BaseDirectory, LogLevel.Warn, $"BRIDGES never-reachable bridge moved to bridges-red.txt (red_move_min_failures={bridgesConfig.RedMoveMinFailures}): {line}");
                }
            }

            if (DateTimeOffset.UtcNow - _lastBridgeStatSaveAt >= BridgeStatSaveInterval)
            {
                _lastBridgeStatSaveAt = DateTimeOffset.UtcNow;
                BridgeStatStore.Save(BridgeStatsPath, _bridgeMonitor.Snapshot());
            }
        }
        catch (Exception ex)
        {
            _logService.Append(BaseDirectory, LogLevel.Warn, $"BRIDGES maintenance failed: {ex.Message}");
        }
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
