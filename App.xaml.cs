using eMRBCS.Pages;

namespace eMRBCS
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            //MainPage = new AppShell();
            //MainPage = new NavigationPage(new ActivateProduct());
            MainPage = new NavigationPage(new LoadingScreen());
        }
    }
}
