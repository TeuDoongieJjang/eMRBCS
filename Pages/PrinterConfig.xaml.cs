using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;



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

    private static string logoBase64ImgStr = null;
    public PrinterConfig()
    {
        InitializeComponent();
        LoadPickers();

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



    private async void OnSaveChanges(object sender, EventArgs e)
    {
        if (pickerConfigName.SelectedIndex == -1 || pickerProfiles.SelectedIndex == -1 || bluetooth_address.Text == "" || line_spaces.Text == "")
        {
            await DisplayAlertAsync("Notice", "Incomplete Information to save.", "Ok");
            return;

        }

        int isupdate = 0;
        int p_id = 0;
        dbService = new DBService();
        int p_nameindex = pickerConfigName.SelectedIndex;
        int isdefault = chkDefault.IsChecked ? 1 : 0;
        string p_name = "";
        if (p_nameindex == 0)
        {

            p_name = await DisplayPromptAsync("Notice", "Please input new config name: ");
            if (p_name == null)
            {
                await DisplayAlertAsync("Notice", "You need to enter a name for your config", "Ok");
                return;
            }
        }
        else
        {
            p_name = pickerConfigName.Items[p_nameindex];
            p_id = _selectedprinter.id;
            isupdate = 1;
        }
        int p_profileindex = pickerConfigName.SelectedIndex;
        string p_profile = pickerProfiles.Items[p_profileindex];
        int p_linespaces = line_spaces.Text == "" ? 0 : int.Parse(line_spaces.Text);

        bool isSaved = await dbService.PrinterconfigSave(p_name, bluetooth_address.Text, p_profile, p_linespaces, isupdate, p_id, isdefault);
        if (isSaved)
        {
            await DisplayAlertAsync("Notice", "Printer config saved.", "Ok");
            if (isupdate == 1)
            {
                _selectedprinter.linespacing = int.Parse(line_spaces.Text);
                _selectedprinter.profileconfig = p_profile;
                _selectedprinter.bluetooth_address = bluetooth_address.Text;
            }
            if (isdefault == 1 && isupdate == 0)
            {
                //Load printer if default
                MyDeviceInfo.currentPrintConfig = await dbService.GetDefaultPrinterConfig();
                if (MyDeviceInfo.currentPrintConfig != null)
                {
                    await DisplayAlertAsync("Notice", "Printer has been loaded as Default Printer", "Ok");
                    pickerConfigName.SelectedIndex = 0;
                    bluetooth_address.Text = "";
                    pickerProfiles.SelectedIndex = 0;
                    line_spaces.Text = "0";
                    chkDefault.IsChecked = false;
                }
            }
        }
        else
        {
            await DisplayAlertAsync("Warning", "Printer config not saved.", "Ok");
        }
    }

    private async void LoadPickers()
    {
        _printerConfigs = new List<Printerconfig>();
        dbService = new DBService();
        _printerConfigs = await dbService.GetAllPrinterConfig();
        List<string> _printernames = new List<string>();
        _printernames.Add("New Config");
        if (_printerConfigs != null)
        {

            foreach (var item in _printerConfigs)
            {
                _printernames.Add(item.profile_name);
            }
        }

        pickerConfigName.ItemsSource = _printernames;

        pickerProfiles.ItemsSource = new List<string>
        {
            "PB21",
            "P6824SERIAL",
            "P6824BT",
            "PR2",
            "PB31",
            "PB42",
            "PB51"
        };
    }

    private void OnPrinterConfigChange(object sender, EventArgs e)
    {
        if (pickerConfigName.SelectedIndex > 0)
        {
            string _selectedname = pickerConfigName.Items[pickerConfigName.SelectedIndex];
            _selectedprinter = new Printerconfig();
            _selectedprinter = _printerConfigs.Where(p => p.profile_name == _selectedname).FirstOrDefault();
            bluetooth_address.Text = _selectedprinter.bluetooth_address;
            line_spaces.Text = _selectedprinter.linespacing.ToString();
            pickerProfiles.SelectedItem = _selectedprinter.profileconfig;
            chkDefault.IsChecked = _selectedprinter.isdefault == 1 ? true : false;
            btnSave.Text = "Update Config";
            btnLoad.IsEnabled = true;
            btnTestPrint.IsEnabled = false;

        }
        else
        {
            bluetooth_address.Text = "";
            line_spaces.Text = "0";
            pickerProfiles.SelectedIndex = 0;
            btnSave.Text = "Save Config";
            btnLoad.IsEnabled = false;
            btnTestPrint.IsEnabled = false;
        }
    }

    private void OnPrinterProfileChange(object sender, EventArgs e)
    {

    }

    private async void OnLoadConfig(object sender, EventArgs e)
    {
        MyDeviceInfo.currentPrintConfig = _selectedprinter;
        btnTestPrint.IsEnabled = true;
        await DisplayAlertAsync("Notice", "Selected Config Profile has been loaded.", "Ok");
    }




    public async Task<string> DoEscPOS_Print()
    {
        string result = "";
        string strHeaderFileName = "MLS_Header.bmp";

        // Line Space (Width) from config = characters per line: 32 = 2-inch, 46/48 = 3-inch
        int raw = MyDeviceInfo.currentPrintConfig.linespacing;
        var paper = PaperProfile.FromLineSpace(raw);

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

        var printer = new EscPOScmdPrint
        {
            Profile = paper,
            RasterSliceRows = 48,        // send barcodes/QR/underline in small bands
            RasterChunkSize = 256,       // small Bluetooth writes for raster data
            RasterChunkDelayMs = 5       // short pause so the printer buffer can keep up
        };
        try
        {
            string mac = EscPOScmdPrint.NormalizeMacAddress(MyDeviceInfo.currentPrintConfig.bluetooth_address);
            await printer.ConnectAsync(mac);
            await Task.Delay(200);
            await printer.InitializeAsync();
            await printer.AlignAsync(PrintAlign.Left);      // we do alignment ourselves

            // ---- LOGO ----
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            // ---- TITLE: Font B, bold, double high + wide, underline ----
            await printer.PrintTextAsync("TEST PRINT", PrintAlign.Center,
                                         new PrintStyle(FontB: false, WidthMul: 2, HeightMul: 2, Bold: true, Underline: true));
            await printer.FeedAsync(1);

            // ============================== FONT A ==============================
            await printer.PrintSeparatorAsync();

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
                "AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxTyZz-1234567890ñÑńŃňŇáÁéÉíÍóÓúÚüÜçÇàÀèÈùÙâÂêÊîÎôÔûÛäÄöÖåÅæÆøØß-'",
                PrintAlign.Left, new PrintStyle(Size: TextSize.Normal, Bold: true));

            await printer.FeedAsync(1);
            await printer.PrintRowAsync("Bill Amount", "111,203.67", new PrintStyle(Size: TextSize.Small, Bold: true, High: true));
            await printer.PrintRowAsync("Total Amount", "111,203.67", new PrintStyle(Size: TextSize.Normal, Bold: true, Wide: true));

            // ============================== FONT B ==============================
            // Small is the only FontB size in the enum, so for the other three
            // FontB sizes use WidthMul / HeightMul together with FontB: true.
            await printer.PrintSeparatorAsync('-', fontB: true);

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
                "AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxTyZz-1234567890",
                PrintAlign.Left, new PrintStyle(FontB: true));

            await printer.FeedAsync(1);
            await printer.PrintRowAsync("Bill Amount", "111,203.67", new PrintStyle(FontB: true, Wide: true));
            await printer.PrintRowAsync("Total Amount", "111,203.67", new PrintStyle(FontB: true, High: true));

            // ============================== BARCODES ==============================
            await printer.FeedAsync(1);
            await printer.WriteBarcodeAsync("67-031112", humanText: false, widthFraction: 1.0);
            await printer.FeedAsync(2);

            await printer.AlignAsync(PrintAlign.Center);
            await printer.WriteBarcodeAsync("67-031112", BarcodeType.Code39, humanText: false, widthFraction: 1.0);
            await printer.WriteRawAsync(new byte[] { 0x0A, 0x0A });
            await printer.AlignAsync(PrintAlign.Left);

            // ============================== QR CODES ==============================
            await printer.PrintQrSizedAsync("www.microlinesmanila.com.ph", widthFraction: 0.50);
            await printer.FeedAsync(1);

            await printer.PrintQrSizedAsync(
                EscPOScmdPrint.BuildAccountPayload("01-011234", "MjM Aparente", 1250.50m), widthFraction: 0.50);

            await printer.FeedAsync(3);
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
            bool isConfirm = await DisplayAlertAsync("Test Print", "Click YES for Generic Test Print \n or NO for Honeywell Test Print.", "Yes", "No");
            if (isConfirm)
            {
                output = await DoEscPOS_Print();
            }
            else
            {
                //output = await DoPrint();
                output = await DoPrintBill_HoneyWell();

            }
            await DisplayAlertAsync("Test Print", output, "Ok");
        }
        else
        {
            await DisplayAlertAsync("Test Print", "Please select a configuration to load.", "Ok");
        }
    }
}