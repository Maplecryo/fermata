/// Reads, writes, and watches friction.json in %APPDATA%\Fermata\.
/// Does not validate app names — that is the caller's responsibility.
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FermataUI.Services;

public class FermataConfig
{
    [JsonPropertyName("delay_seconds")] public int DelaySeconds { get; set; } = 30;
    [JsonPropertyName("require_journal")] public bool RequireJournal { get; set; } = false;
    [JsonPropertyName("apps")] public List<string> Apps { get; set; } = new();
}

public class ConfigService : IDisposable
{
    public static readonly string DataDir = GetDataDir();

    private static string GetDataDir()
    {
        // Windows: %APPDATA%\Fermata
        // macOS:   ~/Library/Application Support/Fermata
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(appData, "Fermata");
    }

    private static readonly string ConfigPath = Path.Combine(DataDir, "fermata.json");
    private static readonly string DefaultConfigPath = Path.Combine(DataDir, "fermata.default.json");

    private readonly FileSystemWatcher _watcher;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public event Action? ConfigChanged;

    public FermataConfig Current { get; private set; } = new();

    public ConfigService()
    {
        Directory.CreateDirectory(DataDir);
        EnsureConfigExists();
        Current = Load();

        _watcher = new FileSystemWatcher(DataDir, "fermata.json")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) =>
        {
            // Brief delay to allow the write to finish before reading
            Thread.Sleep(100);
            Current = Load();
            ConfigChanged?.Invoke();
        };
    }

    public void Save(FermataConfig config)
    {
        Current = config;
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, _jsonOptions));
    }

    private void EnsureConfigExists()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new FermataConfig();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, _jsonOptions));
        }
    }

    private FermataConfig Load()
    {
        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<FermataConfig>(json, _jsonOptions) ?? new FermataConfig();
        }
        catch
        {
            return new FermataConfig();
        }
    }

    public void Dispose() => _watcher.Dispose();
}
