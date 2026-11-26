using eMRBCS.Global;
using eMRBCS.Services;
using MSRBC.Services;

namespace eMRBCS.Pages;

public partial class Utilities : ContentPage
{

    bool ImportFileStatus;
    bool ImportDBStatus;

    string importStatusMsg;

    DBService dbService;

    string profiles;

    private static string logoBase64ImgStr = null;

    public Utilities()
    {
        InitializeComponent();
        syncProgress.BindingContext = DataSyncService.Instance;
        dbService = new DBService();
        ScreenAdjustment();
        AccessCheck();
    }

    private void ScreenAdjustment()
    {
        if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {

        }
    }
    private async void TapLicense(object sender, TappedEventArgs e)
    => await Navigation.PushOnceAsync(() => new ProductActivation());
    private void AccessCheck()
    {
        cmdLicense.IsVisible = LoginUser.role == 1;
        int role = LoginUser.role;
        switch (role)
        {
            case 1:

                cmdSettings.IsVisible = true;
                cmdRestore.IsVisible = true;
                cmdUserAccess.IsVisible = true;
                break;
            case 2: //admin                
                cmdRestore.IsVisible = false;
                break;
            case 3:
                cmdRestore.IsVisible = false;
                cmdUserAccess.IsVisible = false;
                cmdSettings.IsVisible = false;
                break;
        }
    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PopOnceAsync();
    }

    private async void TapGetData(object sender, EventArgs e)
        => await UiGuard.RunAsync("get-data", async () =>
        {
            DateTime billDate = MyDeviceInfo.BillingDate.Date;
            if (DataSyncService.Instance.IsBusy)
            {
                await DisplayAlertAsync("Notice", "Data is already being downloaded.", "Ok");
                return;
            }
            //Check the latest import date.
            string latest_importdate;
            string import_message = "This process will import new data for " + billDate.ToString("yyyy-MM-dd") + " from server.";
            bool isConfirmAgain = false;
            string warningmsg = "";
            bool withError = false;

            bool confirm_answer;
            int consumerFile = 0;
            int remarkFile = 0;
            int billrateFile = 0;
            int userFile = 0;

            bool isValidRequest = true;
            bool isNewTransaction = false;

            //Before anything else, check if IP address is valid
            if (Serverdetails.Validnetwork == 0)
            {
                await DisplayAlertAsync("Warning", "Invalid Server address. Please contact administrator.", "Ok");
                return;
            }



            Serverdetails.Server_Transactions = await dbService.GetLatestImportDate();

            if (Serverdetails.Server_Transactions != null)
            {
                latest_importdate = Serverdetails.Server_Transactions.latest_importdate;

                if (DateTime.Parse(latest_importdate).Date != billDate)
                {
                    isNewTransaction = true;
                    if (Serverdetails.Server_Transactions.latest_exportdate == null)
                    {
                        isConfirmAgain = true;
                        if (ConsumerRecords.noofrecordsBilled > 0)
                        {
                            import_message = import_message + " YOU HAVEN'T SUBMITTED YOUR DAILY REPORT. Do you still want to continue without submitting your daily transactions for " + DateTime.Parse(latest_importdate).Date.ToString("yyyy-MM-dd") + " ?";

                        }
                        else
                        {
                            import_message = import_message + " Existing consumer records for " + DateTime.Parse(latest_importdate).Date.ToString("yyyy-MM-dd") + " will be overwritten with " + billDate.ToString("yyyy-MM-dd") + " consumers? Continue?";
                        }

                    }
                    else
                    {
                        import_message = import_message + " Do you want to continue?";
                    }
                }
                else
                {

                    if (ConsumerRecords.noofrecordsBilled > 0)
                    {
                        import_message = import_message + " YOU HAVE " + ConsumerRecords.noofrecordsBilled + " BILLED CONSUMERS. ALL DATA will be LOST!!! Continue?";
                        isConfirmAgain = true;
                    }
                    else
                    {
                        import_message = import_message + " Existing consumer records will be overwritten!!! Continue?";
                    }
                }
            }
            else
            {
                isNewTransaction = true;
                import_message = import_message + " Do you want to continue?";
            }

            if (billDate != DateTime.Today)
                import_message += $"\n\nWARNING: Billing date ({billDate:yyyy-MM-dd}) is not the device date ({DateTime.Today:yyyy-MM-dd}).";
            confirm_answer = await DisplayAlertAsync("Warning", import_message, "Yes", "No");
            if (confirm_answer)
            {
                if (isConfirmAgain)
                {
                    bool confirm_overwrite = await DisplayAlertAsync("Confirmation", "Are you really sure you want to proceed?", "Yes", "No");
                    if (!confirm_overwrite)
                    {
                        isValidRequest = false;
                    }
                }

            }
            else
            {

                isValidRequest = false;
            }

            if (!isValidRequest) return;

            // Runs in the background singleton: the UI stays free and the work continues on other pages.
            _ = DataSyncService.Instance.StartAsync();
        });

    private async void TapSendData(object sender, EventArgs e)
    => await UiGuard.RunAsync("send-data", async () =>
    {
        if (!await DisplayAlertAsync("Confirm", "Are you sure you want to submit your report?", "Yes", "No"))
            return;

        stackLoading.IsVisible = true;
        bool isError = false;
        try
        {
            ConsumerRecords.ErrorSendData = "";
            dbService = new DBService();

            var fileService = new FileService();
            isError = !(await dbService.PrepareTransaction()
                     && await fileService.WriteDailyTrans()
                     && await fileService.ExportFileAsync()
                     && await dbService.ServerExportUpdate());
        }
        finally { stackLoading.IsVisible = false; }

        await DisplayAlertAsync("Notice",
            isError ? "Failed to submit report file!\nError:\n" + ConsumerRecords.ErrorSendData
                    : "Report file has been submitted", "Ok");
    });
    private async void TapSettings(object sender, EventArgs e)
    {
        dbService = new DBService();
        bool isLog = await dbService.SaveUserActivity("Utilities", "Open Settings", "Success");
        await Navigation.PushOnceAsync(() => new AppSettings());

    }

    private async void TapUsers(object sender, TappedEventArgs e)
    {

        await Navigation.PushOnceAsync(() => new UserAccess());
    }

    private async void TapBackup(object sender, TappedEventArgs e)
        => await UiGuard.RunAsync("backup-data", async () =>
        {
            //await Navigation.PushAsync(new ExportData());

            bool istoBackup = await DisplayAlertAsync("Notice", "Backup data to available external storage. Continue?", "Yes", "No");
            if (istoBackup)
            {
                stackLoading.IsVisible = true;
                string copyStatus = await CopyFileToExternalStorageAsync();
                string[] tmpStatus = copyStatus.Split(':');
                string alertStatus = "Copy " + tmpStatus[0] + "\nMessage: " + tmpStatus[1] + "\nDestination: " + tmpStatus[2];
                stackLoading.IsVisible = false;
                await DisplayAlertAsync("Notice", alertStatus, "Ok");
            }

        });

    private async void TapRestore(object sender, TappedEventArgs e)
    {
        //await Navigation.PushAsync(new ExportData());
        await Navigation.PushOnceAsync(() => new ImportData());
    }



    private async void TapPrinterConfig(object sender, TappedEventArgs e)
    {
        await Navigation.PushOnceAsync(() => new PrinterConfig());
    }

    public async Task<string> CopyFileToExternalStorageAsync()
    {
        string destinationFilePath = "";
        try
        {
            string source = Path.Combine(FileSystem.AppDataDirectory, database_info.dbName);
            if (!File.Exists(source)) return "Failed:Database file doesn't exist:";

#if ANDROID
            string? dir = null;
            foreach (var f in Android.App.Application.Context.GetExternalFilesDirs(null))
                if (f != null && !f.AbsolutePath.Contains("emulated")) { dir = f.AbsolutePath; break; }

            if (dir == null) return "Failed:No external storage (SD card) found:";

            destinationFilePath = Path.Combine(dir, database_info.dbName);
            await Task.Run(() => File.Copy(source, destinationFilePath, true));
            return "Success:No error:" + destinationFilePath;
#else
        await Task.CompletedTask;
        return "Failed:Backup is only implemented for Android:";
#endif
        }
        catch (Exception ex)
        {
            return "Failed:" + ex.Message.Replace(':', ' ') + ":" + destinationFilePath;   // ':' is the separator used by TapBackup
        }
    }
}