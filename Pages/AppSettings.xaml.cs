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

        export_ipaddress.Text = Serverdetails.ExportServerIPAddress;
        export_sharedfolder.Text = Serverdetails.ExportSharedFolder;
        export_username.Text = Serverdetails.ExportServerUserName;
        export_pass.Text = Serverdetails.ExportServerPassword;
        CheckServerStatus();
        // GetExternalStorage();

    }

    private static string BuildIpDisplay(string raw, bool isDeleting)
    {
        // 1. Keep only digits and dots
        string filtered = new string(raw.Where(c => char.IsDigit(c) || c == '.').ToArray());

        // 2. Remove consecutive dots and leading dots
        while (filtered.Contains(".."))
            filtered = filtered.Replace("..", ".");
        while (filtered.StartsWith("."))
            filtered = filtered.Substring(1);

        // 3. Split into parts (max 4 parts)
        var parts = filtered.Split('.');
        if (parts.Length > 4)
            parts = parts.Take(4).ToArray();

        // 4. Enforce max 3 digits per part
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 3)
                parts[i] = parts[i].Substring(0, 3);
        }

        string result = string.Join(".", parts);

        // 5. Auto-append dot if typing (not deleting), last part is 3 digits, and we don't have 4 parts yet
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

        // Forces the cursor to stay at the end (after the auto-period)
        Dispatcher.Dispatch(() => entry.CursorPosition = display.Length);
    }

    private void OnToggleImportPassword(object sender, EventArgs e)
    {
        import_pass.IsPassword = !import_pass.IsPassword;
        btnToggleImportPass.Source = import_pass.IsPassword
            ? "menuicons/eye.png"
            : "menuicons/eye_off.png";
    }

    private void OnToggleExportPassword(object sender, EventArgs e)
    {
        export_pass.IsPassword = !export_pass.IsPassword;
        btnToggleExportPass.Source = export_pass.IsPassword
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
        string new_importipaddress = import_ipaddress.Text;
        string new_importfolder = (import_sharedfolder.Text ?? "").Trim();
        string new_importusername = import_username.Text;
        string new_importpassword = import_pass.Text;

        string new_exportipaddress = export_ipaddress.Text;
        string new_exportfolder = (export_sharedfolder.Text ?? "").Trim();
        string new_exportusername = export_username.Text;
        string new_exportpassword = export_pass.Text;

        Serverdetails.ImportServerIPAddress = new_importipaddress;
        Serverdetails.ImportSharedFolder = new_importfolder;
        Serverdetails.ImportServerUserName = new_importusername;
        Serverdetails.ImportServerPassword = new_importpassword;

        Serverdetails.ExportServerIPAddress = new_exportipaddress;
        Serverdetails.ExportSharedFolder = new_exportfolder;
        Serverdetails.ExportServerUserName = new_exportusername;
        Serverdetails.ExportServerPassword = new_exportpassword;

        dBService = new DBService();

        bool isConfigSave = await dBService.NetworkConfigSave();
        if (isConfigSave)
        {
            Serverdetails.Validnetwork = 1;
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

    private void OnCheckSameDetails(object sender, EventArgs e)
    {
        if (chkSameDetails.IsChecked)
        {
            export_ipaddress.Text = import_ipaddress.Text;
            export_sharedfolder.Text = import_sharedfolder.Text;
            export_username.Text = import_username.Text;
            export_pass.Text = import_pass.Text;

            export_ipaddress.IsReadOnly = true;
            export_sharedfolder.IsReadOnly = true;
            export_username.IsReadOnly = true;
            export_pass.IsReadOnly = true;
        }
        else
        {
            export_ipaddress.IsReadOnly = false;
            export_sharedfolder.IsReadOnly = false;
            export_username.IsReadOnly = false;
            export_pass.IsReadOnly = false;
        }


    }




}