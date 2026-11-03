namespace eMRBCS.Services
{
    public partial class ApkInstaller
    {
        public static bool IsUpdateInProgress { get; private set; }

        public static event Action? UpdateFlowAborted;

#if ANDROID
        public partial Task InstallApkAsync(string apkUrl);
#else
    public Task InstallApkAsync(string apkUrl) =>
        throw new PlatformNotSupportedException("APK installation is supported only on Android.");
#endif
    }
}
