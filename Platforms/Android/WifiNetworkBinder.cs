#if ANDROID
using Android.Content;
using Android.Net;

namespace eMRBCS.Platforms.Android
{
    public static class WifiNetworkBinder
    {
        private static ConnectivityManager? _cm;
        private static WifiCallback? _callback;

        public static void Start()
        {
            if (_callback != null) return;
            try
            {
                _cm = Microsoft.Maui.ApplicationModel.Platform.AppContext
                        .GetSystemService(Context.ConnectivityService) as ConnectivityManager;
                if (_cm == null) return;

                var request = new NetworkRequest.Builder()
                    .AddTransportType(TransportType.Wifi)!
                    .Build()!;

                _callback = new WifiCallback(_cm);
                _cm.RegisterNetworkCallback(request, _callback);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WifiNetworkBinder: " + ex);
            }
        }

        private sealed class WifiCallback : ConnectivityManager.NetworkCallback
        {
            private readonly ConnectivityManager _cm;
            private Network? _current;

            public WifiCallback(ConnectivityManager cm) { _cm = cm; }

            public override void OnAvailable(Network network)
            {
                base.OnAvailable(network);
                _current = network;
                _cm.BindProcessToNetwork(network);      // SMB traffic now goes through Wi-Fi
            }

            public override void OnLost(Network network)
            {
                base.OnLost(network);
                if (_current != null && _current.Equals(network))
                {
                    _current = null;
                    _cm.BindProcessToNetwork(null);     // back to the default network
                }
            }
        }
    }
}
#endif