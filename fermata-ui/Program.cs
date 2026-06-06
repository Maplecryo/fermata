/// Application entry point. Builds and runs the Avalonia app with classic desktop lifetime.
/// Does not configure any windows here — see App.axaml.cs.
using Avalonia;
using FermataUI.Services;

namespace FermataUI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Catch any unhandled exception and write it to the log file before dying.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("UnhandledException", e.ExceptionObject?.ToString());

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("UnobservedTaskException", e.Exception?.ToString());
            e.SetObserved(); // prevent process termination
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal static class CrashLog
{
    private static readonly string LogPath =
        Path.Combine(ConfigService.DataDir, "fermata-crash.log");

    public static void Write(string kind, string? detail)
    {
        try
        {
            Directory.CreateDirectory(ConfigService.DataDir);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}:\n{detail}\n\n");
        }
        catch { /* logging must never crash the app */ }
    }
}
