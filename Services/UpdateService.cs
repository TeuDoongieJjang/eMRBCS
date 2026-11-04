using eMRBCS.Models;
using System.Text.Json;

namespace eMRBCS.Services;

public class UpdateService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<UpdateInfo?> CheckForUpdateAsync(string repoOwner, string repoName)
    {
        // 1. Do nothing if the device has no internet connection.
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return null;

        try
        {
            var localVersionName = AppInfo.Current.VersionString;
            var localVersionCode = int.TryParse(AppInfo.Current.BuildString, out var code) ? code : 0;

            // 2. Fetch remote manifest
            var manifestUrl = $"https://github.com/{repoOwner}/{repoName}/releases/latest/download/version.json";
            var manifestJson = await _httpClient.GetStringAsync(manifestUrl);

            var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestJson, _jsonOptions);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.VersionName))
                return null;

            // 3. Reject any pre‑release version in the manifest.
            if (manifest.VersionName.Contains('-'))
                return null;

            // 4. Check  versions
            var codeIsNewer = manifest.VersionCode > localVersionCode;
            var nameIsNewer = VersionHelper.IsGreater(manifest.VersionName, localVersionName);

            var updateAvailable = manifest.VersionCode > 0 ? codeIsNewer : nameIsNewer;

            if (!updateAvailable)
                return null;

            // 5. Build download URL
            var apkFileName = !string.IsNullOrWhiteSpace(manifest.ApkFileName)
                ? manifest.ApkFileName
                : $"eMRBCS-{manifest.VersionName}.apk";

            var downloadUrl = $"https://github.com/{repoOwner}/{repoName}/releases/latest/download/{apkFileName}";

            return new UpdateInfo
            {
                Version = manifest.VersionName,
                VersionCode = manifest.VersionCode,
                DownloadUrl = downloadUrl,
                ReleaseNotes = manifest.ReleaseNotes
            };
        }
        catch
        {
            // 6. Silently ignore any error (network failure, malformed JSON, etc.)
            return null;
        }
    }
}