
using CommunityToolkit.Mvvm.ComponentModel;
using eMRBCS.Global;
using eMRBCS.Models;
using System.Collections.ObjectModel;

namespace eMRBCS.Services;

public sealed partial class ConsumerStore : ObservableObject
{
    public static ConsumerStore Instance { get; } = new();
    private ConsumerStore() { }

    public ObservableCollection<ConsumerRow> Unbilled { get; } = new();
    public ObservableCollection<ConsumerRow> Billed { get; } = new();

    private readonly Dictionary<int, ConsumerRow> _byId = new();
    private Dictionary<string, Consumers> _byAcct = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(LoadProgress))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadProgress))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int loadedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool isLoading;

    [ObservableProperty] private int revision;

    public bool IsEmpty => !IsLoading && TotalCount == 0;
    public double LoadProgress => TotalCount == 0 ? 0 : (double)LoadedCount / TotalCount;
    public string StatusText => IsLoading ? $"Loading consumers... {LoadedCount:N0} / {TotalCount:N0}" : "";

    private static void UI(Action a)
    {
        if (MainThread.IsMainThread) a(); else MainThread.BeginInvokeOnMainThread(a);
    }

    public void BeginLoading() => UI(() => IsLoading = true);

    /// O(1) lookup instead of lstconsumers.Where(...).FirstOrDefault()
    public Consumers? FindByAccountNo(string? acct)
        => acct != null && _byAcct.TryGetValue(acct, out var c) ? c : null;

    /// Replaces the whole data set. Rows appear gradually; the UI never blocks.
    public async Task ResetAsync(List<Consumers> all)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            // Full data is usable immediately by the Reading page / reports.
            ConsumerRecords.lstconsumers = all;
            ConsumerRecords.noofrecords_import = all.Count;
            ConsumerRecords.noofrecordsBilled = all.Count(c => c.status == 1);

            var map = new Dictionary<string, Consumers>(all.Count);
            foreach (var c in all) if (c.accountNo != null) map[c.accountNo] = c;
            _byAcct = map;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Unbilled.Clear(); Billed.Clear(); _byId.Clear();
                LoadedCount = 0; TotalCount = all.Count; IsLoading = true; Revision++;
            });

            int index = 0;
            while (index < all.Count)
            {
                if (token.IsCancellationRequested) return;

                int take = Math.Min(index == 0 ? 30 : 60, all.Count - index);
                var rows = new ConsumerRow[take];
                for (int i = 0; i < take; i++) rows[i] = new ConsumerRow(all[index + i]); // off UI thread
                index += take;
                int done = index;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    foreach (var r in rows)
                    {
                        r.Refresh(); // status may have changed while the row was being built
                        _byId[r.Consumer.accountID] = r;
                        if (r.Status == 0) Unbilled.Add(r);
                        else if (r.Status == 1) Billed.Add(r);
                        // other statuses (e.g. 2 = collected) are not shown in these two lists
                    }
                    LoadedCount = done;
                });

                await Task.Delay(15); // let the UI breathe between batches
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        finally
        {
            if (!token.IsCancellationRequested)
                await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    /// Call after a consumer was saved (status = 1). Moves the row Unbilled -> Billed.
    public void MarkBilled(Consumers c) => UI(() =>
    {
        if (_byId.TryGetValue(c.accountID, out var row))
        {
            row.Refresh();
            if (Unbilled.Remove(row)) InsertSorted(Billed, row);
        }
        Revision++;
    });

    private static void InsertSorted(ObservableCollection<ConsumerRow> list, ConsumerRow row)
    {
        int i = 0;
        while (i < list.Count && list[i].Consumer.accountID < row.Consumer.accountID) i++;
        list.Insert(i, row);
    }
}