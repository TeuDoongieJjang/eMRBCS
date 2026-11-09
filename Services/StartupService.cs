using eMRBCS.Global;
using MSRBC.Services;
using System.Security.Cryptography;
using System.Text;

namespace eMRBCS.Services;

public sealed class StartupService
{
    public static StartupService Instance { get; } = new();
    private bool _started;

    /// DB copied + network config + activation. Login waits only for this.
    public Task CoreReady { get; private set; } = Task.CompletedTask;
    public Task UsersReady { get; private set; } = Task.CompletedTask;
    public Task ConsumersReady { get; private set; } = Task.CompletedTask;
    public Task PrinterReady { get; private set; } = Task.CompletedTask;

    public static void CaptureDeviceInfo()
    {
        var d = DeviceDisplay.MainDisplayInfo;
        string id = string.Empty;
#if ANDROID
        id = Android.Provider.Settings.Secure.GetString(
            Android.App.Application.Context.ContentResolver,
            Android.Provider.Settings.Secure.AndroidId);
#endif
        MyDeviceInfo.device_widthpix = d.Width;
        MyDeviceInfo.device_heightpix = d.Height;
        MyDeviceInfo.device_density = d.Density;
        MyDeviceInfo.device_width = d.Width / d.Density;
        MyDeviceInfo.device_height = d.Height / d.Density;
        MyDeviceInfo.device_AndroidID = id;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        ConsumerStore.Instance.BeginLoading();

        CoreReady = Task.Run(RunCoreAsync);
        // These four run in parallel as soon as Core is done.
        UsersReady = After(CoreReady, RunUsersAsync);
        ConsumersReady = After(CoreReady, RunConsumersAsync);
        PrinterReady = After(CoreReady, RunPrinterAsync);
    }

    public async Task WaitForUsersAsync()
    {
        try { await UsersReady.WaitAsync(TimeSpan.FromSeconds(20)); } catch { }
    }

    private static async Task After(Task first, Func<Task> next)
    {
        try { await first.ConfigureAwait(false); } catch { }
        try { await next().ConfigureAwait(false); } catch { }
    }

    private async Task RunCoreAsync()
    {
        try
        {
            string target = Path.Combine(FileSystem.AppDataDirectory, database_info.dbName);
            if (!File.Exists(target))
            {
                using var input = await FileSystem.OpenAppPackageFileAsync(database_info.packageDBfile);
                using var output = File.Create(target);
                await input.CopyToAsync(output);
            }

            var net = await new DBService().GetExistingNetwork();
            if (net == null) { Serverdetails.Validnetwork = 0; return; }

            Serverdetails.NetworkID = net.id;
            Serverdetails.ImportServerIPAddress = net.import_ipaddress;
            Serverdetails.ImportSharedFolder = net.import_folder;
            Serverdetails.ImportServerUserName = net.import_user;
            Serverdetails.ImportServerPassword = net.import_pass;   // was export_pass (bug)
            Serverdetails.ExportServerIPAddress = net.export_ipaddress;
            Serverdetails.ExportSharedFolder = net.export_folder;
            Serverdetails.ExportServerUserName = net.export_user;
            Serverdetails.ExportServerPassword = net.export_pass;
            Serverdetails.Validnetwork = net.import_ipaddress != "0.0.0.0" ? 1 : 0;

            string key = EncryptString((MyDeviceInfo.device_AndroidID ?? "").ToUpper(), "manny");
            MyDeviceInfo.isActivated = IsAppActivated(key);
        }
        catch (Exception ex)
        {
            Serverdetails.Validnetwork = 0;
            System.Diagnostics.Debug.WriteLine("Startup core failed: " + ex);
        }
    }

    private async Task RunUsersAsync()
    {
        if (Serverdetails.Validnetwork != 1) return;
        var fs = new FileService();
        string r = await fs.ImportFileAsync(3).WaitAsync(TimeSpan.FromSeconds(20));
        if (r.Split(';')[0].Trim() == "success")
            await new DBService().UserReadAndProcessileAsync(fs.userfile);
    }

    private Task RunConsumersAsync() => new DBService().RequestForConsumers();

    private async Task RunPrinterAsync()
        => MyDeviceInfo.currentPrintConfig = await new DBService().GetDefaultPrinterConfig();

    // ---- moved here from LoadingScreen ----
    public static string EncryptString(string plainText, string passphrase)
    {
        using var aes = Aes.Create();
        var kdf = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(passphrase), 10000);
        aes.Key = kdf.GetBytes(32);
        aes.IV = kdf.GetBytes(16);
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs)) sw.Write(plainText);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static bool IsAppActivated(string productKey)
    {
        string path = Path.Combine(FileSystem.AppDataDirectory, MyDeviceInfo.activation_file);
        return File.Exists(path) && File.ReadAllText(path) == productKey;
    }
}