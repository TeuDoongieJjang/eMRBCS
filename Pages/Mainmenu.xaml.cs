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
        stackLoading.IsVisible = false;

        ServerStatusMonitor.Instance.StatusChanged += OnServerStatusChanged;
        ApplyStatus(ServerStatusMonitor.Instance.IsOnline);   // show last known state immediately
        ServerStatusMonitor.Instance.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ServerStatusMonitor.Instance.StatusChanged -= OnServerStatusChanged;
        ServerStatusMonitor.Instance.Stop();
    }

    private void OnServerStatusChanged(bool ok)
        => MainThread.BeginInvokeOnMainThread(() => ApplyStatus(ok));

    private void ApplyStatus(bool ok)
        => frameStatus.BackgroundColor = ok ? Colors.Green : Colors.Red;





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
        syncBar.BindingContext = DataSyncService.Instance;
        //activityIndicator.IsVisible = false;
        //activityIndicator.IsRunning = false;

        //stackLoading.IsVisible = false;

        lbluserfullname.Text = "Name: " + LoginUser.login_user.fullname;
        lbllogindate.Text = "Login Date/Time: " + DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
        stackLoading.IsVisible = false;
        ScreenAdjustment();
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

    private async void TapMeterReading(object sender, EventArgs e)
    => await UiGuard.RunAsync("menu", async () =>
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }
        await Navigation.PushOnceAsync(() => new ReadingConsumers());
    });
    private async void TapUtilities(object sender, EventArgs e)
    => await UiGuard.RunAsync("menu", async () =>
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }
        dbService = new DBService();
        _ = dbService.SaveUserActivity("Main Menu", "Open Utilities", "Success");   // don't wait for it
        await Navigation.PushOnceAsync(() => new Utilities());
    });

    private async void TapLogout(object sender, TappedEventArgs e)
    => await UiGuard.RunAsync("menu", async () =>
    {
        if (!await DisplayAlertAsync("Notice", "Are you sure you want to log out?", "Yes", "No")) return;
        dbService = new DBService();
        await dbService.SaveUserActivity("Mainmenu", "logout", "Success");
        LoginUser.login_user = new Users();
        // fresh stack: the Android Back button can no longer return to the Main Menu
        Application.Current!.Windows[0].Page = new NavigationPage(new Login());
    });
    private async void TapCollection(object sender, TappedEventArgs e)
    => await UiGuard.RunAsync("menu", async () =>
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }
        await DisplayAlertAsync("Notice", "This feature is not yet available for this version.", "Ok");
    });

    private async void TapReports(object sender, TappedEventArgs e)
        => await UiGuard.RunAsync("menu", async () =>
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }
        await Navigation.PushOnceAsync(() => new Reports());
    });
    private async void TapProductKey(object sender, TappedEventArgs e)
    => await UiGuard.RunAsync("menu", async () =>
    {
        await Navigation.PushOnceAsync(() => new ProductActivation());
    });
}