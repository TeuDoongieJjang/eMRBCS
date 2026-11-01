using Microsoft.Maui.ApplicationModel;
using eMRBCS.Global;
using static System.Net.WebRequestMethods;

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
		await Navigation.PopAsync();
    }

    private async void Tapprofile(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new Profile());
    }

    private async void TapAddUser(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new UserManagement());
    }

    private async void TapUserActivity(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new UserActivity());
    }
}