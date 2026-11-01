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
        public static Task<bool> EnsureBluetoothPermissionsAsync()
        {
            if (HasBluetoothPermissions())
                return Task.FromResult(true);

            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
                return Task.FromResult(false);

            _pendingRequest = new TaskCompletionSource<bool>();

            ActivityCompat.RequestPermissions(
                activity,
                new[] { Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect },
                RequestCode);

            return _pendingRequest.Task;
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
