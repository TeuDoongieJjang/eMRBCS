using eMRBCS.Pages;
using eMRBCS.Services;

namespace eMRBCS
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            StartupService.CaptureDeviceInfo();                 // cheap, needed by Login layout
            MainPage = new NavigationPage(new Login());         // was LoadingScreen
            StartupService.Instance.Start();                    // parallel background loading
        }
    }
}