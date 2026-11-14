using eMRBCS.Global;
using eMRBCS.Services;
using MSRBC.Services;
namespace eMRBCS.Pages;

public partial class AppSettings : ContentPage
{
    DBService dBService;
    FileService fileService;
    private bool _isFormattingIp = false;
    private string _lastIpDisplay = string.Empty;

    public AppSettings()
    {
        InitializeComponent();

        import_ipaddress.Text = Serverdetails.ImportServerIPAddress;
        import_sharedfolder.Text = Serverdetails.ImportSharedFolder;
        import_username.Text = Serverdetails.ImportServerUserName;
        import_pass.Text = Serverdetails.ImportServerPassword;

        CheckServerStatus();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ServerStatusMonitor.Instance.StatusChanged += OnServerStatusChanged;
        ServerStatusMonitor.Instance.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ServerStatusMonitor.Instance.StatusChanged -= OnServerStatusChanged;
        ServerStatusMonitor.Instance.Stop();
    }

    private void OnServerStatusChanged(bool ok)
        => MainThread.BeginInvokeOnMainThread(() =>
            frameStatus.BackgroundColor = ok ? Colors.Green : Colors.Red);

    private static string BuildIpDisplay(string raw, bool isDeleting)
    {
        string filtered = new string(raw.Where(c => char.IsDigit(c) || c == '.').ToArray());

        while (filtered.Contains(".."))
            filtered = filtered.Replace("..", ".");
        while (filtered.StartsWith("."))
            filtered = filtered.Substring(1);

        var parts = filtered.Split('.');
        if (parts.Length > 4)
            parts = parts.Take(4).ToArray();

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 3)
                parts[i] = parts[i].Substring(0, 3);
        }

        string result = string.Join(".", parts);

        if (!isDeleting && parts.Length < 4 && parts.Last().Length == 3 && !result.EndsWith("."))
        {
            result += ".";
        }

        return result;
    }

    private void OnIpAddressChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormattingIp) return;

        var entry = (Entry)sender;
        string prev = _lastIpDisplay;
        string raw = e.NewTextValue ?? string.Empty;

        bool isDeleting = raw.Length < prev.Length;

        string display = BuildIpDisplay(raw, isDeleting);

        _lastIpDisplay = display;

        if (display != raw)
        {
            _isFormattingIp = true;
            entry.Text = display;
            _isFormattingIp = false;
        }

        Dispatcher.Dispatch(() => entry.CursorPosition = display.Length);
    }

    private void OnToggleImportPassword(object sender, EventArgs e)
    {
        import_pass.IsPassword = !import_pass.IsPassword;
        btnToggleImportPass.Source = import_pass.IsPassword
            ? "menuicons/eye.png"
            : "menuicons/eye_off.png";
    }

    private async void CheckServerStatus()
    {
        fileService = new FileService();
        frameStatus.BackgroundColor = Colors.Red;
        if (Serverdetails.Validnetwork == 1)
        {
            bool isConnected = await fileService.CheckSMBStatus();
            if (isConnected)
            {
                frameStatus.BackgroundColor = Colors.Green;
            }
        }
    }

    public async void OnSaveChanges(object sender, EventArgs e)
    {
        string new_ip = import_ipaddress.Text;
        string new_folder = (import_sharedfolder.Text ?? "").Trim();
        string new_user = import_username.Text;
        string new_pass = import_pass.Text;

        Serverdetails.ImportServerIPAddress = new_ip;
        Serverdetails.ImportSharedFolder = new_folder;
        Serverdetails.ImportServerUserName = new_user;
        Serverdetails.ImportServerPassword = new_pass;

        // The export side always uses the same server (SOURCE / OUTPUT are fixed sub-folders)
        Serverdetails.ExportServerIPAddress = new_ip;
        Serverdetails.ExportSharedFolder = new_folder;
        Serverdetails.ExportServerUserName = new_user;
        Serverdetails.ExportServerPassword = new_pass;

        dBService = new DBService();

        bool isConfigSave = await dBService.NetworkConfigSave();
        if (isConfigSave)
        {
            Serverdetails.Validnetwork = 1;
            ServerStatusMonitor.Instance.RequestCheck();
            CheckServerStatus();
            await DisplayAlertAsync("Notice",
                "Server Configuration has been saved!\n\n", "Ok");
        }
        else
        {
            await DisplayAlertAsync("Error", "Server Configuration failed to saved!", "Ok");
        }
    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PopOnceAsync();
    }
}