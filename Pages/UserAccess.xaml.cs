using eMRBCS.Global;
using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class UserAccess : ContentPage
{
    public UserAccess()
    {
        InitializeComponent();
        ScreenAdjustment();

    }

    private void ScreenAdjustment()
    {
        if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {

        }
    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopOnceAsync();
    }

    private async void Tapprofile(object sender, TappedEventArgs e)
    {
        await Navigation.PushOnceAsync(() => new Profile());
    }

    private async void TapAddUser(object sender, TappedEventArgs e)
    {
        await Navigation.PushOnceAsync(() => new UserManagement());
    }

    private async void TapUserActivity(object sender, TappedEventArgs e)
    {
        await Navigation.PushOnceAsync(() => new UserActivity());
    }
}