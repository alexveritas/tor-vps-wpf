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
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(15);

    private readonly ITorService _torService;
    private readonly IMihomoService _mihomoService;
    private readonly ITorControlClient _torControlClient;
    private readonly ISocks5Probe _socksProbe;
    private readonly IVpsMonitorService _vpsMonitorService;
    private readonly ILogService _logService;
    private readonly IConfigCheckService _configCheckService;
    private readonly DispatcherTimer _refreshTimer;

    private readonly Queue<GraphHistoryPoint> _graphHistory = new();
    private readonly Queue<VpsHistoryPoint> _vpsHistory = new();

    private bool _refreshing;
    private bool _probeRunning;
    private DateTimeOffset _lastProbeStart = DateTimeOffset.MinValue;
    private bool _vpsFetchRunning;
    private DateTimeOffset _lastVpsFetchAt = DateTimeOffset.MinValue;
    private ProbeResult? _onionProbe;
    private ProbeResult? _chainProbe;
    private ProbeResult? _facebookProbe;
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
        IConfigCheckService configCheckService)
    {
        _torService = torService;
        _mihomoService = mihomoService;
        _torControlClient = torControlClient;
        _socksProbe = socksProbe;
        _vpsMonitorService = vpsMonitorService;
        _logService = logService;
        _configCheckService = configCheckService;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        UpdateConfTabStatus();
    }

    public void StartMonitoring()
    {
        if (_refreshTimer.IsEnabled)
            return;
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MihomoToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTunnelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMihomoCommand))]
    private bool _tunnelOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MihomoToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMihomoCommand))]
    private bool _mihomoOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TunnelToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(MihomoToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTunnelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMihomoCommand))]
    private bool _busy;

    [ObservableProperty]
    private bool _rulesUpdating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfTab))]
    private bool _isMainTab = true;

    public bool IsConfTab => !IsMainTab;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ServerMetrics _server = new();

    [ObservableProperty]
    private string _serverTooltip = "server metrics unavailable";

    [ObservableProperty]
    private string _confTabStatusText = "check";

    [ObservableProperty]
    private HealthState _confTabState = HealthState.Unknown;

    public ObservableCollection<CardState> Cards { get; } = new();
    public ObservableCollection<HealthChip> Health { get; } = new();
    public ObservableCollection<BridgeRecord> Bridges { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ConfigCheck> Checks { get; } = new();

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

    [RelayCommand]
    private void ShowMainTab() => IsMainTab = true;

    [RelayCommand]
    private void ShowConfTab() => IsMainTab = false;

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

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var logLines = _logService.ReadTail(BaseDirectory, 200);
            var config = ConfigParser.ParseAppConfig(BaseDirectory, logLines);

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
                if (recent100.Any(l => l.Contains("PROBE ONION OK") || l.Contains("PROBE TOR_FACEBOOK OK")))
                    bootstrap = 100.0;
            }

            if (torAlive)
                MaybeStartProbes(config, mihomoAlive);
            MaybeFetchVpsMetrics(config);

            var bridgesPath = Path.Combine(BaseDirectory, "bridges.txt");
            var bridgesText = File.Exists(bridgesPath) ? await File.ReadAllTextAsync(bridgesPath) : string.Empty;
            var failed = ConfigParser.ParseFailedFromLogs(logLines);
            var bridgeRecords = ConfigParser.ParseBridges(bridgesText, controlStatus.ActiveGuardFingerprints, failed);

            UpdateCardsAndHealth(torAlive, bootstrap, controlStatus, socksOpen, ctrlPortOpen, mihomoAlive, bridgeRecords, config, logLines);
            RecordGraphHistory(torAlive, controlStatus.DownMbit, controlStatus.UpMbit, config.MonitorIntervalSec);

            ReplaceAll(Bridges, bridgeRecords);
            ReplaceAll(LogLines, logLines);
            ReplaceAll(Checks, _configCheckService.Run(config));
            UpdateConfTabStatus();

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

    private static void ReplaceAll<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
