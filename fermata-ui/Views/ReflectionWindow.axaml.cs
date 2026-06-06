/// Code-behind for ReflectionWindow. Wires up the countdown pulse animation.
/// All logic lives in ReflectionViewModel.
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FermataUI.ViewModels;

namespace FermataUI.Views;

public partial class ReflectionWindow : Window
{
    private readonly ScaleTransform _countdownScale = new(1.0, 1.0);

    public ReflectionWindow(ReflectionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        this.Opened += (_, _) =>
        {
            var tb = this.FindControl<TextBlock>("CountdownNumber");
            if (tb is not null) tb.RenderTransform = _countdownScale;
        };

        // Prevent the OS from ever fully closing this window — only hide it.
        this.Closing += (_, e) => e.Cancel = true;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReflectionViewModel.CountdownPulse))
                PulseCountdown();
        };

        vm.HideRequested += () => Hide();
        vm.ShowRequested += () => { Show(); Activate(); };

        // Reset the countdown whenever the user tabs away — they can't bypass
        // the delay by switching to another app and waiting it out there.
        this.Deactivated += (_, _) => vm.ResetCountdown();
    }

    private void PulseCountdown()
    {
        // Scale up to 108% over 90ms then back to 100% over 90ms using a timer.
        const double peak = 1.08;
        const int steps = 9;
        const int intervalMs = 10; // 90ms / 9 steps
        int step = 0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        timer.Tick += (_, _) =>
        {
            step++;
            if (step <= steps)
            {
                // Ease up
                double t = (double)step / steps;
                _countdownScale.ScaleX = 1.0 + (peak - 1.0) * t;
                _countdownScale.ScaleY = _countdownScale.ScaleX;
            }
            else if (step <= steps * 2)
            {
                // Ease down
                double t = (double)(step - steps) / steps;
                _countdownScale.ScaleX = peak - (peak - 1.0) * t;
                _countdownScale.ScaleY = _countdownScale.ScaleX;
            }
            else
            {
                _countdownScale.ScaleX = 1.0;
                _countdownScale.ScaleY = 1.0;
                timer.Stop();
            }
        };
        timer.Start();
    }
}
