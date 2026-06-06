/// Manages the system tray icon, its state (active/paused/monitor-missing),
/// and launches the Settings, History, and Reflection windows on demand.
/// Does not own window instances — they are created once and shown/hidden.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using FermataUI.ViewModels;
using FermataUI.Views;

namespace FermataUI.Services;

public class TrayIconManager : IDisposable
{
    private const int MonitorPollIntervalMs = 5_000;
    private const string MonitorExeName = "fermata-monitor.exe";

    private readonly ReflectionWindow _reflectionWindow;
    private readonly SettingsWindow _settingsWindow;
    private readonly HistoryWindow _historyWindow;
    private readonly IpcServer _ipc;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;

    private readonly System.Threading.Timer _monitorPollTimer;
    private TrayIcon? _trayIcon;

    public TrayIconManager(
        ReflectionViewModel reflectionVm,
        SettingsViewModel settingsVm,
        HistoryViewModel historyVm,
        IpcServer ipc,
        IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _ipc = ipc;
        _lifetime = lifetime;

        _reflectionWindow = new ReflectionWindow(reflectionVm);
        _settingsWindow   = new SettingsWindow(settingsVm);
        _historyWindow    = new HistoryWindow(historyVm);

        BuildTrayIcon();

        _monitorPollTimer = new System.Threading.Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateTrayState),
            null, TimeSpan.Zero, TimeSpan.FromMilliseconds(MonitorPollIntervalMs));
    }

    private void BuildTrayIcon()
    {
        var menu = new NativeMenu();

        var statusItem = new NativeMenuItem("● Fermata is active") { IsEnabled = false };
        menu.Add(statusItem);
        menu.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => _settingsWindow.Show();
        menu.Add(settingsItem);

        var historyItem = new NativeMenuItem("View History");
        historyItem.Click += (_, _) => _historyWindow.Show();
        menu.Add(historyItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => _lifetime.Shutdown();
        menu.Add(exitItem);

        var icon = new WindowIcon(
            AssetLoader.Open(new Uri("avares://FermataUI/Assets/Icons/TrayIcon.png")));

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Fermata",
            Menu = menu
        };
        _trayIcon.Clicked += (_, _) => _settingsWindow.Show();

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });
    }

    private void UpdateTrayState()
    {
        if (_trayIcon is null) return;
        var monitorRunning = System.Diagnostics.Process
            .GetProcessesByName(Path.GetFileNameWithoutExtension(MonitorExeName))
            .Length > 0;

        _trayIcon.ToolTipText = monitorRunning ? "Fermata — active" : "Fermata — monitor not detected";
    }

    public void Dispose()
    {
        _monitorPollTimer.Dispose();
        _trayIcon?.Dispose();
    }
}
