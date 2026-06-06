/// ViewModel for the settings window. Validates app names and manages the restricted list.
/// Does not write to disk until SaveCommand is explicitly invoked.
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FermataUI.Services;

namespace FermataUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static readonly HashSet<string> SystemBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        "explorer.exe", "winlogon.exe", "csrss.exe", "lsass.exe",
        "svchost.exe", "taskmgr.exe", "dwm.exe",
        "fermata-monitor.exe", "FermataUI.exe",
        // macOS
        "Finder", "loginwindow", "WindowServer", "launchd",
        "fermata-monitor", "FermataUI",
    };

    private readonly ConfigService _config;

    [ObservableProperty] private ObservableCollection<string> _apps = new();
    [ObservableProperty] private string? _selectedApp;
    [ObservableProperty] private string _newAppInput = "";
    [ObservableProperty] private string _inputError = "";
    [ObservableProperty] private int _delaySeconds;
    [ObservableProperty] private bool _requireJournal;
    [ObservableProperty] private string _saveConfirmation = "";

    // Two-stage remove: arm first, confirm second
    [ObservableProperty] private bool _removeArmed;

    public string RemoveButtonLabel => RemoveArmed ? "Are you sure?" : "Remove selected";

    // Shown in the Settings window input hint
    public string AppInputWatermark => IsMac ? "e.g. Steam" : "e.g. steam.exe";

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SaveCommand { get; }

    public SettingsViewModel(ConfigService config)
    {
        _config = config;
        LoadFromConfig();

        AddCommand = new RelayCommand(OnAdd);
        RemoveCommand = new RelayCommand(OnRemove, () => SelectedApp is not null);
        SaveCommand = new RelayCommand(OnSave);

        _config.ConfigChanged += () =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(LoadFromConfig);
    }

    partial void OnSelectedAppChanged(string? value)
    {
        RemoveArmed = false;
        OnPropertyChanged(nameof(RemoveButtonLabel));
        ((RelayCommand)RemoveCommand).NotifyCanExecuteChanged();
    }

    partial void OnRemoveArmedChanged(bool value) =>
        OnPropertyChanged(nameof(RemoveButtonLabel));

    private void LoadFromConfig()
    {
        Apps = new ObservableCollection<string>(_config.Current.Apps);
        DelaySeconds = _config.Current.DelaySeconds;
        RequireJournal = _config.Current.RequireJournal;
    }

    private void OnAdd()
    {
        var name = NewAppInput.Trim();
        InputError = "";

        if (string.IsNullOrEmpty(name))
            return;

        // On Windows require .exe; on macOS just a plain name is fine.
        if (!IsMac && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            InputError = "Name must end in .exe";
            return;
        }

        if (SystemBlocklist.Contains(name))
        {
            InputError = $"{name} is a protected system process and cannot be added.";
            return;
        }

        if (Apps.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            InputError = "Already in the list.";
            return;
        }

        Apps.Add(name);
        NewAppInput = "";
    }

    private void OnRemove()
    {
        if (SelectedApp is null) return;

        if (!RemoveArmed)
        {
            RemoveArmed = true;
            return;
        }

        Apps.Remove(SelectedApp);
        SelectedApp = null;
        RemoveArmed = false;
    }

    private void OnSave()
    {
        _config.Save(new FermataConfig
        {
            DelaySeconds = DelaySeconds,
            RequireJournal = RequireJournal,
            Apps = Apps.ToList()
        });

        SaveConfirmation = "Saved.";
        Task.Delay(2000).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => SaveConfirmation = ""));
    }
}
