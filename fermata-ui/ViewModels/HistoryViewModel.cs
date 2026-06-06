/// ViewModel for the history window. Loads from DatabaseService in pages of 50.
/// Does not modify any data — read-only view.
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FermataUI.Services;

namespace FermataUI.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly DatabaseService _db;
    private int _offset;

    [ObservableProperty] private ObservableCollection<LaunchRecord> _records = new();
    [ObservableProperty] private int _weekTotal;
    [ObservableProperty] private int _weekContinued;
    [ObservableProperty] private int _weekCancelled;
    [ObservableProperty] private bool _hasMore;

    // Sort state
    [ObservableProperty] private string _sortColumn = "Timestamp";
    [ObservableProperty] private bool _sortAscending;

    public ICommand LoadMoreCommand { get; }
    public ICommand SortCommand { get; }

    public HistoryViewModel(DatabaseService db)
    {
        _db = db;
        LoadMoreCommand = new RelayCommand(LoadNextPage);
        SortCommand = new RelayCommand<string>(OnSort);
    }

    public void Refresh()
    {
        _offset = 0;
        Records.Clear();
        LoadNextPage();

        var summary = _db.GetWeeklySummary();
        WeekTotal = summary.Total;
        WeekContinued = summary.Continued;
        WeekCancelled = summary.Cancelled;
    }

    private void LoadNextPage()
    {
        var page = _db.GetHistory(PageSize, _offset);
        foreach (var r in page) Records.Add(r);
        _offset += page.Count;
        HasMore = page.Count == PageSize;
    }

    private void OnSort(string? column)
    {
        if (column is null) return;

        if (SortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            SortColumn = column;
            SortAscending = false;
        }

        var sorted = SortAscending
            ? Records.OrderBy(r => GetSortKey(r, column))
            : Records.OrderByDescending(r => GetSortKey(r, column));

        Records = new ObservableCollection<LaunchRecord>(sorted);
    }

    private static string GetSortKey(LaunchRecord r, string column) => column switch
    {
        "Application" => r.Application,
        "Outcome"     => r.Outcome,
        "WaitedMs"    => r.WaitedMs.ToString("D20"),
        _             => r.Timestamp
    };
}
