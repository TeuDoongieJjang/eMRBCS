using eMRBCS.Global;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

#if ANDROID
using Android.Content;
using Android.Provider;
#endif

namespace eMRBCS.Services;

public enum LicenseState
{
    Demo,
    Activated,
    Expired
}

public sealed record LicenseStatus(
    LicenseState State,
    DateTime InstallUtc,
    DateTime ExpiresUtc,
    int DaysLeft,
    bool ClockRolledBack,
    bool RecordTampered);

/// <summary>
/// Offline license persistence.
///
/// Copies are stored in multiple independent locations.
/// Valid records are merged using:
/// - Earliest InstallTicks
/// - Latest LastSeenTicks
/// - Any valid activation wins
///
/// When one valid copy survives, SaveAll repairs the other copies.
/// </summary>
public sealed class LicenseService
{
    public static LicenseService Instance { get; } = new();

    private LicenseService() { }

    public const int DemoMonths = 3;

    public static bool EnforceStorageAccess = true;

    private static readonly TimeSpan SaveStep =
        TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ClockSlack =
        TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _askGate = new(1, 1);

    private Rec? _rec;
    private bool _tampered;
    private bool _foundExternal;

    private readonly HashSet<string> _asked = new();

    private string _readInfo = "";
    private string _writeInfo = "";

    public string StorageInfo =>
        (_readInfo + _writeInfo +
         "all-files-access=" + HasAllFilesAccess).TrimEnd();

    public string DeviceId
    {
        get
        {
            string id = MyDeviceInfo.device_AndroidID ?? "";

            return string.IsNullOrWhiteSpace(id)
                ? "UNKNOWN"
                : id.Trim().ToUpperInvariant();
        }
    }

    private sealed class Rec
    {
        public string DeviceId { get; set; } = "";
        public long InstallTicks { get; set; }
        public long LastSeenTicks { get; set; }
        public bool Activated { get; set; }
        public string ActivationKey { get; set; } = "";
    }

    private sealed record Source(
        string Label,
        string? Text,
        string Note,
        bool External);

#if ANDROID
    private sealed record MediaLocation(
        string Label,
        global::Android.Net.Uri Collection,
        string RelativePath,
        string FileName,
        string FilePrefix);
#endif

    // ------------------------------------------------------------
    // Build-time floor
    // ------------------------------------------------------------

    private static readonly DateTime BuildUtc = ReadBuildUtc();

    private static DateTime ReadBuildUtc()
    {
        try
        {
            var attr = typeof(LicenseService).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildUtc");

            if (attr?.Value != null &&
                DateTime.TryParseExact(
                    attr.Value,
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out var d))
            {
                return d;
            }
        }
        catch { }

        return DateTime.MinValue;
    }

    // ------------------------------------------------------------
    // Activation key
    // Keep the existing ProductActivation algorithm unchanged.
    // ------------------------------------------------------------

    public static string ComputeActivationKey(string productKeyUpper)
    {
        string encrypted =
            StartupService.EncryptString(productKeyUpper, "manny");

        string alnum =
            Regex.Replace(encrypted, "[^a-zA-Z0-9]", "");

        return (alnum.Length > 8
            ? alnum.Substring(0, 8)
            : alnum).ToUpperInvariant();
    }

    // ------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------

    public Task InitializeAsync() => Task.Run(Initialize);

    public bool IsUsable =>
        GetStatus().State != LicenseState.Expired;

    public bool HasAllFilesAccess
    {
        get
        {
#if ANDROID
            try
            {
                return !OperatingSystem.IsAndroidVersionAtLeast(30)
                    || global::Android.OS.Environment
                        .IsExternalStorageManager;
            }
            catch
            {
                return false;
            }
#else
            return true;
#endif
        }
    }

    // Retained as a compatibility property for any existing callers.
    // Settings.System is no longer used by this service.
    public bool HasFullProtection => HasAllFilesAccess;

    public bool NeedsStorageAccess =>
        EnforceStorageAccess &&
        !_foundExternal &&
        !HasAllFilesAccess;

    // ------------------------------------------------------------
    // Initialize and merge records
    // ------------------------------------------------------------

    public void Initialize()
    {
        lock (_gate)
        {
            if (_rec != null)
                return;

            string dev = DeviceId;
            string expectedKey = ComputeActivationKey(dev);

            var valid = new List<Rec>();
            var info = new StringBuilder();

            info.AppendLine("id=" + dev);

            _foundExternal = false;

            foreach (var source in ReadSources())
            {
                if (source.Text == null)
                {
                    info.AppendLine(
                        source.Label + ": " + source.Note);

                    continue;
                }

                var (record, note) =
                    ParseText(source.Text, dev);

                info.AppendLine(
                    source.Label + ": " + note);

                if (record == null)
                    continue;

                valid.Add(record);

                if (source.External)
                    _foundExternal = true;
            }

            _readInfo = info.ToString();

            DateTime raw = DateTime.UtcNow;

            DateTime start =
                raw < BuildUtc ? BuildUtc : raw;

            Rec merged;

            if (valid.Count > 0)
            {
                // Earliest installation date wins.
                // Latest last-seen date wins.
                merged = new Rec
                {
                    DeviceId = dev,

                    InstallTicks =
                        valid.Min(x => x.InstallTicks),

                    LastSeenTicks =
                        valid.Max(x => x.LastSeenTicks)
                };

                // Any valid activation wins.
                if (valid.Any(x =>
                    x.Activated &&
                    string.Equals(
                        x.ActivationKey,
                        expectedKey,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Activated = true;
                    merged.ActivationKey = expectedKey;
                }
            }
            else
            {
                // No valid record survived.
                // This is treated as a new installation.
                merged = new Rec
                {
                    DeviceId = dev,
                    InstallTicks = start.Ticks,
                    LastSeenTicks = start.Ticks
                };

                if (LegacyKeyValid(dev))
                {
                    merged.Activated = true;
                    merged.ActivationKey = expectedKey;
                }
            }

            _rec = merged;

            // Rebuild every storage copy from the merged record.
            SaveAll();
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            _rec = null;
        }

        Initialize();
    }

    // ------------------------------------------------------------
    // License status
    // ------------------------------------------------------------

    public LicenseStatus GetStatus()
    {
        Initialize();

        lock (_gate)
        {
            var rec = _rec!;

            var lastSeen =
                new DateTime(
                    rec.LastSeenTicks,
                    DateTimeKind.Utc);

            DateTime raw = DateTime.UtcNow;

            bool clockBeforeBuild =
                BuildUtc != DateTime.MinValue &&
                raw < BuildUtc - ClockSlack;

            bool rolledBack =
                clockBeforeBuild ||
                raw < lastSeen - ClockSlack;

            DateTime now =
                raw < lastSeen ? lastSeen : raw;

            if (!rolledBack &&
                raw - lastSeen >= SaveStep)
            {
                rec.LastSeenTicks = raw.Ticks;
                SaveAll();
            }

            var install =
                new DateTime(
                    rec.InstallTicks,
                    DateTimeKind.Utc);

            var expires =
                install.AddMonths(DemoMonths);

            int daysLeft = Math.Max(
                0,
                (int)Math.Ceiling(
                    (expires - now).TotalDays));

            LicenseState state;

            if (rec.Activated)
            {
                state = LicenseState.Activated;
            }
            else if (rolledBack)
            {
                state = LicenseState.Expired;
            }
            else
            {
                state = now < expires
                    ? LicenseState.Demo
                    : LicenseState.Expired;
            }

            return new LicenseStatus(
                state,
                install,
                expires,
                daysLeft,
                rolledBack,
                _tampered);
        }
    }

    // ------------------------------------------------------------
    // Activation
    // ------------------------------------------------------------

    public bool TryActivate(string? key)
    {
        Initialize();

        string k =
            (key ?? "").Trim().ToUpperInvariant();

        if (k.Length == 0)
            return false;

        if (!string.Equals(
            k,
            ComputeActivationKey(DeviceId),
            StringComparison.Ordinal))
        {
            return false;
        }

        lock (_gate)
        {
            _rec!.Activated = true;
            _rec.ActivationKey = k;

            _tampered = false;

            SaveAll();
        }

        return true;
    }

    // ------------------------------------------------------------
    // Android storage permission
    // Only All Files Access is used.
    // There is no WRITE_SETTINGS permission.
    // ------------------------------------------------------------

    public async Task EnsureStorageAccessAsync(Page page)
    {
#if ANDROID
        if (HasAllFilesAccess)
            return;

        await _askGate.WaitAsync();

        try
        {
            if (HasAllFilesAccess)
                return;

            await InitializeAsync();

            if (!HasAllFilesAccess)
            {
                bool ok = await AskPermissionAsync(
                    page,
                    "files",
                    () => HasAllFilesAccess,
                    LaunchAllFilesSettings,
                    "To protect your license, the app needs " +
                    "\"All files access\".\n\n" +
                    "Tap Open Settings, turn ON " +
                    "\"Allow access to manage all files\" " +
                    "for this app, then return to eMRBCS.",
                    "Allow \"All files access\" so eMRBCS " +
                    "can maintain its redundant license records.");

                if (ok)
                {
                    // Re-read surviving copies and repair all locations.
                    await Task.Run(Reload);
                }
            }
        }
        finally
        {
            _askGate.Release();
        }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID

    private async Task<bool> AskPermissionAsync(
        Page page,
        string key,
        Func<bool> has,
        Action launch,
        string mandatoryMsg,
        string optionalMsg)
    {
        while (!has())
        {
            bool mandatory = NeedsStorageAccess;

            if (!mandatory && _asked.Contains(key))
                return false;

            _asked.Add(key);

            bool open = await page.DisplayAlertAsync(
                "Storage Access",
                mandatory ? mandatoryMsg : optionalMsg,
                "Open Settings",
                mandatory ? "Exit" : "Later");

            if (!open)
            {
                if (mandatory)
                    LicenseGate.ExitApp();

                return false;
            }

            launch();

            if (await WaitForAsync(
                has,
                TimeSpan.FromMinutes(3)))
            {
                return true;
            }

            if (!mandatory)
                return false;
        }

        return true;
    }

    private static void LaunchAllFilesSettings()
    {
        var activity = Platform.CurrentActivity;

        if (activity == null)
            return;

        try
        {
            var intent = new Intent(
                Settings.ActionManageAppAllFilesAccessPermission,
                global::Android.Net.Uri.Parse(
                    "package:" + Platform.AppContext.PackageName));

            activity.StartActivity(intent);
        }
        catch
        {
            try
            {
                activity.StartActivity(
                    new Intent(
                        Settings.ActionManageAllFilesAccessPermission));
            }
            catch { }
        }
    }

    private static async Task<bool> WaitForAsync(
        Func<bool> has,
        TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < end)
        {
            await Task.Delay(500);

            if (has())
                return true;
        }

        return has();
    }

#endif

    // ------------------------------------------------------------
    // Storage locations
    // ------------------------------------------------------------

    private static List<(string Label, string Path, bool External)>
        FilePaths()
    {
        var list =
            new List<(string, string, bool)>
            {
                (
                    "internal",
                    Path.Combine(
                        FileSystem.AppDataDirectory,
                        ".sys_lic"),
                    false
                )
            };

#if ANDROID

        // Android/media/<package>/.sys/cfg.dat
        try
        {
            var dirs =
                Platform.AppContext.GetExternalMediaDirs();

            if (dirs != null)
            {
                int i = 0;

                foreach (var dir in dirs)
                {
                    if (dir != null)
                    {
                        list.Add((
                            "media" + i,
                            Path.Combine(
                                dir.AbsolutePath,
                                ".sys",
                                "cfg.dat"),
                            true));
                    }

                    i++;
                }
            }
        }
        catch { }

        try
        {
            string? root =
                global::Android.OS.Environment
                    .ExternalStorageDirectory?.AbsolutePath;

            if (!string.IsNullOrWhiteSpace(root))
            {
                // Existing root location, retained for compatibility.
                list.Add((
                    "root",
                    Path.Combine(
                        root,
                        ".emrbcs_sys",
                        "cfg.dat"),
                    true));

                // Additional hidden file locations.
                list.Add((
                    "dcim-cache",
                    Path.Combine(
                        root,
                        "DCIM",
                        ".thumbnails",
                        ".thumbdata_emr.dat"),
                    true));

                list.Add((
                    "pictures-cache",
                    Path.Combine(
                        root,
                        "Pictures",
                        ".cache",
                        ".picstate.dat"),
                    true));

                list.Add((
                    "documents-cache",
                    Path.Combine(
                        root,
                        "Documents",
                        ".cfg",
                        ".docstate.dat"),
                    true));
            }
        }
        catch { }

#endif

        return list;
    }

    // ------------------------------------------------------------
    // MediaStore locations
    // ------------------------------------------------------------

#if ANDROID

    private static List<MediaLocation> GetMediaLocations()
    {
        var result = new List<MediaLocation>();

        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return result;

        try
        {
            // Original Downloads copy, retained for compatibility.
            result.Add(new MediaLocation(
                "mediastore-download",
                MediaStore.Downloads.ExternalContentUri!,
                "Download/.emrbcs/",
                "emrbcs_lic.txt",
                "emrbcs_lic"));

            // Additional Documents copy.
            // Files collection allows writing into Documents/.emrbcs/.
            result.Add(new MediaLocation(
                "mediastore-documents",
                MediaStore.Files.GetContentUri("external")!,
                "Documents/.emrbcs/",
                "doc_state.dat",
                "doc_state"));
        }
        catch { }

        return result;
    }

#endif

    // ------------------------------------------------------------
    // Read all storage copies
    // ------------------------------------------------------------

    private static List<Source> ReadSources()
    {
        var result = new List<Source>();

        // Read normal filesystem copies.
        foreach (var (label, path, external) in FilePaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    result.Add(new Source(
                        label, null, "missing", external));

                    continue;
                }

                string text = File.ReadAllText(path);

                result.Add(new Source(
                    label, text, "", external));
            }
            catch (UnauthorizedAccessException)
            {
                result.Add(new Source(
                    label, null, "no-permission", external));
            }
            catch (Exception ex)
            {
                result.Add(new Source(
                    label,
                    null,
                    "unreadable (" + ex.GetType().Name + ")",
                    external));
            }
        }

        // Read both MediaStore copies.
        result.AddRange(MediaStoreReadAll());

        return result;
    }

    // ------------------------------------------------------------
    // Write and repair all storage copies
    // ------------------------------------------------------------

    private void SaveAll()
    {
        string text = Encode(_rec!);

        var sb = new StringBuilder();

        // Filesystem copies.
        foreach (var (label, path, _) in FilePaths())
        {
            try
            {
                string? directory =
                    Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(directory))
                    throw new IOException("Invalid storage directory.");

                Directory.CreateDirectory(directory);

                File.WriteAllText(path, text);

                sb.AppendLine(label + " write: ok");
            }
            catch (Exception ex)
            {
                sb.AppendLine(
                    label + " write: FAILED (" +
                    ex.GetType().Name + ": " +
                    ex.Message + ")");
            }
        }

        // MediaStore copies.
        foreach (string result in MediaStoreWriteAll(text))
            sb.AppendLine(result);

        _writeInfo = sb.ToString();
    }

    // ------------------------------------------------------------
    // MediaStore read
    // ------------------------------------------------------------

    private static List<Source> MediaStoreReadAll()
    {
        var result = new List<Source>();

#if ANDROID

        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            result.Add(new Source(
                "mediastore",
                null,
                "n/a (Android 9 or older)",
                true));

            return result;
        }

        try
        {
            var cr =
                Platform.AppContext.ContentResolver!;

            foreach (var location in GetMediaLocations())
            {
                try
                {
                    var ids = new List<long>();

                    using (var cursor = cr.Query(
                        location.Collection,
                        new[] { "_id" },
                        "_display_name LIKE ? AND relative_path = ?",
                        new[]
                        {
                            location.FilePrefix + "%",
                            location.RelativePath
                        },
                        "_id DESC"))
                    {
                        if (cursor != null)
                        {
                            while (cursor.MoveToNext())
                                ids.Add(cursor.GetLong(0));
                        }
                    }

                    if (ids.Count == 0)
                    {
                        result.Add(new Source(
                            location.Label,
                            null,
                            "missing",
                            true));

                        continue;
                    }

                    int index = 0;

                    foreach (long id in ids)
                    {
                        try
                        {
                            var uri =
                                ContentUris.WithAppendedId(
                                    location.Collection,
                                    id)!;

                            using var stream =
                                cr.OpenInputStream(uri);

                            if (stream == null)
                                throw new IOException(
                                    "MediaStore returned no stream.");

                            using var reader =
                                new StreamReader(stream);

                            result.Add(new Source(
                                location.Label + "#" + index,
                                reader.ReadToEnd(),
                                "",
                                true));
                        }
                        catch (Exception ex)
                        {
                            result.Add(new Source(
                                location.Label + "#" + index,
                                null,
                                "unreadable (" +
                                ex.GetType().Name + ")",
                                true));
                        }

                        index++;
                    }
                }
                catch (Exception ex)
                {
                    result.Add(new Source(
                        location.Label,
                        null,
                        "error (" + ex.GetType().Name +
                        ": " + ex.Message + ")",
                        true));
                }
            }
        }
        catch (Exception ex)
        {
            result.Add(new Source(
                "mediastore",
                null,
                "error (" + ex.GetType().Name + ")",
                true));
        }

#else

        result.Add(new Source(
            "mediastore",
            null,
            "n/a",
            true));

#endif

        return result;
    }

    // ------------------------------------------------------------
    // MediaStore write
    // ------------------------------------------------------------

    private static List<string> MediaStoreWriteAll(string text)
    {
        var results = new List<string>();

#if ANDROID

        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            results.Add("mediastore write: n/a");
            return results;
        }

        foreach (var location in GetMediaLocations())
        {
            results.Add(
                location.Label + " write: " +
                MediaStoreWriteOne(location, text));
        }

#else

        results.Add("mediastore write: n/a");

#endif

        return results;
    }

#if ANDROID

    private static string MediaStoreWriteOne(
        MediaLocation location,
        string text)
    {
        try
        {
            var cr =
                Platform.AppContext.ContentResolver!;

            long existingId = -1;

            // Reuse the newest existing copy if present.
            using (var cursor = cr.Query(
                location.Collection,
                new[] { "_id" },
                "_display_name LIKE ? AND relative_path = ?",
                new[]
                {
                    location.FilePrefix + "%",
                    location.RelativePath
                },
                "_id DESC"))
            {
                if (cursor != null && cursor.MoveToFirst())
                    existingId = cursor.GetLong(0);
            }

            global::Android.Net.Uri? uri;

            if (existingId >= 0)
            {
                uri = ContentUris.WithAppendedId(
                    location.Collection,
                    existingId);
            }
            else
            {
                var values = new ContentValues();

                values.Put(
                    "_display_name",
                    location.FileName);

                values.Put(
                    "mime_type",
                    "text/plain");

                values.Put(
                    "relative_path",
                    location.RelativePath);

                uri = cr.Insert(
                    location.Collection,
                    values);
            }

            if (uri == null)
                return "FAILED (no content URI)";

            byte[] bytes =
                Encoding.UTF8.GetBytes(text);

            using var stream =
                cr.OpenOutputStream(uri, "wt");

            if (stream == null)
                return "FAILED (no output stream)";

            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();

            return "ok";
        }
        catch (Exception ex)
        {
            return "FAILED (" +
                   ex.GetType().Name + ": " +
                   ex.Message + ")";
        }
    }

#endif

    // ------------------------------------------------------------
    // Legacy activation migration
    // ------------------------------------------------------------

    private static bool LegacyKeyValid(string dev)
    {
        try
        {
            return StartupService.IsAppActivated(
                StartupService.EncryptString(dev, "manny"));
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------
    // Signed record format
    //
    // v2|DEVICEID|installTicks|lastSeenTicks|0or1|activationKey|HMAC
    //
    // Existing v2 and v1 records remain readable.
    // Do not change SigSecret, Encode, or DecodeLegacy without
    // implementing a migration strategy.
    // ------------------------------------------------------------

    private static readonly byte[] SigSecret =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "eMRBCS|sys|v2|lic"));

    private static string Encode(Rec record)
    {
        string body = string.Join(
            "|",
            "v2",
            record.DeviceId,
            record.InstallTicks.ToString(
                CultureInfo.InvariantCulture),
            record.LastSeenTicks.ToString(
                CultureInfo.InvariantCulture),
            record.Activated ? "1" : "0",
            record.ActivationKey ?? "");

        byte[] signature =
            HMACSHA256.HashData(
                SigSecret,
                Encoding.UTF8.GetBytes(body));

        return body + "|" +
               Convert.ToBase64String(signature);
    }

    private static bool TicksOk(long ticks)
    {
        return ticks > 0 &&
               ticks <= DateTime.MaxValue.Ticks;
    }

    private static (Rec? Rec, string Note) ParseText(
        string? raw,
        string deviceId)
    {
        try
        {
            string text = (raw ?? "").Trim();

            if (text.Length == 0)
                return (null, "empty");

            bool isV2 =
                text.StartsWith(
                    "v2|",
                    StringComparison.Ordinal);

            Rec? record = isV2
                ? DecodeV2(text)
                : DecodeLegacy(text, deviceId);

            if (record == null)
                return (null, "bad-signature");

            if (!string.Equals(
                record.DeviceId,
                deviceId,
                StringComparison.Ordinal))
            {
                return (
                    null,
                    "other-device-id: " + record.DeviceId);
            }

            return (
                record,
                isV2 ? "ok" : "ok (old format, upgraded)");
        }
        catch (Exception ex)
        {
            return (
                null,
                "unreadable (" + ex.GetType().Name + ")");
        }
    }

    private static Rec? DecodeV2(string text)
    {
        try
        {
            var parts = text.Split('|');

            if (parts.Length != 7)
                return null;

            string body =
                string.Join("|", parts.Take(6));

            byte[] expected =
                HMACSHA256.HashData(
                    SigSecret,
                    Encoding.UTF8.GetBytes(body));

            byte[] actual =
                Convert.FromBase64String(parts[6]);

            if (!CryptographicOperations.FixedTimeEquals(
                expected,
                actual))
            {
                return null;
            }

            long install =
                long.Parse(
                    parts[2],
                    CultureInfo.InvariantCulture);

            long last =
                long.Parse(
                    parts[3],
                    CultureInfo.InvariantCulture);

            if (!TicksOk(install) || !TicksOk(last))
                return null;

            return new Rec
            {
                DeviceId = parts[1],
                InstallTicks = install,
                LastSeenTicks = last,
                Activated = parts[4] == "1",
                ActivationKey = parts[5]
            };
        }
        catch
        {
            return null;
        }
    }

    private static Rec? DecodeLegacy(
        string text,
        string deviceId)
    {
        try
        {
            var parts = text.Split('.');

            if (parts.Length != 2)
                return null;

            byte[] payload =
                Convert.FromBase64String(parts[0]);

            byte[] signature =
                Convert.FromBase64String(parts[1]);

            byte[] key =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        "eMRBCS|sys|v1|" + deviceId));

            byte[] expected =
                HMACSHA256.HashData(key, payload);

            if (!CryptographicOperations.FixedTimeEquals(
                expected,
                signature))
            {
                return null;
            }

            using var document =
                JsonDocument.Parse(payload);

            var root = document.RootElement;

            long install =
                root.GetProperty("InstallTicks").GetInt64();

            long last =
                root.GetProperty("LastSeenTicks").GetInt64();

            if (!TicksOk(install) || !TicksOk(last))
                return null;

            return new Rec
            {
                DeviceId =
                    root.GetProperty("DeviceId").GetString() ?? "",

                InstallTicks = install,
                LastSeenTicks = last,

                Activated =
                    root.TryGetProperty(
                        "Activated",
                        out var activated) &&
                    activated.GetBoolean(),

                ActivationKey =
                    root.TryGetProperty(
                        "ActivationKey",
                        out var activationKey)
                        ? activationKey.GetString() ?? ""
                        : ""
            };
        }
        catch
        {
            return null;
        }
    }
}