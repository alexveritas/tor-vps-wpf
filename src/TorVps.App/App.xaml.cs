using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TorVps.App.ViewModels;
using TorVps.Core.Interfaces;
using TorVps.Core.Services;

namespace TorVps.App;

public partial class App : Application
{
    private const string BaseDirectory = @"C:\tor";
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IProcessManager, ProcessManager>();
        services.AddSingleton<IWindowsProxyService, WindowsProxyService>();
        services.AddSingleton<ISocks5Probe, Socks5Probe>();
        services.AddSingleton<ITorControlClient, TorControlClient>();
        services.AddSingleton<IBridgeAvailabilityMonitor, BridgeAvailabilityMonitor>();
        services.AddSingleton<IExitIpProbe, ExitIpProbe>();
        services.AddSingleton<TunnelWatchdog>();
        // The Glances endpoint is TLS 1.3-only; give the monitor its own managed-TLS client (Win10 Schannel can't do TLS 1.3).
        services.AddSingleton<IVpsMonitorService>(_ => new VpsMonitorService(ManagedTls.CreateHttpClient()));
        services.AddSingleton<IMihomoService, MihomoService>();
        services.AddSingleton<ITorService, TorService>();
        services.AddSingleton<IConfigCheckService, ConfigCheckService>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        _serviceProvider.GetRequiredService<ILogService>().Initialize(BaseDirectory);
        _serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Persist bridge availability counters so yellow/red stats survive restarts.
            _serviceProvider?.GetService<DashboardViewModel>()?.FlushBridgeStats();
        }
        catch (Exception)
        {
            // Best-effort on shutdown.
        }
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
