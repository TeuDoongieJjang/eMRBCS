using CommunityToolkit.Mvvm.ComponentModel;
using eMRBCS.Global;
using MSRBC.Services;

namespace eMRBCS.Services;

public sealed partial class DataSyncService : ObservableObject
{
    public static DataSyncService Instance { get; } = new();
    private DataSyncService() { }

    // order MUST match FileService.ImportFileAsync: 0 consumers, 1 remarks, 2 rates, 3 users
    private static readonly string[] Names = { "Consumer File", "Remarks File", "Bill Rates File", "Users File" };
    private static readonly double[] Weights = { 0.55, 0.10, 0.20, 0.15 };
    private readonly double[] _stage = new double[4];
    private readonly object _lock = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ProgressText))] private double progress;
    [ObservableProperty] private string statusText = "";

    public string ProgressText => $"{(int)Math.Round(Progress * 100)}%";

    private void Report(int index, double stage)
    {
        double total = 0;
        lock (_lock)
        {
            _stage[index] = stage;
            for (int i = 0; i < 4; i++) total += Weights[i] * _stage[i];
        }
        MainThread.BeginInvokeOnMainThread(() => Progress = total);
    }

    public async Task StartAsync()
    {
        if (IsBusy) return;
        lock (_lock) Array.Clear(_stage);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsBusy = true; Progress = 0; StatusText = "Getting data...";
        });

        var msgs = new string[4];
        var ok = new bool[4];
        string? warning = null;

        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            try
            {
                var files = new FileService();                    // own SMB session per file
                var parts = (await files.ImportFileAsync(i)).Split(';');
                if (parts[0].Trim() != "success")
                {
                    warning ??= parts.Length > 1 ? parts[1].Trim() : "Download failed.";
                    msgs[i] = $"{Names[i]}: Failed";
                    return;
                }
                Report(i, 0.5);

                var db = new DBService();
                bool saved = i switch
                {
                    0 => await db.ConsumerReadAndProcessileAsync(files.consumerFile),
                    1 => await db.RemarkReadAndProcessFileAsync(files.remarksFile),
                    2 => await db.BillRatesReadAndProcessFileAsync(files.billrateFile),
                    _ => await db.UserReadAndProcessileAsync(files.userfile)
                };

                ok[i] = saved;
                msgs[i] = saved ? $"{Names[i]}: Done" + i switch
                {
                    0 => $" | Records: {ConsumerRecords.noofrecords_import}",
                    1 => $" | Records: {OtherRecords.noofremarks}",
                    2 => $" | Records: {OtherRecords.noofbillrates}",
                    _ => $" | Records: {OtherRecords.noofusers}"
                } : $"{Names[i]}: Failed";
            }
            catch (Exception ex)
            {
                warning ??= ex.Message;
                msgs[i] = $"{Names[i]}: Failed";
            }
            finally { Report(i, 1); }
        })));

        bool withError = ok.Any(x => !x);
        try
        {
            var db = new DBService();
            await db.ServerImportSave(ok[0] ? 1 : 0, ok[1] ? 1 : 0, ok[2] ? 1 : 0, ok[3] ? 1 : 0);
            await db.SaveUserActivity("Utilities", "Tap Get Data", withError ? "Success with Error" : "Success");
        }
        catch { }

        string summary = string.Join("\n", msgs) + (withError && warning != null ? "\n\n" + warning : "");

        await MainThread.InvokeOnMainThreadAsync(() => { IsBusy = false; StatusText = ""; });

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page != null)
            await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlertAsync("Notice", summary, "Ok"));
    }
}