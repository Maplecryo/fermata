/// Code-behind for HistoryWindow. Triggers a data refresh each time the window is shown.
/// No logic — all behaviour is in HistoryViewModel.
using Avalonia.Controls;
using FermataUI.ViewModels;

namespace FermataUI.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _vm;

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _vm.Refresh();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Hide instead of close so the window can be reopened from the tray.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
