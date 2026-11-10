using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using System.Text.RegularExpressions;


#if ANDROID
using eMRBCS.Platforms.Android;
#endif

namespace eMRBCS.Pages;

public partial class PrinterConfig : ContentPage
{
    List<Printerconfig> _printerConfigs;
    DBService dbService;

    Printerconfig _selectedprinter;

    string profiles;

    private static readonly Regex MacRegex = new Regex("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.Compiled);
    private static readonly List<string> ProfileNames = new()
    { "P6824BT", "PB21", "P6824SERIAL", "PR2", "PB31", "PB42", "PB51" };
    private const string DefaultProfile = "P6824BT";
    private bool _isFormattingMacAddress;
    private string _lastMacDisplay = string.Empty;

    private static string logoBase64ImgStr = null;
    public PrinterConfig()
    {
        InitializeComponent();
        LoadPickers();

#if ANDROID
        bluetooth_address.HandlerChanged += (s, e) =>
        {
            if (bluetooth_address.Handler?.PlatformView is Android.Widget.EditText et)
            {
                et.InputType = Android.Text.InputTypes.ClassText
                             | Android.Text.InputTypes.TextFlagCapCharacters
                             | Android.Text.InputTypes.TextFlagNoSuggestions;
            }
        };
#endif

    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAndSelectLatestAsync();
    }

    private async Task LoadAndSelectLatestAsync()
    {
        dbService = new DBService();

        pickerProfiles.ItemsSource ??= ProfileNames;

        _printerConfigs = (await dbService.GetAllPrinterConfig()) ?? new List<Printerconfig>();

        var names = new List<string> { "New Config" };
        names.AddRange(_printerConfigs.Select(p => p.profile_name));
        pickerConfigName.ItemsSource = names;

        // Priority: currently loaded -> default -> most recently saved (highest id)
        Printerconfig target = null;

        if (MyDeviceInfo.currentPrintConfig != null)
            target = _printerConfigs.FirstOrDefault(p => p.id == MyDeviceInfo.currentPrintConfig.id);

        target ??= _printerConfigs.FirstOrDefault(p => p.isdefault == 1);
        target ??= _printerConfigs.OrderByDescending(p => p.id).FirstOrDefault();

        SelectConfigInPicker(target?.profile_name);

        // If the config we picked is already the active one, allow Test Print
        if (target != null &&
            MyDeviceInfo.currentPrintConfig != null &&
            MyDeviceInfo.currentPrintConfig.id == target.id)
        {
            btnTestPrint.IsEnabled = true;
        }
    }

    private void SelectConfigInPicker(string name)
    {
        var names = pickerConfigName.ItemsSource as List<string>;
        int idx = 0;
        if (names != null && !string.IsNullOrEmpty(name))
        {
            int found = names.IndexOf(name);
            if (found > 0) idx = found;
        }

        // Force the change event to fire even if the index is already idx
        pickerConfigName.SelectedIndex = -1;
        pickerConfigName.SelectedIndex = idx;
    }

    private async void OnLoadPrintConfig()
    {
        if (MyDeviceInfo.currentPrintConfig != null)
        {
            _selectedprinter = MyDeviceInfo.currentPrintConfig;
            pickerConfigName.SelectedItem = _selectedprinter.profileconfig;
            bluetooth_address.Text = _selectedprinter.bluetooth_address;
            pickerProfiles.SelectedItem = _selectedprinter.profile_name;
            line_spaces.Text = _selectedprinter.linespacing.ToString();
            btnTestPrint.IsEnabled = true;
            btnLoad.IsEnabled = true;
            btnSave.Text = "Update Config";

        }
    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private static string BuildMacDisplay(string hex)
    {
        if (hex.Length > 12) hex = hex[..12];

        var sb = new System.Text.StringBuilder(17);
        for (int i = 0; i < hex.Length; i++)
        {
            if (i > 0 && i % 2 == 0) sb.Append(':');
            sb.Append(hex[i]);
        }

        // Show the next separator while the value is still incomplete
        if (hex.Length > 0 && hex.Length < 12 && hex.Length % 2 == 0)
            sb.Append(':');

        return sb.ToString();
    }

    private void OnBluetoothAddressChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFormattingMacAddress) return;

        var entry = (Entry)sender;
        string prev = _lastMacDisplay;
        string raw = e.NewTextValue ?? string.Empty;

        string hex = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length > 12) hex = hex[..12];

        string display = BuildMacDisplay(hex);

        // Backspace landed on the auto-inserted trailing colon -> drop a digit too
        if (raw.Length < prev.Length && display == prev && hex.Length > 0)
        {
            hex = hex[..^1];
            display = BuildMacDisplay(hex);
        }

        _lastMacDisplay = display;

        if (display != raw)
        {
            _isFormattingMacAddress = true;
            entry.Text = display;
            _isFormattingMacAddress = false;
        }

        Dispatcher.Dispatch(() => entry.CursorPosition = display.Length);
    }

    private void SetBluetoothAddress(string value)
    {
        _isFormattingMacAddress = true;
        bluetooth_address.Text = value ?? string.Empty;
        _lastMacDisplay = bluetooth_address.Text;
        _isFormattingMacAddress = false;
    }



    private async void OnSaveChanges(object sender, EventArgs e)
    {
        if (pickerConfigName.SelectedIndex == -1 ||
            pickerProfiles.SelectedIndex == -1 ||
            string.IsNullOrWhiteSpace(bluetooth_address.Text) ||
            string.IsNullOrWhiteSpace(line_spaces.Text))
        {
            await DisplayAlertAsync("Notice", "Incomplete Information to save.", "Ok");
            return;
        }

        // ---- Bluetooth must be a full XX:XX:XX:XX:XX:XX ----
        if (!MacRegex.IsMatch(bluetooth_address.Text ?? string.Empty))
        {
            await DisplayAlertAsync("Notice",
                "Please enter a complete Bluetooth address (XX:XX:XX:XX:XX:XX).", "Ok");
            return;
        }

        // ---- Line spacing must be a positive integer ----
        if (!int.TryParse(line_spaces.Text, out int p_linespaces) || p_linespaces <= 0)
        {
            await DisplayAlertAsync("Notice",
                "Line space must be a positive number.", "Ok");
            return;
        }

        int isupdate = 0;
        int p_id = 0;
        dbService = new DBService();
        int p_nameindex = pickerConfigName.SelectedIndex;
        int isdefault = chkDefault.IsChecked ? 1 : 0;

        string p_name;
        if (p_nameindex == 0)
        {
            p_name = await DisplayPromptAsync("Notice", "Please input new config name: ");
            if (string.IsNullOrWhiteSpace(p_name))
            {
                await DisplayAlertAsync("Notice",
                    "You need to enter a name for your config", "Ok");
                return;
            }
            p_name = p_name.Trim();

            if (_printerConfigs != null &&
                _printerConfigs.Any(p => string.Equals(p.profile_name, p_name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                await DisplayAlertAsync("Notice",
                    $"A config named \"{p_name}\" already exists.", "Ok");
                return;
            }
        }
        else
        {
            p_name = (string)pickerConfigName.Items[p_nameindex];
            p_id = _selectedprinter?.id ?? 0;
            isupdate = 1;
        }

        string p_profile = (string)pickerProfiles.Items[pickerProfiles.SelectedIndex];

        bool isSaved = await dbService.PrinterconfigSave(
            p_name, bluetooth_address.Text, p_profile, p_linespaces,
            isupdate, p_id, isdefault);

        if (!isSaved)
        {
            await DisplayAlertAsync("Warning", "Printer config not saved.", "Ok");
            return;
        }

        await DisplayAlertAsync("Notice", "Printer config saved.", "Ok");

        // Reload from DB and re-select the just-saved config so the picker
        // and every field stay populated without leaving the page.
        _printerConfigs = (await dbService.GetAllPrinterConfig()) ?? new List<Printerconfig>();
        if (MyDeviceInfo.currentPrintConfig != null)
        {
            var fresh = _printerConfigs.FirstOrDefault(p => p.id == MyDeviceInfo.currentPrintConfig.id);
            if (fresh != null) MyDeviceInfo.currentPrintConfig = fresh;
        }
        var names = new List<string> { "New Config" };
        names.AddRange(_printerConfigs.Select(p => p.profile_name));
        pickerConfigName.ItemsSource = names;
        SelectConfigInPicker(p_name);

        if (isdefault == 1)
        {
            MyDeviceInfo.currentPrintConfig = await dbService.GetDefaultPrinterConfig();
            if (MyDeviceInfo.currentPrintConfig != null)
            {
                await DisplayAlertAsync("Notice",
                    "Printer has been loaded as Default Printer", "Ok");
                btnTestPrint.IsEnabled = true;
            }
        }
    }

    private async void OnDeleteConfig(object sender, EventArgs e)
    {
        if (pickerConfigName.SelectedIndex <= 0 || _selectedprinter == null)
        {
            await DisplayAlertAsync("Notice",
                "Please select a saved config to remove.", "Ok");
            return;
        }

        bool confirm = await DisplayAlertAsync("Remove Config",
            $"Remove printer config \"{_selectedprinter.profile_name}\"?",
            "Yes", "No");
        if (!confirm) return;

        int removedId = _selectedprinter.id;
        dbService = new DBService();

        // NOTE: match this name to whatever your DBService exposes
        // (PrinterconfigDelete / DeletePrinterConfig / RemovePrinterConfig).
        bool deleted = await dbService.PrinterconfigDelete(removedId);

        if (!deleted)
        {
            await DisplayAlertAsync("Warning", "Printer config not removed.", "Ok");
            return;
        }

        // If we removed the active config, drop it from MyDeviceInfo
        if (MyDeviceInfo.currentPrintConfig != null &&
            MyDeviceInfo.currentPrintConfig.id == removedId)
        {
            MyDeviceInfo.currentPrintConfig = await dbService.GetDefaultPrinterConfig();
        }

        await DisplayAlertAsync("Notice", "Printer config removed.", "Ok");

        _selectedprinter = null;
        await LoadAndSelectLatestAsync();
    }

    private async void LoadPickers()
    {
        dbService = new DBService();

        pickerProfiles.ItemsSource = ProfileNames;
        pickerProfiles.SelectedItem = DefaultProfile;

        await RefreshPrinterConfigPickerAsync(null);
    }

    private async Task RefreshPrinterConfigPickerAsync(string selectName)
    {
        _printerConfigs = (await dbService.GetAllPrinterConfig()) ?? new List<Printerconfig>();

        var names = new List<string> { "New Config" };
        names.AddRange(_printerConfigs.Select(p => p.profile_name));

        pickerConfigName.ItemsSource = names;

        if (!string.IsNullOrEmpty(selectName))
        {
            int idx = names.FindIndex(n => n == selectName);
            if (idx > 0)
            {
                // Setting SelectedIndex fires OnPrinterConfigChange,
                // which re-populates every field from the DB record.
                pickerConfigName.SelectedIndex = idx;
            }
        }
    }

    private void OnPrinterConfigChange(object sender, EventArgs e)
    {
        if (pickerConfigName.SelectedIndex > 0 && _printerConfigs != null)
        {
            string selectedName = (string)pickerConfigName.Items[pickerConfigName.SelectedIndex];
            _selectedprinter = _printerConfigs.FirstOrDefault(p => p.profile_name == selectedName);

            if (_selectedprinter == null)
            {
                btnSave.Text = "Save Config";
                btnLoad.IsEnabled = false;
                btnTestPrint.IsEnabled = false;
                btnDelete.IsEnabled = false;
                return;
            }

            SetBluetoothAddress(_selectedprinter.bluetooth_address);
            line_spaces.Text = _selectedprinter.linespacing.ToString();
            pickerProfiles.SelectedItem = ProfileNames.Contains(_selectedprinter.profileconfig)
                                           ? _selectedprinter.profileconfig : DefaultProfile;
            chkDefault.IsChecked = _selectedprinter.isdefault == 1;

            btnSave.Text = "Update Config";
            btnLoad.IsEnabled = true;
            btnTestPrint.IsEnabled = false;
            btnDelete.IsEnabled = true;
        }
        else
        {
            _selectedprinter = null;
            SetBluetoothAddress("");
            line_spaces.Text = "0";
            pickerProfiles.SelectedItem = DefaultProfile;
            chkDefault.IsChecked = false;

            btnSave.Text = "Save Config";
            btnLoad.IsEnabled = false;
            btnTestPrint.IsEnabled = false;
            btnDelete.IsEnabled = false;
        }
    }

    private void OnPrinterProfileChange(object sender, EventArgs e)
    {

    }

    private async void OnLoadConfig(object sender, EventArgs e)
    {
        if (_selectedprinter == null) return;

        MyDeviceInfo.currentPrintConfig = _selectedprinter;
        btnTestPrint.IsEnabled = true;
        await DisplayAlertAsync("Notice", "Selected Config Profile has been loaded.", "Ok");
    }




    public async Task<string> DoEscPOS_Print()
    {
        string result = "";
        string strHeaderFileName = "MLS_Header.bmp";

        // Line Space (Width) from config = characters per line: 32 = 2-inch, 46/48 = 3-inch
        var paper = PaperProfile.FromLineSpace(MyDeviceInfo.currentPrintConfig.linespacing);

#if ANDROID
        bool hasPermission = await BluetoothPermissionHelper.EnsureBluetoothPermissionsAsync();
        if (!hasPermission)
            return "Bluetooth permission was not granted. Please allow Bluetooth access in Settings and try again.";

        byte[] logoBytes;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(strHeaderFileName.Trim());
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            logoBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return "Logo load failed: " + ex.GetType().Name + " - " + ex.Message;
        }

        var printer = new EscPOScmdPrint { Profile = paper };
        try
        {
            // dilate every glyph
            PrinterHardware.DensityMethod = DensityMethod.Esc7HeatTime;
            PrinterHardware.DensityLevel = 4;      // 0..4, 4 = darkest

            // OPTIONAL: If you want standard ASCII characters to use the printer's native font
            // (which is often darker than 1-bit raster graphics):
            PrinterHardware.SmallFont = SmallFontMode.Native;
            string mac = EscPOScmdPrint.NormalizeMacAddress(MyDeviceInfo.currentPrintConfig.bluetooth_address);
            // ESC 7 heat-time method (works on most Epson-compatible printers)

            await printer.ConnectAndPrepareAsync(mac);

            //await printer.AlignAsync(PrintAlign.Left);      // we do alignment ourselves

            // ---- LOGO ----
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            // ---- TITLE: Font B, bold, double high + wide, underline ----
            await printer.PrintTextAsync("TEST PRINT", PrintAlign.Center,
                                         new PrintStyle(FontB: false, WidthMul: 2, HeightMul: 2, Bold: true, Underline: true));
            await printer.FeedAsync(1);

            // ============================== FONT A ==============================
            // ============================== SEPARATOR STYLES ==============================

            await printer.PrintSeparatorAsync(SeparatorStyle.Broken);

            // 4 named sizes in Font A
            // LARGE, WIDE, HIGH, BOLD  -> FontA 3x3
            await printer.PrintTextAsync("FONT A", PrintAlign.Center,
                new PrintStyle(FontB: false, WidthMul: 4, HeightMul: 1, Bold: true));

            // BIG, BOLD, WIDE          -> FontA 2x2 (both dims doubled = wide + high)
            await printer.PrintTextAsync("FONT A", PrintAlign.Center,
                new PrintStyle(FontB: false, WidthMul: 3, HeightMul: 1, Bold: true));

            // NORMAL, HIGH, BOLD       -> FontA 1x2 (normal width, doubled height)
            await printer.PrintTextAsync("FONT A", PrintAlign.Center,
                new PrintStyle(FontB: false, WidthMul: 2, HeightMul: 1, Bold: true));

            // SMALL, BOLD              -> FontA 1x1
            await printer.PrintTextAsync("FONT A", PrintAlign.Center,
                new PrintStyle(FontB: false, WidthMul: 1, HeightMul: 1, Bold: true));

            await printer.PrintTextAsync(
                "AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxTyZz-1234567890-ÑñÁáÉéÍíÓóÚúÀàÂâÈèÊêÌìÎîÒòÔôÙùÛûÄäËëÏïÖöÜü'",
                PrintAlign.Left, new PrintStyle(Size: TextSize.Normal, Bold: true));

            await printer.FeedAsync(1);
            await printer.PrintRowAsync("Bill Amount", "111,203.67", new PrintStyle(Size: TextSize.Small, Bold: true, High: true));
            await printer.PrintRowAsync("Total Amount", "111,203.67", new PrintStyle(Size: TextSize.Normal, Bold: true, Wide: true));

            // ============================== FONT B ==============================
            // Small is the only FontB size in the enum, so for the other three
            // FontB sizes use WidthMul / HeightMul together with FontB: true.
            await printer.PrintSeparatorAsync(SeparatorStyle.Broken);

            await printer.PrintTextAsync("FONT B", PrintAlign.Center,
                new PrintStyle(FontB: true, WidthMul: 4, HeightMul: 1, Bold: true));

            // BIG, BOLD, WIDE          -> FontB 2x2
            await printer.PrintTextAsync("FONT B", PrintAlign.Center,
                new PrintStyle(FontB: true, WidthMul: 3, HeightMul: 1, Bold: true));

            // NORMAL, HIGH, BOLD       -> FontB 1x2
            await printer.PrintTextAsync("FONT B", PrintAlign.Center,
                new PrintStyle(FontB: true, WidthMul: 2, HeightMul: 1, Bold: true));

            // SMALL, BOLD              -> FontB 1x1
            await printer.PrintTextAsync("FONT B", PrintAlign.Center,
                new PrintStyle(FontB: true, WidthMul: 1, HeightMul: 1, Bold: true));

            await printer.PrintTextAsync(
                "AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxTyZz-1234567890-ÑñÁáÉéÍíÓóÚúÀàÂâÈèÊêÌìÎîÒòÔôÙùÛûÄäËëÏïÖöÜü'",
                PrintAlign.Left, new PrintStyle(FontB: true));

            await printer.FeedAsync(1);
            await printer.PrintRowAsync("Bill Amount", "111,203.67", new PrintStyle(FontB: true, Wide: true));
            await printer.PrintRowAsync("Total Amount", "111,203.67", new PrintStyle(FontB: true, High: true));

            // ============================== BARCODES ==============================
            await printer.FeedAsync(1);
            await printer.WriteBarcodeAsync("67-031112", humanText: false, widthFraction: 1.0);
            await printer.FeedAsync(2);

            //await printer.AlignAsync(PrintAlign.Center);
            await printer.WriteBarcodeAsync("67-031112", BarcodeType.Code39, humanText: false, widthFraction: 1.5);
            await printer.WriteRawAsync(new byte[] { 0x0A, 0x0A });
            //await printer.AlignAsync(PrintAlign.Left);

            // ============================== QR CODES ==============================
            await printer.PrintQrSizedAsync("www.microlinesmanila.com.ph", widthFraction: 0.60);
            await printer.FeedAsync(1);

            await printer.PrintQrSizedAsync(
                EscPOScmdPrint.BuildAccountPayload("01-011234", "MjM Aparente", 1250.50m), widthFraction: 0.60);

            await printer.FeedAsync(5);
            await printer.WriteRawAsync(new byte[] { 0x1B, 0x40 });
            await printer.FlushAsync();
            result = "Done printing.";
        }
        catch (Exception ex)
        {
            result = "Print failed: " + ex.GetType().Name + " - " + ex.Message;
        }
        finally
        {
            await Task.Delay(1500 + printer.RasterRows * 2);
            printer.Disconnect();
        }
#else
    result = "Printing is only implemented for Android.";
#endif
        return result;
    }


    private async void ReadProFile()
    {
        string sourcefilename = "Datafiles/printer_profiles.JSON";
        //Stream stream = assembly.GetManifestResourceStream(resourcePrefix + fileName);
        using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        profiles = "";
        using (var reader = new System.IO.StreamReader(stream))
        {
            profiles = reader.ReadToEnd();
        }

    }

    private async void ReadImgToBase64String()
    {

        string sourcefilename = "Datafiles/msl_print.bmp";
        //Stream stream = assembly.GetManifestResourceStream(resourcePrefix + fileName);
        using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        byte[] imgByteArray = new byte[stream.Length];
        stream.Read(imgByteArray, 0, (int)stream.Length);
        logoBase64ImgStr = Convert.ToBase64String(imgByteArray);

    }

    private async void OnTestPrint(object sender, EventArgs e)
    {
        string output = "";

        if (MyDeviceInfo.currentPrintConfig != null)
        {
            //string outputTest = await DoTestPrint();
            //await DisplayAlertAsync("Notice", outputTest, "Ok");
            bool isConfirm = await DisplayAlertAsync("Test Print", "Would you like to perform a test print?", "Yes", "No");
            if (isConfirm)
            {
                output = await DoEscPOS_Print();
            }
            await DisplayAlertAsync("Test Print", output, "Ok");
        }
        else
        {
            await DisplayAlertAsync("Test Print", "Please select a configuration to load.", "Ok");
        }
    }
}