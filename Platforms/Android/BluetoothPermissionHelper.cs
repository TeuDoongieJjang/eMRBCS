#if ANDROID
using Android;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace eMRBCS.Platforms.Android
{

    public static class BluetoothPermissionHelper
    {
        private const int RequestCode = 1101;
        private static TaskCompletionSource<bool> _pendingRequest;

        /// <summary>
        /// True if Bluetooth permissions are already granted (or not required on this OS version).
        /// </summary>
        public static bool HasBluetoothPermissions()
        {
            if ((int)global::Android.OS.Build.VERSION.SdkInt < 31)
                return true; // Pre-Android 12 (e.g. Honeywell on Android 10) - no runtime check needed.

            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null) return false;

            return ContextCompat.CheckSelfPermission(activity, Manifest.Permission.BluetoothScan) == Permission.Granted
                && ContextCompat.CheckSelfPermission(activity, Manifest.Permission.BluetoothConnect) == Permission.Granted;
        }

        /// <summary>
        /// Requests Bluetooth permissions if needed and awaits the user's response.
        /// Returns true if granted (or already granted / not required).
        /// </summary>
        public static async Task<bool> EnsureBluetoothPermissionsAsync()
        {
            if (HasBluetoothPermissions()) return true;

            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null) return false;

            _pendingRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ActivityCompat.RequestPermissions(
                activity,
                new[] { Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect },
                RequestCode);

            try { return await _pendingRequest.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { _pendingRequest = null; return false; }
        }

        /// <summary>
        /// Call this from MainActivity.OnRequestPermissionsResult to complete the pending request.
        /// </summary>
        public static void OnRequestPermissionsResult(int requestCode, Permission[] grantResults)
        {
            if (requestCode != RequestCode || _pendingRequest == null)
                return;

            bool granted = grantResults.Length > 0 && grantResults.All(r => r == Permission.Granted);
            _pendingRequest.TrySetResult(granted);
            _pendingRequest = null;
        }
    }
}
#endif
