using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using System.Security.Cryptography;
using System.Text;
using System.Timers;


namespace eMRBCS.Pages;

public partial class Login : ContentPage
{
    Users loginuser;
    DBService dbService;

    //private System.Timers.Timer _systimer;

    int isServerValid = 0;
    string fileContent = "";
    string profiles;

    private static string logoBase64ImgStr = null;

    string encryptedText;

    public Login()
    {
        InitializeComponent();
        SetAppVersion();




        lbldatetime.Text = DateTime.Now.ToString("yyyy-MM-dd");

        ScreenAdjustment();

    }

    private void SetAppVersion()
    {
        // 1. Get the version string applied by your GitHub Actions workflow
        string rawVersion = AppInfo.Current.VersionString;

        // 2. Strip the pre-release suffix (e.g., "1.0.1-2" becomes "1.0.1")
        string fullVersion = rawVersion.Split('-')[0];

        // 3. Apply the 'v' prefix and set it to the label
        lblversion.Text = $"v{fullVersion}";
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

        await Task.Delay(2000);
        isServerValid = Serverdetails.Validnetwork;
        if (isServerValid == 0)
        {

            await DisplayAlertAsync("Notice", "Network address invalid.\nPlease contact your Administrator", "Ok");
        }



    }

    private void ScreenAdjustment()
    {
        if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {
            lblcompany.FontSize = 16;
            lblcompany.Margin = new Thickness(0, 0, 0, 0);
            lblversion.FontSize = 12;
            lblversion.Margin = new Thickness(0, 0, 0, 0);
            lblcredentials.Margin = new Thickness(10, 5, 10, 5);
        }
    }


    private void OnTimedEvent(Object source, ElapsedEventArgs e)
    {

        MainThread.BeginInvokeOnMainThread(() =>
        {
            lbldatetime.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
        });
    }

    private async void OnLoginButtonClicked(object sender, EventArgs e)
    {
        string username = usernameEntry.Text;
        string password = passwordEntry.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {

            await DisplayAlertAsync("Notice", "Please enter both username and password.", "Ok");
            return;
        }

        bool isValid = await ValidateLogin(username, password);
        if (isValid)
        {
            await Navigation.PushAsync(new Mainmenu());
        }
        else
        {
            //
            await DisplayAlertAsync("Notice", "Invalid username or password.", "Ok");
        }
    }


    private async Task<bool> ValidateLogin(string username, string password)
    {
        loginuser = new Users();
        dbService = new DBService();
        bool isValidUser = false;

        //Try to get if the user is an admin
        Users_admin adminuser = await dbService.GeAdminUser(username, password);
        if (adminuser != null)
        {
            loginuser = new Users
            {
                userid = adminuser.id,
                username = adminuser.username,
                fullname = adminuser.name,
                trx_mode = adminuser.position,
                password = password,
            };

            LoginUser.login_user = loginuser;
            LoginUser.user_position = adminuser.position;
            LoginUser.role = adminuser.role;
            bool isLog = await dbService.SaveUserActivity("Login", "login", "Success");
            isValidUser = true;
        }

        if (!isValidUser)
        {
            if (Serverdetails.Validnetwork == 1)
            {
                loginuser = await dbService.GetLoginUser(username, password);
                if (loginuser != null)
                {
                    LoginUser.login_user = loginuser;
                    LoginUser.user_position = "Operator";
                    LoginUser.role = 3;
                    bool isLog = await dbService.SaveUserActivity("Login", "login", "Success");
                    return true;
                }
            }
        }

        return isValidUser;
    }

    private void DisplayGetScreenInfo(object sender, EventArgs e)
    {

        var mainDisplayInfo = DeviceDisplay.MainDisplayInfo;


        double screenWidth = mainDisplayInfo.Width;
        double screenHeight = mainDisplayInfo.Height;


        double density = mainDisplayInfo.Density;


        double screenWidthInDip = screenWidth / density;
        double screenHeightInDip = screenHeight / density;

        string info = screenWidth.ToString() + " : " + screenHeight.ToString() + " : " + screenWidthInDip.ToString() + " : " + screenHeightInDip.ToString() + " : " + density.ToString();

        DisplayAlertAsync("Notice", info, "Ok");
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



    public static string EncryptString(string plainText, string passphrase)
    {


        using (var aes = Aes.Create())
        {
            var keyDerivationFunction = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(passphrase), 10000);
            aes.Key = keyDerivationFunction.GetBytes(32); // 256-bit key
            aes.IV = keyDerivationFunction.GetBytes(16);  // 128-bit IV


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

    public static string DecryptString(string encryptedText1, string passphrase)
    {

        using (var aes = Aes.Create())
        {
            var keyDerivationFunction = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(passphrase), 10000);
            aes.Key = keyDerivationFunction.GetBytes(32);
            aes.IV = keyDerivationFunction.GetBytes(16);


            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream(Convert.FromBase64String(encryptedText1)))
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            using (var sr = new StreamReader(cs))
            {
                return sr.ReadToEnd();
            }
        }
    }

    private async void DoDecrypt(object sender, EventArgs e)
    {
        string decryptedText = DecryptString(encryptedText, "manny");



        await DisplayAlertAsync("Notice", encryptedText.ToUpper() + "\n" + decryptedText.ToUpper() + "\n" + MyDeviceInfo.device_AndroidID, "Ok");
    }
}