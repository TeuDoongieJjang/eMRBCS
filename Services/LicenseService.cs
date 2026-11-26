using eMRBCS.Global;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// 3-month demo + license activation. LOCAL storage only (nothing is sent to / stored on the server).
///
/// Copies of one signed record are kept in:
///   1. FileSystem.AppDataDirectory            (wiped on uninstall)
///   2. Android/media/com.mls.emrbcs/.sys/     (SURVIVES uninstall / reinstall, no permission needed)
/// On every start the copies are merged and any missing copy is restored.
/// </summary>
public sealed class LicenseService
{
    public static LicenseService Instance { get; } = new();
    private LicenseService() { }

    public const int DemoMonths = 3;

    private static readonly TimeSpan SaveStep = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private Rec? _rec;
    private bool _tampered;

    /// <summary>Debug text: how many license copies were found / valid.</summary>
    public string StorageInfo { get; private set; } = "";

    /// <summary>The "generated address" / Product Key shown to the user and sent to the key generator.</summary>
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
    //  Build-time floor (see csproj <AssemblyMetadata Include="BuildUtc" .../>)
    //  The device clock can never be earlier than the moment this APK was built.
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

    public void Initialize()
    {
        lock (_gate)
        {
            if (_rec != null) return;

            string dev = DeviceId;
            string expectedKey = ComputeActivationKey(dev);
            var paths = StoragePaths();

            var valid = new List<Rec>();
            int found = 0, invalid = 0;
            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;
                found++;
                var r = Decode(SafeRead(p), dev);
                if (r == null) invalid++; else valid.Add(r);
            }

            DateTime now = DateTime.UtcNow;
            if (now < BuildUtc) now = BuildUtc;

            Rec rec;
            if (valid.Count > 0)
            {
                // merge: earliest install wins, latest "last seen" wins, activation must match this device's key
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
            else if (invalid > 0)
            {
                // files exist but none is valid (edited / copied from another device) -> locked
                _tampered = true;
                rec = new Rec { DeviceId = dev, InstallTicks = 0, LastSeenTicks = now.Ticks };
            }
            else
            {
                // nothing found anywhere: first run (or very first install of the licensed version)
                rec = new Rec { DeviceId = dev, InstallTicks = now.Ticks, LastSeenTicks = now.Ticks };

                // migrate devices activated with the OLD file (eMRBCS.key)
                if (LegacyKeyValid(dev))
                {
                    rec.Activated = true;
                    rec.ActivationKey = expectedKey;
                }
            }

            _rec = rec;
            SaveAll();
            StorageInfo = $"paths={paths.Count}, found={found}, valid={valid.Count}, invalid={invalid}";
        }
    }

    public LicenseStatus GetStatus()
    {
        Initialize();
        lock (_gate)
        {

            var rec = _rec!;
            var lastSeen = new DateTime(rec.LastSeenTicks, DateTimeKind.Utc);
            DateTime raw = DateTime.UtcNow;

            bool rolledBack = raw < lastSeen - ClockSlack;

            DateTime now = raw;
            if (now < lastSeen) now = lastSeen;
            if (now < BuildUtc) now = BuildUtc;

            if (!rolledBack && now - lastSeen >= SaveStep)
            {
                rec.LastSeenTicks = now.Ticks;
                SaveAll();
            }

            var install = new DateTime(rec.InstallTicks, DateTimeKind.Utc);
            var expires = install.AddMonths(DemoMonths);
            int daysLeft = Math.Max(0, (int)Math.Ceiling((expires - now).TotalDays));

            LicenseState state;
            if (rec.Activated) state = LicenseState.Activated;
            else if (rolledBack) state = LicenseState.Expired;     // clock set back: locked until corrected
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
    //  Storage
    // ------------------------------------------------------------------
    private static List<string> StoragePaths()
    {
        var list = new List<string> { Path.Combine(FileSystem.AppDataDirectory, ".sys_lic") };
#if ANDROID
        try
        {
            // Android/media/<package>/ : NOT removed on uninstall, readable again after reinstall
            var dirs = Platform.AppContext.GetExternalMediaDirs();
            if (dirs != null)
                foreach (var d in dirs)
                    if (d != null) list.Add(Path.Combine(d.AbsolutePath, ".sys", "cfg.dat"));
        }
        catch { }
#endif
        return list;
    }

    private void SaveAll()
    {
        string text = Encode(_rec!);
        foreach (var p in StoragePaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, text);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("License save failed: " + p + " " + ex.Message); }
        }
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return ""; }
    }

    private static bool LegacyKeyValid(string dev)
    {
        try { return StartupService.IsAppActivated(StartupService.EncryptString(dev, "manny")); }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    //  Signed record  (base64(json) + "." + base64(HMACSHA256))
    // ------------------------------------------------------------------
    private static byte[] SigKey(string dev) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("eMRBCS|sys|v1|" + dev));

    private static string Encode(Rec r)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(r);
        byte[] sig = HMACSHA256.HashData(SigKey(r.DeviceId), payload);
        return Convert.ToBase64String(payload) + "." + Convert.ToBase64String(sig);
    }

    private static Rec? Decode(string text, string dev)
    {
        try
        {
            var parts = (text ?? "").Trim().Split('.');
            if (parts.Length != 2) return null;
            byte[] payload = Convert.FromBase64String(parts[0]);
            byte[] sig = Convert.FromBase64String(parts[1]);
            byte[] expected = HMACSHA256.HashData(SigKey(dev), payload);
            if (!CryptographicOperations.FixedTimeEquals(expected, sig)) return null;
            var r = JsonSerializer.Deserialize<Rec>(payload);
            return r != null && r.DeviceId == dev ? r : null;
        }
        catch { return null; }
    }
}