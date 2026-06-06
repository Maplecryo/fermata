/// App entry point. Initialises DI, starts the tray icon, and wires up the IPC server.
/// Does not own any windows directly — window lifetime is managed by ViewModels.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FermataUI.Services;
using FermataUI.ViewModels;
using FermataUI.Views;

namespace FermataUI;

public class App : Application
{
    private TrayIconManager? _trayIconManager;

    // Invisible anchor window — keeps Avalonia's lifetime alive on macOS when
    // all visible windows are hidden. Never shown to the user.
    private Window? _anchorWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Keep the process alive on macOS even when no windows are visible.
            // Positioned off-screen at 1x1px with no decorations — never seen by the user.
            _anchorWindow = new Window
            {
                Width = 1, Height = 1,
                ShowInTaskbar = false,
                SystemDecorations = SystemDecorations.None,
                Opacity = 0,
                Position = new Avalonia.PixelPoint(-9999, -9999),
                Topmost = false,
            };
            _anchorWindow.Closing += (_, e) => e.Cancel = true; // never allow it to close
            _anchorWindow.Show();

            var configService = new ConfigService();
            var dbService = new DatabaseService();
            var ipcServer = new IpcServer();

            var reflectionVm = new ReflectionViewModel(ipcServer, dbService, configService);
            var settingsVm = new SettingsViewModel(configService);
            var historyVm = new HistoryViewModel(dbService);

            _trayIconManager = new TrayIconManager(
                reflectionVm, settingsVm, historyVm, ipcServer, desktop);

            ipcServer.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
