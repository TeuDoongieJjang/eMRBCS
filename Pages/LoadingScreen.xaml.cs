using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using MSRBC.Services;
using System.Security.Cryptography;
using System.Text;


namespace eMRBCS.Pages;

public partial class LoadingScreen : ContentPage
{
    DBService dbService;
    FileService fileService;

    string sourcefilename = database_info.packageDBfile;
    string targetfilename = database_info.dbName;

    public LoadingScreen()
    {
        InitializeComponent();
        GetDeviceInfo();
        ScreenAdjustment();
    }

    private void GetDeviceInfo()
    {

        var mainDisplayInfo = DeviceDisplay.MainDisplayInfo;

        double screenWidth = mainDisplayInfo.Width;
        double screenHeight = mainDisplayInfo.Height;


        double density = mainDisplayInfo.Density;

        double screenWidthInDip = screenWidth / density;
        double screenHeightInDip = screenHeight / density;

        string device_id = string.Empty;

#if ANDROID

        device_id = Android.Provider.Settings.Secure.GetString(Android.App.Application.Context.ContentResolver, Android.Provider.Settings.Secure.AndroidId);

#endif

        MyDeviceInfo.device_width = screenWidthInDip;
        MyDeviceInfo.device_height = screenHeightInDip;
        MyDeviceInfo.device_density = density;
        MyDeviceInfo.device_widthpix = screenWidth;
        MyDeviceInfo.device_heightpix = screenHeight;
        MyDeviceInfo.device_AndroidID = device_id;
    }

    private async void ScreenAdjustment()
    {
        if (MyDeviceInfo.device_heightpix > 2200)
        {

        }
        else if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {
            imglogo.HeightRequest = 150;
            imglogo.WidthRequest = 133;

            var absBound = new Rect(0, 50, 250, 100);
            absLoading.SetLayoutBounds(lblLoadingDetails, absBound);

            absBound = new Rect(0, 60, 250, 100);
            absLoading.SetLayoutBounds(activityIndicator, absBound);

            absBound = new Rect(0, 100, 250, 100);
            absLoading.SetLayoutBounds(lblLoadingPercent, absBound);

            absBound = new Rect(20, 30, 300, 100);
            absCompany.SetLayoutBounds(lblpoweredby, absBound);

            absBound = new Rect(15, 45, 300, 100);
            absCompany.SetLayoutBounds(lblcopyright, absBound);
        }

    }

    private async Task CopyDBFile()
    {
        using Stream inputStream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        string targetFile = Path.Combine(FileSystem.Current.AppDataDirectory, targetfilename);
        using FileStream outputStream = File.Create(targetFile);
        await inputStream.CopyToAsync(outputStream);
    }




    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (ApkInstaller.IsUpdateInProgress)
            return;

        var updateService = new UpdateService();
        var updateInfo = await updateService.CheckForUpdateAsync("TeuDoongieJjang", "eMRBCS");

        if (updateInfo != null)
        {
            bool answer = await DisplayAlertAsync("Update Available", "Download and install now?", "Yes", "No");

            if (answer)
            {
                var installer = new ApkInstaller();
                await installer.InstallApkAsync(updateInfo.DownloadUrl);
            }
        }
        try
        {
            string filePath = Path.Combine(FileSystem.AppDataDirectory, targetfilename);
            if (!File.Exists(filePath))
                await CopyDBFile();          // <-- must be awaited

            await Task.Delay(100);           // was 500 – just enough for UI to paint
            await LoadConsumerFromClass();   // now a Task, awaited
        }
        catch (Exception ex)
        {
            lblLoadingDetails.Text = "Startup failed.";
            lblLoadingPercent.Text = "0%";
            await DisplayAlertAsync("Startup Error", ex.Message, "Ok");
        }
    }

    private async Task LoadConsumerFromClass()   // was async void
    {
        dbService = new DBService();
        lblLoadingPercent.Text = "10%";

        Networkconfig networkconfig = await dbService.GetExistingNetwork();

        // ---- FIX: don't hang when there is no config ----
        if (networkconfig == null)
        {
            lblLoadingPercent.Text = "100%";
            lblLoadingDetails.Text = "No network configuration found.";
            await Navigation.PushAsync(new Login());
            return;
        }

        Serverdetails.NetworkID = networkconfig.id;
        Serverdetails.ImportServerIPAddress = networkconfig.import_ipaddress;
        Serverdetails.ImportSharedFolder = networkconfig.import_folder;
        Serverdetails.ImportServerUserName = networkconfig.import_user;
        Serverdetails.ImportServerPassword = networkconfig.export_pass;

        Serverdetails.ExportServerIPAddress = networkconfig.export_ipaddress;
        Serverdetails.ExportSharedFolder = networkconfig.export_folder;
        Serverdetails.ExportServerUserName = networkconfig.export_user;
        Serverdetails.ExportServerPassword = networkconfig.export_pass;

        lblLoadingPercent.Text = "15%";

        if (networkconfig.import_ipaddress != "0.0.0.0")
        {
            fileService = new FileService();
            lblLoadingPercent.Text = "25%";

            string isImportSuccess = await fileService.ImportFileAsync(3);
            string[] items = isImportSuccess.Split(';');

            if (items[0].Trim() == "success")
            {
                lblLoadingPercent.Text = "30%";
                bool ImportDBStatus = await dbService.UserReadAndProcessileAsync(fileService.userfile);
                if (ImportDBStatus)
                    lblLoadingDetails.Text = "Loading Users...";
            }

            lblLoadingPercent.Text = "50%";
            lblLoadingDetails.Text = "Loading Consumers...";

            ConsumerRecords.gridConsumerBilledRecords = new Grid
            {
                RowDefinitions = { new RowDefinition { Height = GridLength.Auto } },
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star } }
            };
            ConsumerRecords.gridBilled_rowindex = 0;
            Serverdetails.Validnetwork = 1;

            lblLoadingPercent.Text = "75%";
            ConsumerRecords.AutoID_UIndex = new List<string>();
            ConsumerRecords.AutoID_BIndex = new List<string>();
            ConsumerRecords.gridConsumerBilledCollection = new List<Grid>();
            ConsumerRecords.gridConsumerUnbilledCollection = new List<Grid>();

            await dbService.RequestForConsumers();

            MyDeviceInfo.currentPrintConfig = await dbService.GetDefaultPrinterConfig();
        }
        else
        {
            Serverdetails.Validnetwork = 0;
        }

        lblLoadingPercent.Text = "100%";

        string product_key = EncryptString(MyDeviceInfo.device_AndroidID.ToUpper(), "manny");
        MyDeviceInfo.isActivated = IsAppActivated(product_key);

        await Navigation.PushAsync(new Login());
    }

    public static string EncryptString(string plainText, string passphrase)
    {


        using (var aes = Aes.Create())
        {
            var keyDerivationFunction = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(passphrase), 10000);
            aes.Key = keyDerivationFunction.GetBytes(32);
            aes.IV = keyDerivationFunction.GetBytes(16);


            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }

                var encryptedBytes = ms.ToArray();
                return Convert.ToBase64String(encryptedBytes);
            }
        }
    }

    public static bool IsAppActivated(string productKey)
    {

        bool isActivated = false;
        string activationkey = "";
        string filePath = Path.Combine(FileSystem.AppDataDirectory, MyDeviceInfo.activation_file);
        if (File.Exists(filePath))
        {

            activationkey = File.ReadAllText(filePath);

            if (activationkey == productKey)
            {
                isActivated = true;
            }

        }

        return isActivated;
    }


}