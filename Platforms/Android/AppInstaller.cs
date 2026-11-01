using Android.Content;
using Android.Provider;

namespace eMRBCS.Services
{
    public partial class ApkInstaller
    {
        private static string? _pendingApkPath;

        /// <summary>
        /// True while the update flow is running (download → permission → install).
        /// MainPage.OnAppearing uses this to avoid re-triggering the alert.
        /// </summary>

        public partial async Task InstallApkAsync(string apkUrl)
        {
            IsUpdateInProgress = true;
            try
            {
                // 1. Download APK to the persistent app data directory
                var apkPath = Path.Combine(FileSystem.AppDataDirectory, "update.apk");

                using var httpClient = new HttpClient();
                await using (var remoteStream = await httpClient.GetStreamAsync(apkUrl))
                await using (var localStream = File.Create(apkPath))
                {
                    await remoteStream.CopyToAsync(localStream);
                }

                // 2. On Android 8.0+ check the "Install unknown apps" permission
                if (OperatingSystem.IsAndroidVersionAtLeast(26)
                    && !Platform.AppContext.PackageManager.CanRequestPackageInstalls())
                {
                    // Remember the path so OnActivityResumed can finish the job
                    _pendingApkPath = apkPath;

                    var settingsIntent = new Intent(Settings.ActionManageUnknownAppSources);
                    settingsIntent.SetData(
                        Android.Net.Uri.Parse($"package:{Platform.AppContext.PackageName}"));

                    // IMPORTANT: launch from the Activity context and DO NOT
                    // add ActivityFlags.NewTask. That keeps the Settings screen
                    // in our task, so pressing Back returns here.
                    Platform.CurrentActivity!.StartActivity(settingsIntent);
                    return; // Keep IsUpdateInProgress = true until the user returns
                }

                LaunchInstaller(apkPath);
                IsUpdateInProgress = false;
            }
            catch
            {
                IsUpdateInProgress = false;
                throw;
            }
        }

        private static void LaunchInstaller(string apkPath)
        {
            var file = new Java.IO.File(apkPath);
            var authority = $"{Platform.AppContext.PackageName}.fileprovider";
            var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                Platform.AppContext, authority, file);

            var installIntent = new Intent(Intent.ActionView);
            installIntent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
            installIntent.AddFlags(ActivityFlags.NewTask);
            installIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

            Platform.AppContext.StartActivity(installIntent);
        }

        /// <summary>
        /// Called from the Android OnResume lifecycle event. When the user
        /// returns from the "Install unknown apps" page, this checks whether
        /// the permission was granted and, if so, launches the installer
        /// automatically – no second Yes tap needed.
        /// </summary>
        public static void OnActivityResumed()
        {
            if (string.IsNullOrEmpty(_pendingApkPath))
                return;

            var apkPath = _pendingApkPath;
            _pendingApkPath = null;

            if (OperatingSystem.IsAndroidVersionAtLeast(26)
                && !Platform.AppContext.PackageManager.CanRequestPackageInstalls())
            {
                // User came back without granting permission; end the flow.
                Android.Widget.Toast.MakeText(Platform.AppContext, "Permission required to install updates", Android.Widget.ToastLength.Short)!.Show();
                IsUpdateInProgress = false;
                return;
            }

            try
            {
                LaunchInstaller(apkPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-resume APK installation failed: {ex}");
            }
            finally
            {
                IsUpdateInProgress = false;
            }
        }
    }
}