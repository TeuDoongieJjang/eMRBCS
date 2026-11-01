using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using MSRBC.Services;
using SQLite;

namespace eMRBCS.Pages;

public partial class Mainmenu : ContentPage
{
    public static SQLiteConnection Database;
    string sourcefilename = database_info.packageDBfile;
    string targetfilename = database_info.dbName;

    List<Remarks> remark_items;

    DBService dbService;
    FileService fileService;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Always reset the overlay when this page becomes visible again
        // (e.g. returning from Reports, Utilities, ReadingConsumers, Login, …)
        stackLoading.IsVisible = false;
    }

    private async void CopyDBFile()
    {
        using Stream inputStream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        string targetFile = Path.Combine(FileSystem.Current.AppDataDirectory, targetfilename);
        using FileStream outputStream = File.Create(targetFile);
        await inputStream.CopyToAsync(outputStream);

    }

    public Mainmenu()
    {
        InitializeComponent();
        //activityIndicator.IsVisible = false;
        //activityIndicator.IsRunning = false;

        //stackLoading.IsVisible = false;

        lbluserfullname.Text = "Name: " + LoginUser.login_user.fullname;
        lbllogindate.Text = "Login Date/Time: " + DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
        stackLoading.IsVisible = false;
        ScreenAdjustment();
        CheckServerStatus();
    }

    private void ScreenAdjustment()
    {

        if ((MyDeviceInfo.device_heightpix < 2300 && MyDeviceInfo.device_heightpix > 2000) && (MyDeviceInfo.device_widthpix > 1000))
        {
            recDivider.Margin = new Thickness(30, 10, 30, 0);

            var boundsStackloading = new Rect(0.5, 0.3, 200, 25);
            AbsoluteLayout.SetLayoutBounds(stackLoading, boundsStackloading);

        }
        else if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {
            lblProductKey.WidthRequest = 100;
            lblMR.WidthRequest = 100;



        }

        MyDeviceInfo.isActivated = true;

        if (MyDeviceInfo.isActivated)
        {
            recProductKeyLower.IsVisible = false;
            recProductKeyUpper.IsVisible = false;
            imgProductKey.IsVisible = false;
            lblProductKey.IsVisible = false;
            frmProductKey.IsVisible = false;
        }

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

    private async void TapMeterReading(object sender, EventArgs e)
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }

        stackLoading.IsVisible = true;
        try
        {
            ConsumerRecords.Ubatch_loaded = new List<int>();
            ConsumerRecords.Bbatch_loaded = new List<int>();
            await Navigation.PushAsync(new ReadingConsumers());
        }
        finally
        {
            stackLoading.IsVisible = false;
        }
    }

    private async void TapUtilities(object sender, EventArgs e)
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }

        stackLoading.IsVisible = true;
        try
        {
            dbService = new DBService();
            await dbService.SaveUserActivity("Main Menu", "Open Utilities", "Success");
            await Navigation.PushAsync(new Utilities());
        }
        finally
        {
            stackLoading.IsVisible = false;
        }
    }


    private async void TapLogout(object sender, TappedEventArgs e)
    {
        bool confirm = await DisplayAlertAsync("Notice", "Are you sure you want to log out?", "Yes", "No");
        if (confirm)
        {
            dbService = new DBService();
            bool isLog = await dbService.SaveUserActivity("Mainmenu", "logout", "Success");
            LoginUser.login_user = new Users();
            await Navigation.PushAsync(new Login());
        }

    }

    private async void TapCollection(object sender, TappedEventArgs e)
    {
        if (LoginUser.role > 1)
        {
            if (!MyDeviceInfo.isActivated)
            {
                await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
                return;
            }
        }
        await DisplayAlertAsync("Notice", "This feature is not yet available for this version.", "Ok");
    }

    private async void TapReports(object sender, TappedEventArgs e)
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }

        stackLoading.IsVisible = true;
        try
        {
            await Navigation.PushAsync(new Reports());
        }
        finally
        {
            // PushAsync completes once Reports is shown, so the overlay
            // is hidden again before the user can ever come back to it.
            stackLoading.IsVisible = false;
        }
    }

    private async void TapProductKey(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new ProductActivation());
    }
}