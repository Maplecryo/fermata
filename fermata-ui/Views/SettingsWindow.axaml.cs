/// Code-behind for SettingsWindow. No logic — all behaviour is in SettingsViewModel.
using Avalonia.Controls;
using FermataUI.ViewModels;

namespace FermataUI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Hide instead of close so the window can be reopened from the tray.
        this.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }
}
