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

public enum LicenseState { Demo, Activated, Expired }

public sealed record LicenseStatus(
    LicenseState State,
    DateTime InstallUtc,
    DateTime ExpiresUtc,
    int DaysLeft,
    bool ClockRolledBack,
    bool RecordTampered);

/// <summary>
/// 3-month demo + license activation. LOCAL storage only.
///
/// One signed record is written to SEVERAL places and merged on every start
/// (earliest install date wins, latest "last seen" wins, any valid activation wins):
///   internal   AppDataDirectory                               survives UPDATE
///   media      Android/media/com.mls.emrbcs/.sys              survives uninstall on most phones (Honeywell), NOT on Samsung
///   mediastore Download/.emrbcs/emrbcs_lic.txt (MediaStore)   survives uninstall, no permission (Android 10+)
///   root       /storage/emulated/0/.emrbcs_sys/cfg.dat        survives uninstall on EVERY phone, needs "All files access" (Android 11+)
///
/// Android 11+ : the app asks ONCE for "All files access" (EnsureStorageAccessAsync, called from Login).
/// That permission is what makes the license survive uninstall/reinstall on Samsung and other newer phones.
/// </summary>
public sealed class LicenseService
{
    public static LicenseService Instance { get; } = new();
    private LicenseService() { }

    public const int DemoMonths = 3;

    /// <summary>true = on Android 11+ a NEW install (no earlier license copy found) cannot start until "All files access" is granted.
    /// This is what stops "uninstall + reinstall = new demo". Set false to make the permission optional.</summary>
    public static bool EnforceStorageAccess = true;

    private static readonly TimeSpan SaveStep = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _askGate = new(1, 1);
    private Rec? _rec;
    private bool _tampered;                 // kept for API compatibility (always false)
    private bool _foundExternal;            // a valid copy was found OUTSIDE the app's private storage
    private bool _askedThisSession;
    private string _readInfo = "";
    private string _writeInfo = "";

    /// <summary>Debug text: every storage location with its read / write result.</summary>
    public string StorageInfo =>
        (_readInfo + _writeInfo + "all-files-access=" + HasAllFilesAccess).TrimEnd();

    public string DeviceId
    {
        get
        {
            string id = MyDeviceInfo.device_AndroidID ?? "";
            return string.IsNullOrWhiteSpace(id) ? "UNKNOWN" : id.Trim().ToUpperInvariant();
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

    // ------------------------------------------------------------------
    //  Build-time floor
    // ------------------------------------------------------------------
    private static readonly DateTime BuildUtc = ReadBuildUtc();

    private static DateTime ReadBuildUtc()
    {
        try
        {
            var attr = typeof(LicenseService).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildUtc");
            if (attr?.Value != null &&
                DateTime.TryParseExact(attr.Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                return d;
        }
        catch { }
        return DateTime.MinValue;
    }

    // ------------------------------------------------------------------
    //  Activation key (SAME algorithm as ProductActivation / ActivateProduct)
    // ------------------------------------------------------------------
    public static string ComputeActivationKey(string productKeyUpper)
    {
        string encrypted = StartupService.EncryptString(productKeyUpper, "manny");
        string alnum = Regex.Replace(encrypted, "[^a-zA-Z0-9]", "");
        return (alnum.Length > 8 ? alnum.Substring(0, 8) : alnum).ToUpperInvariant();
    }

    // ------------------------------------------------------------------
    //  Public API
    // ------------------------------------------------------------------
    public Task InitializeAsync() => Task.Run(Initialize);

    public bool IsUsable => GetStatus().State != LicenseState.Expired;

    /// <summary>true on Android 10 and below, or when "All files access" is granted.</summary>
    public bool HasAllFilesAccess
    {
        get
        {
#if ANDROID
            try
            {
                return !OperatingSystem.IsAndroidVersionAtLeast(30)
                       || global::Android.OS.Environment.IsExternalStorageManager;
            }
            catch { return false; }
#else
            return true;
#endif
        }
    }

    /// <summary>New install on Android 11+ with no earlier license copy and no "All files access".</summary>
    public bool NeedsStorageAccess => EnforceStorageAccess && !HasAllFilesAccess && !_foundExternal;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_rec != null) return;

            string dev = DeviceId;
            string expectedKey = ComputeActivationKey(dev);

            var valid = new List<Rec>();
            var info = new StringBuilder();
            info.AppendLine("id=" + dev);
            _foundExternal = false;

            foreach (var s in ReadSources())
            {
                if (s.Text == null) { info.AppendLine(s.Label + ": " + s.Note); continue; }
                var (r, note) = ParseText(s.Text, dev);
                info.AppendLine(s.Label + ": " + note);
                if (r != null)
                {
                    valid.Add(r);
                    if (s.External) _foundExternal = true;
                }
            }
            _readInfo = info.ToString();

            DateTime raw = DateTime.UtcNow;
            DateTime start = raw < BuildUtc ? BuildUtc : raw;

            Rec rec;
            if (valid.Count > 0)
            {
                rec = new Rec
                {
                    DeviceId = dev,
                    InstallTicks = valid.Min(v => v.InstallTicks),
                    LastSeenTicks = valid.Max(v => v.LastSeenTicks)
                };
                if (valid.Any(v => v.Activated &&
                                   string.Equals(v.ActivationKey, expectedKey, StringComparison.OrdinalIgnoreCase)))
                {
                    rec.Activated = true;
                    rec.ActivationKey = expectedKey;
                }
            }
            else
            {
                rec = new Rec { DeviceId = dev, InstallTicks = start.Ticks, LastSeenTicks = start.Ticks };
                if (LegacyKeyValid(dev))
                {
                    rec.Activated = true;
                    rec.ActivationKey = expectedKey;
                }
            }

            _rec = rec;
            SaveAll();
        }
    }

    /// <summary>Forget the cached record and read every location again (after "All files access" was granted).</summary>
    public void Reload()
    {
        lock (_gate) { _rec = null; }
        Initialize();
    }

    public LicenseStatus GetStatus()
    {
        Initialize();
        lock (_gate)
        {
            var rec = _rec!;
            var lastSeen = new DateTime(rec.LastSeenTicks, DateTimeKind.Utc);
            DateTime raw = DateTime.UtcNow;

            bool clockBeforeBuild = BuildUtc != DateTime.MinValue && raw < BuildUtc - ClockSlack;
            bool rolledBack = clockBeforeBuild || raw < lastSeen - ClockSlack;

            DateTime now = raw < lastSeen ? lastSeen : raw;

            if (!rolledBack && raw - lastSeen >= SaveStep)
            {
                rec.LastSeenTicks = raw.Ticks;
                SaveAll();
            }

            var install = new DateTime(rec.InstallTicks, DateTimeKind.Utc);
            var expires = install.AddMonths(DemoMonths);
            int daysLeft = Math.Max(0, (int)Math.Ceiling((expires - now).TotalDays));

            LicenseState state;
            if (rec.Activated) state = LicenseState.Activated;
            else if (rolledBack) state = LicenseState.Expired;
            else state = now < expires ? LicenseState.Demo : LicenseState.Expired;

            return new LicenseStatus(state, install, expires, daysLeft, rolledBack, _tampered);
        }
    }

    public bool TryActivate(string? key)
    {
        Initialize();
        string k = (key ?? "").Trim().ToUpperInvariant();
        if (k.Length == 0) return false;
        if (!string.Equals(k, ComputeActivationKey(DeviceId), StringComparison.Ordinal)) return false;

        lock (_gate)
        {
            _rec!.Activated = true;
            _rec.ActivationKey = k;
            _tampered = false;
            SaveAll();
        }
        return true;
    }

    // ------------------------------------------------------------------
    //  "All files access" (Android 11+): call from Login.OnAppearing BEFORE the license state is applied
    // ------------------------------------------------------------------
    public async Task EnsureStorageAccessAsync(Page page)
    {
#if ANDROID
        if (HasAllFilesAccess) return;

        await _askGate.WaitAsync();
        try
        {
            if (HasAllFilesAccess) return;
            await InitializeAsync();                 // learn whether an earlier license copy exists

            while (true)
            {
                bool mandatory = NeedsStorageAccess;
                if (!mandatory && _askedThisSession) return;
                _askedThisSession = true;

                string msg = mandatory
                    ? "To protect your license (demo period / activation) the app needs \"All files access\".\n\n" +
                      "Tap Open Settings, turn ON \"Allow access to manage all files\" for this app, then come back."
                    : "Allow \"All files access\" so your license is kept even if the app is uninstalled and installed again.\n\n" +
                      "Tap Open Settings and turn it ON for this app.";

                bool open = await page.DisplayAlertAsync("Storage Access", msg, "Open Settings", mandatory ? "Exit" : "Later");
                if (!open)
                {
                    if (mandatory) LicenseGate.ExitApp();
                    return;
                }

                LaunchAllFilesSettings();
                bool granted = await WaitForAllFilesAsync(TimeSpan.FromMinutes(3));
                if (granted)
                {
                    await Task.Run(Reload);          // merge the copy stored in the root folder
                    return;
                }
                if (!mandatory) return;
            }
        }
        finally { _askGate.Release(); }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private static void LaunchAllFilesSettings()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return;
        try
        {
            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission,
                global::Android.Net.Uri.Parse("package:" + Platform.AppContext.PackageName));
            activity.StartActivity(intent);
        }
        catch
        {
            try { activity.StartActivity(new Intent(Settings.ActionManageAllFilesAccessPermission)); }
            catch { }
        }
    }

    private async Task<bool> WaitForAllFilesAsync(TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            await Task.Delay(500);
            if (HasAllFilesAccess) return true;
        }
        return HasAllFilesAccess;
    }
#endif

    // ------------------------------------------------------------------
    //  Storage
    // ------------------------------------------------------------------
    private const string MsName = "emrbcs_lic.txt";
    private const string MsPrefix = "emrbcs_lic";
    private const string MsRel = "Download/.emrbcs/";

    private static List<(string Label, string Path, bool External)> FilePaths()
    {
        var list = new List<(string, string, bool)>
        {
            ("internal", Path.Combine(FileSystem.AppDataDirectory, ".sys_lic"), false)
        };
#if ANDROID
        try
        {
            var dirs = Platform.AppContext.GetExternalMediaDirs();
            if (dirs != null)
            {
                int i = 0;
                foreach (var d in dirs)
                {
                    if (d != null) list.Add(("media" + i, Path.Combine(d.AbsolutePath, ".sys", "cfg.dat"), true));
                    i++;
                }
            }
        }
        catch { }

        try
        {
            var root = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
            if (!string.IsNullOrEmpty(root))
                list.Add(("root", Path.Combine(root, ".emrbcs_sys", "cfg.dat"), true));
        }
        catch { }
#endif
        return list;
    }

    private sealed record Source(string Label, string? Text, string Note, bool External);

    private static List<Source> ReadSources()
    {
        var res = new List<Source>();

        foreach (var (label, path, external) in FilePaths())
        {
            try
            {
                if (!File.Exists(path)) { res.Add(new Source(label, null, "missing", external)); continue; }
                string t = File.ReadAllText(path);
                res.Add(new Source(label, t, "", external));
            }
            catch (UnauthorizedAccessException) { res.Add(new Source(label, null, "no-permission", external)); }
            catch (Exception ex) { res.Add(new Source(label, null, "unreadable (" + ex.GetType().Name + ")", external)); }
        }

        res.AddRange(MediaStoreReadAll());
        return res;
    }

    private void SaveAll()
    {
        string text = Encode(_rec!);
        var sb = new StringBuilder();

        foreach (var (label, p, _) in FilePaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, text);
                sb.AppendLine(label + " write: ok");
            }
            catch (Exception ex)
            {
                sb.AppendLine(label + " write: FAILED (" + ex.GetType().Name + ")");
            }
        }

        sb.AppendLine("mediastore write: " + MediaStoreWrite(text));
        _writeInfo = sb.ToString();
    }

    // ---- MediaStore (Download/.emrbcs/) : created by this app, no permission, survives uninstall ----
    private static List<Source> MediaStoreReadAll()
    {
        var res = new List<Source>();
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                res.Add(new Source("mediastore", null, "n/a (Android 9 or older)", true));
                return res;
            }

            var cr = Platform.AppContext.ContentResolver!;
            var coll = MediaStore.Downloads.ExternalContentUri!;
            var ids = new List<long>();
            using (var c = cr.Query(coll, new[] { "_id" },
                       "_display_name LIKE ? AND relative_path LIKE ?",
                       new[] { MsPrefix + "%", MsRel + "%" }, "_id DESC"))
            {
                if (c != null) while (c.MoveToNext()) ids.Add(c.GetLong(0));
            }

            if (ids.Count == 0) { res.Add(new Source("mediastore", null, "missing", true)); return res; }

            int i = 0;
            foreach (long id in ids)
            {
                try
                {
                    var uri = ContentUris.WithAppendedId(coll, id)!;
                    using var s = cr.OpenInputStream(uri);
                    using var sr = new StreamReader(s!);
                    res.Add(new Source("mediastore#" + i, sr.ReadToEnd(), "", true));
                }
                catch (Exception ex)
                {
                    res.Add(new Source("mediastore#" + i, null, "unreadable (" + ex.GetType().Name + ")", true));
                }
                i++;
            }
        }
        catch (Exception ex)
        {
            res.Add(new Source("mediastore", null, "error (" + ex.GetType().Name + ")", true));
        }
#else
        res.Add(new Source("mediastore", null, "n/a", true));
#endif
        return res;
    }

    private static string MediaStoreWrite(string text)
    {
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return "n/a";

            var cr = Platform.AppContext.ContentResolver!;
            var coll = MediaStore.Downloads.ExternalContentUri!;

            long id = -1;
            using (var c = cr.Query(coll, new[] { "_id" },
                       "_display_name LIKE ? AND relative_path LIKE ?",
                       new[] { MsPrefix + "%", MsRel + "%" }, "_id DESC"))
            {
                if (c != null && c.MoveToFirst()) id = c.GetLong(0);
            }

            global::Android.Net.Uri? uri;
            if (id >= 0)
            {
                uri = ContentUris.WithAppendedId(coll, id);
            }
            else
            {
                var v = new ContentValues();
                v.Put("_display_name", MsName);
                v.Put("mime_type", "text/plain");
                v.Put("relative_path", MsRel);
                uri = cr.Insert(coll, v);
            }
            if (uri == null) return "FAILED (no uri)";

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using var os = cr.OpenOutputStream(uri, "wt");
            os!.Write(bytes, 0, bytes.Length);
            os.Flush();
            return "ok";
        }
        catch (Exception ex) { return "FAILED (" + ex.GetType().Name + ")"; }
#else
        return "n/a";
#endif
    }

    private static bool LegacyKeyValid(string dev)
    {
        try { return StartupService.IsAppActivated(StartupService.EncryptString(dev, "manny")); }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    //  Record format v2 (plain text):
    //     v2|DEVICEID|installTicks|lastSeenTicks|0or1|activationKey|base64(HMACSHA256)
    //  Format v1 (base64(json).base64(hmac)) is still readable.
    // ------------------------------------------------------------------
    private static readonly byte[] SigSecret =
        SHA256.HashData(Encoding.UTF8.GetBytes("eMRBCS|sys|v2|lic"));

    private static string Encode(Rec r)
    {
        string body = string.Join("|",
            "v2",
            r.DeviceId,
            r.InstallTicks.ToString(CultureInfo.InvariantCulture),
            r.LastSeenTicks.ToString(CultureInfo.InvariantCulture),
            r.Activated ? "1" : "0",
            r.ActivationKey ?? "");
        byte[] sig = HMACSHA256.HashData(SigSecret, Encoding.UTF8.GetBytes(body));
        return body + "|" + Convert.ToBase64String(sig);
    }

    private static bool TicksOk(long t) => t > 0 && t <= DateTime.MaxValue.Ticks;

    private static (Rec? Rec, string Note) ParseText(string? raw, string dev)
    {
        try
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return (null, "empty");

            bool v2 = text.StartsWith("v2|", StringComparison.Ordinal);
            Rec? r = v2 ? DecodeV2(text) : DecodeLegacy(text, dev);
            if (r == null) return (null, "bad-signature");
            if (!string.Equals(r.DeviceId, dev, StringComparison.Ordinal))
                return (null, "other-device-id (signing key / device changed): " + r.DeviceId);
            return (r, v2 ? "ok" : "ok (old format, upgraded)");
        }
        catch (Exception ex)
        {
            return (null, "unreadable (" + ex.GetType().Name + ")");
        }
    }

    private static Rec? DecodeV2(string text)
    {
        try
        {
            var parts = text.Split('|');
            if (parts.Length != 7) return null;

            string body = string.Join("|", parts.Take(6));
            byte[] expected = HMACSHA256.HashData(SigSecret, Encoding.UTF8.GetBytes(body));
            byte[] sig = Convert.FromBase64String(parts[6]);
            if (!CryptographicOperations.FixedTimeEquals(expected, sig)) return null;

            long install = long.Parse(parts[2], CultureInfo.InvariantCulture);
            long last = long.Parse(parts[3], CultureInfo.InvariantCulture);
            if (!TicksOk(install) || !TicksOk(last)) return null;

            return new Rec
            {
                DeviceId = parts[1],
                InstallTicks = install,
                LastSeenTicks = last,
                Activated = parts[4] == "1",
                ActivationKey = parts[5]
            };
        }
        catch { return null; }
    }

    private static Rec? DecodeLegacy(string text, string dev)
    {
        try
        {
            var parts = text.Split('.');
            if (parts.Length != 2) return null;
            byte[] payload = Convert.FromBase64String(parts[0]);
            byte[] sig = Convert.FromBase64String(parts[1]);

            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("eMRBCS|sys|v1|" + dev));
            byte[] expected = HMACSHA256.HashData(key, payload);
            if (!CryptographicOperations.FixedTimeEquals(expected, sig)) return null;

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            long install = root.GetProperty("InstallTicks").GetInt64();
            long last = root.GetProperty("LastSeenTicks").GetInt64();
            if (!TicksOk(install) || !TicksOk(last)) return null;

            return new Rec
            {
                DeviceId = root.GetProperty("DeviceId").GetString() ?? "",
                InstallTicks = install,
                LastSeenTicks = last,
                Activated = root.TryGetProperty("Activated", out var a) && a.GetBoolean(),
                ActivationKey = root.TryGetProperty("ActivationKey", out var k) ? (k.GetString() ?? "") : ""
            };
        }
        catch { return null; }
    }
}