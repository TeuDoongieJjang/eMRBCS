namespace eMRBCS.Services;

public static class UiGuard
{
    private static readonly HashSet<string> _running = new();   // UI thread only
    private static bool _navBusy;

    /// Runs `work` only if the same key is not already running.
    /// lockControl (optional) is disabled while running.
    public static async Task RunAsync(string key, Func<Task> work, VisualElement? lockControl = null)
    {
        if (!_running.Add(key)) return;                 // 2nd tap: ignored
        if (lockControl != null) lockControl.IsEnabled = false;
        try { await work(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UiGuard:{key}] {ex}");
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null) await page.DisplayAlertAsync("Error", ex.Message, "Ok");
        }
        finally
        {
            if (lockControl != null) lockControl.IsEnabled = true;
            _running.Remove(key);
        }
    }

    /// Push a page once. The page is created lazily, so a blocked tap does not
    /// even run the page constructor.
    public static async Task PushOnceAsync(this INavigation nav, Func<Page> create)
    {
        if (_navBusy) return;
        _navBusy = true;
        try
        {
            await nav.PushAsync(create());
            await Task.Delay(250);                      // absorb late queued taps
        }
        finally { _navBusy = false; }
    }

    /// Pop once. A double tap on "Back" no longer pops two pages.
    public static async Task PopOnceAsync(this INavigation nav)
    {
        if (_navBusy || nav.NavigationStack.Count <= 1) return;
        _navBusy = true;
        try
        {
            await nav.PopAsync();
            await Task.Delay(250);
        }
        finally { _navBusy = false; }
    }
}

/// Only one print job at a time (one Bluetooth socket).
internal static class PrintGate
{
    private static int _busy;

    public static async Task<string> RunAsync(Func<Task<string>> print)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return "Printer is busy. Please wait for the current print to finish.";
        try { return await print(); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}