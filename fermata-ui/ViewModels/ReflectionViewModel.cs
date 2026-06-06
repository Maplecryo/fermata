/// ViewModel for the reflection window. Owns the countdown timer, journal state,
/// and IPC response logic. Does not touch the database directly for window open —
/// that is triggered here and passed to DatabaseService.
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FermataUI.Services;

namespace FermataUI.ViewModels;

public partial class ReflectionViewModel : ObservableObject
{
    private const int JournalMinChars = 10;
    // Require at least 2 words each with 2+ characters, and 4+ unique characters.
    private const int JournalMinWords = 2;
    private const int JournalMinWordLength = 2;
    private const int JournalMinUniqueChars = 4;

    private readonly IpcServer _ipc;
    private readonly DatabaseService _db;
    private readonly ConfigService _config;

    private System.Threading.Timer? _timer;
    private long _launchId;
    private DateTime _windowOpenedAt;

    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private string _exePath = "";
    [ObservableProperty] private int _countdownSeconds;
    [ObservableProperty] private bool _countdownFinished;
    [ObservableProperty] private string _countdownLabel = "seconds left";
    [ObservableProperty] private string _journalText = "";
    [ObservableProperty] private bool _showJournal;
    [ObservableProperty] private bool _canContinue;

    // Triggers the scale-pulse animation in the view (toggled each tick)
    [ObservableProperty] private bool _countdownPulse;

    public event Action? ShowRequested;
    public event Action? HideRequested;

    public ICommand CancelCommand { get; }
    public ICommand ContinueCommand { get; }

    public ReflectionViewModel(IpcServer ipc, DatabaseService db, ConfigService config)
    {
        _ipc = ipc;
        _db = db;
        _config = config;

        CancelCommand = new RelayCommand(OnCancel);
        ContinueCommand = new RelayCommand(OnContinue, () => CanContinue);

        ipc.InterceptReceived += evt => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try { Open(evt); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReflectionViewModel] Open failed: {ex}");
                ipc.SendResponse("cancel");
            }
        });
    }

    partial void OnJournalTextChanged(string value) => RefreshCanContinue();
    partial void OnCountdownFinishedChanged(bool value) => RefreshCanContinue();

    private void RefreshCanContinue()
    {
        var journalOk = !ShowJournal || IsJournalAcceptable(JournalText);
        CanContinue = CountdownFinished && journalOk;
        ((RelayCommand)ContinueCommand).NotifyCanExecuteChanged();
    }

    /// Rejects gibberish: requires enough chars, enough unique chars, and enough words.
    private static bool IsJournalAcceptable(string text)
    {
        var nonWhitespace = text.Count(c => !char.IsWhiteSpace(c));
        if (nonWhitespace < JournalMinChars) return false;

        var uniqueChars = text.ToLowerInvariant()
            .Where(char.IsLetter)
            .Distinct()
            .Count();
        if (uniqueChars < JournalMinUniqueChars) return false;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= JournalMinWordLength)
            .Count();
        if (words < JournalMinWords) return false;

        return true;
    }

    private void Open(InterceptEvent evt)
    {
        AppName = evt.AppName;
        ExePath = evt.ExePath;
        ShowJournal = _config.Current.RequireJournal;
        JournalText = "";
        CountdownSeconds = _config.Current.DelaySeconds;
        CountdownFinished = false;
        CountdownLabel = "seconds left";
        _windowOpenedAt = DateTime.UtcNow;

        try { _launchId = _db.LogLaunch(evt.AppName, evt.ExePath, evt.Timestamp); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReflectionViewModel] DB log failed: {ex}");
            _launchId = 0;
        }

        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Tick);
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        ShowRequested?.Invoke();
    }

    /// Called when the reflection window loses focus. Resets the countdown
    /// so the user can't bypass the delay by switching to another app.
    public void ResetCountdown()
    {
        // Only reset if the countdown is still running.
        if (CountdownFinished) return;

        CountdownSeconds = _config.Current.DelaySeconds;
        CountdownLabel = "seconds left";

        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Tick);
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Tick()
    {
        if (CountdownSeconds > 0)
        {
            CountdownSeconds--;
            CountdownPulse = !CountdownPulse;
        }

        if (CountdownSeconds == 0 && !CountdownFinished)
        {
            CountdownFinished = true;
            CountdownLabel = "ready when you are";
            _timer?.Dispose();
        }
    }

    private void OnCancel()
    {
        _timer?.Dispose();
        try { if (_launchId > 0) _db.UpdateOutcome(_launchId, "cancelled",
            (long)(DateTime.UtcNow - _windowOpenedAt).TotalMilliseconds); }
        catch { /* don't let DB errors block IPC */ }

        _ipc.SendResponse("cancel");
        HideRequested?.Invoke();
    }

    private void OnContinue()
    {
        _timer?.Dispose();
        try
        {
            if (_launchId > 0)
            {
                _db.UpdateOutcome(_launchId, "continued",
                    (long)(DateTime.UtcNow - _windowOpenedAt).TotalMilliseconds);
                if (ShowJournal && JournalText.Trim().Length > 0)
                    _db.InsertJournalEntry(_launchId, DateTime.UtcNow.ToString("O"), JournalText.Trim());
            }
        }
        catch { /* don't let DB errors block IPC */ }

        // Send IPC response BEFORE hiding — the monitor is blocked waiting for this.
        _ipc.SendResponse("continue");
        HideRequested?.Invoke();
    }
}
