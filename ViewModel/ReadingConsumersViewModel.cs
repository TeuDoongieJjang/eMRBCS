using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using System.ComponentModel;

namespace eMRBCS.ViewModels;

public partial class ReadingConsumersViewModel : ObservableObject
{
    public ConsumerStore Store => ConsumerStore.Instance;
    public List<string> BillingFilters { get; } = new() { "Unbilled Consumers", "Billed Consumers", "Collected Consumers" };
    public List<string> SearchFields { get; } = new() { "Meter No", "Account No", "Name" };

    [ObservableProperty] private IEnumerable<ConsumerRow> items;
    [ObservableProperty] private int selectedFilterIndex;
    [ObservableProperty] private int selectedSearchFieldIndex;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(OptionsIcon))] private bool showOptions;
    [ObservableProperty] private string summaryText = "0 / 0";
    [ObservableProperty] private Color? summaryColor;

    public string OptionsIcon => ShowOptions ? "menuicons/options.png" : "menuicons/options1.png";
    public string EmptyText => Store.IsLoading ? "Loading consumers..."
                             : Store.TotalCount == 0 ? "No consumers yet. Go to Utilities > Get Data."
                             : "No record(s) to display.";

    private bool _isSearching;
    private int _matchCount;

    public ReadingConsumersViewModel() { items = Store.Unbilled; }

    public void Attach()
    {
        Store.PropertyChanged -= OnStoreChanged;   // NEW: never subscribe twice
        Store.PropertyChanged += OnStoreChanged;
        Refresh();
    }
    public void Detach() => Store.PropertyChanged -= OnStoreChanged;
    private void OnStoreChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConsumerStore.Revision) or nameof(ConsumerStore.IsLoading) or nameof(ConsumerStore.TotalCount))
        {
            if (_isSearching) Refresh(); else UpdateSummary();
        }
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        if (value < 0) { SelectedFilterIndex = 0; return; }   // Picker quirk when ItemsSource binds late
        Refresh();
    }
    partial void OnSelectedSearchFieldIndexChanged(int value)
    {
        if (value < 0) SelectedSearchFieldIndex = 0;
    }
    partial void OnSearchTextChanged(string value)
    {
        // clear the filter as soon as the text drops below 3 characters
        if (_isSearching && (value ?? "").Trim().Length < 3) Refresh();
    }

    [RelayCommand] private void ToggleOptions() => ShowOptions = !ShowOptions;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if ((SearchText ?? "").Trim().Length < 3)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null) await page.DisplayAlertAsync("Notice", "Please enter at least 3 characters.", "Ok");
            return;
        }
        Refresh();
    }

    private void Refresh()
    {
        IEnumerable<ConsumerRow>? src = SelectedFilterIndex switch
        {
            0 => Store.Unbilled,
            1 => Store.Billed,
            _ => null
        };

        string term = (SearchText ?? "").Trim();
        if (src == null) { Items = Array.Empty<ConsumerRow>(); _isSearching = false; }
        else if (term.Length < 3) { Items = src; _isSearching = false; }   // live, auto-updating collection
        else
        {
            var list = SelectedSearchFieldIndex switch
            {
                0 => src.Where(r => r.MeterNo.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList(),
                1 => src.Where(r => r.AccountNo.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList(),
                _ => src.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList()
            };
            Items = list; _matchCount = list.Count; _isSearching = true;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int total = ConsumerRecords.noofrecords_import;
        int billed = ConsumerRecords.noofrecordsBilled;

        if (_isSearching) { SummaryText = $"{_matchCount} / {total}"; SummaryColor = Colors.Red; }
        else
        {
            SummaryColor = null;
            SummaryText = SelectedFilterIndex switch
            {
                0 => $"{total - billed} / {total}",
                1 => $"{billed} / {total}",
                _ => $"0 / {total}"
            };
        }
        OnPropertyChanged(nameof(EmptyText));
    }
}