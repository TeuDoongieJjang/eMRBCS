using eMRBCS.Global;
using MSRBC.Services;

namespace eMRBCS.Services;

/// One shared status checker. Pages subscribe to StatusChanged.
public sealed class ServerStatusMonitor
{
    public static ServerStatusMonitor Instance { get; } = new();
    private ServerStatusMonitor() { }

    private const int PollSeconds = 5;

    public event Action<bool>? StatusChanged;
    public bool IsOnline { get; private set; }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _cts;
    private int _users;

    /// Call from OnAppearing. Safe to call from several pages (ref-counted).
    public void Start()
    {
        lock (_gate)
        {
            _users++;
            if (_users > 1) return;

            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => LoopAsync(ct));
        }
    }

    /// Call from OnDisappearing.
    public void Stop()
    {
        lock (_gate)
        {
            if (_users == 0) return;
            _users--;
            if (_users > 0) return;

            Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
            _cts?.Cancel();
            _cts = null;
        }
    }

    /// Ask for a re-check as soon as possible (network changed, app resumed, config saved).
    public void RequestCheck()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.None)
            Publish(false);          // Wi-Fi/network is gone: go red right away
        RequestCheck();              // and verify again (also handles reconnect)
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool ok = false;
            try
            {
                ok = Serverdetails.Validnetwork == 1
                     && await new FileService().CheckSMBStatus();
            }
            catch { }

            if (ct.IsCancellationRequested) break;
            Publish(ok);

            try
            {
                // wakes early when RequestCheck() is called
                bool signaled = await _wake.WaitAsync(TimeSpan.FromSeconds(PollSeconds), ct);
                if (signaled) await Task.Delay(1500, ct);   // give Wi-Fi a moment to get its IP
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Publish(bool ok)
    {
        IsOnline = ok;
        StatusChanged?.Invoke(ok);
    }
}