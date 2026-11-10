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

    private CancellationTokenSource? _pollCts;
    private bool _checking;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        stackLoading.IsVisible = false;

        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        StartStatusPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _pollCts?.Cancel();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        => _ = RefreshServerStatusAsync(delayMs: 1500);     // give Wi-Fi a moment to get its IP

    private void StartStatusPolling()
    {
        _pollCts?.Cancel();
        var cts = _pollCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await RefreshServerStatusAsync();
                try { await Task.Delay(TimeSpan.FromSeconds(10), cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        });
    }

    private async Task RefreshServerStatusAsync(int delayMs = 0)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        if (_checking) return;                              // never overlap checks
        _checking = true;
        try
        {
            bool ok = false;
            if (Serverdetails.Validnetwork == 1)
                ok = await new FileService().CheckSMBStatus();

            MainThread.BeginInvokeOnMainThread(() =>
                frameStatus.BackgroundColor = ok ? Colors.Green : Colors.Red);
        }
        finally { _checking = false; }
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
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }
        await Navigation.PushAsync(new ReadingConsumers());   // no overlay: data is already in the store
    }
    private async void TapUtilities(object sender, EventArgs e)
    {
        if (LoginUser.role > 1 && !MyDeviceInfo.isActivated)
        {
            await DisplayAlertAsync("Notice", "Please activate your application.", "Ok");
            return;
        }
        dbService = new DBService();
        _ = dbService.SaveUserActivity("Main Menu", "Open Utilities", "Success");   // don't wait for it
        await Navigation.PushAsync(new Utilities());
    }

    private async void TapLogout(object sender, TappedEventArgs e)
    {
        if (!await DisplayAlertAsync("Notice", "Are you sure you want to log out?", "Yes", "No")) return;
        dbService = new DBService();
        await dbService.SaveUserActivity("Mainmenu", "logout", "Success");
        LoginUser.login_user = new Users();
        // fresh stack: the Android Back button can no longer return to the Main Menu
        Application.Current!.Windows[0].Page = new NavigationPage(new Login());
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
        await Navigation.PushAsync(new Reports());
    }
    private async void TapProductKey(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new ProductActivation());
    }
}