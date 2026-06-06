/// App entry point. Initialises DI, starts the tray icon, and wires up the IPC server.
/// Does not own any windows directly — window lifetime is managed by ViewModels.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FermataUI.Services;
using FermataUI.ViewModels;

namespace FermataUI;

public class App : Application
{
    private TrayIconManager? _trayIconManager;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // OnExplicitShutdown keeps Avalonia alive with no open windows,
            // which is correct for a menu-bar-only app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        // Hide from Dock. Deferred to Background priority so it fires after
        // Avalonia has finished all its own platform activation — any earlier
        // and Avalonia resets the policy back to Regular.
        if (OperatingSystem.IsMacOS())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                MacOS.SetAccessoryActivationPolicy,
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
