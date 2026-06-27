using System.Reflection;
using System.Windows;
using System.Windows.Input;
using TorVps.App.ViewModels;

namespace TorVps.App;

public partial class MainWindow : Window
{
    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.StartMonitoring();

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        Title = $"TorVps v{version}";
        VersionText.Text = $"Tor-vps v{version}";
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
