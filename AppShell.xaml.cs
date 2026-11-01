using eMRBCS.Pages;

namespace eMRBCS
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(ActivateProduct), typeof(ActivateProduct));
            //Routing.RegisterRoute(nameof(LoadingScreen), typeof(LoadingScreen));
        }
    }
}
