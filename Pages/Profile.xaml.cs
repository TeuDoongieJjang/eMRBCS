using eMRBCS.Global;
using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class Profile : ContentPage
{
    DBService dbService;
	public Profile()
	{
		InitializeComponent();

        txtname.Text = LoginUser.login_user.fullname;
        txtposition.Text = LoginUser.user_position;
        txtusername.Text = LoginUser.login_user.username;
        dbService = new DBService();    

	}

    private async void OnSaveChanges(object sender, EventArgs e)
    {
        bool isSaved = await dbService.UpdateAdminUser(txtusername.Text, LoginUser.login_user.password, txtname.Text, txtposition.Text);
        if (isSaved)
        {
            await DisplayAlertAsync("Notice", "Information has been updated!", "Ok");           
        }
        else
        {
            await DisplayAlertAsync("Notice", "Something went wrong. Failed to Update details.", "Ok");
        }
    }

    private void OnUpdatePassword(object sender, EventArgs e)
    {
        gridChangePass.IsVisible = true;
        txtoldpass.Text = "";
        txtnewpass.Text = "";
        txtconfirmpass.Text = "";
        btnSaveChanges.IsEnabled = false;
        btnUpdatePass.IsEnabled = false;
       
    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnSavePass(object sender, EventArgs e)
    {
        if (txtnewpass.Text == txtconfirmpass.Text && txtoldpass.Text == LoginUser.login_user.password )
        {
            bool isSaved = await dbService.UpdateAdminUser(txtusername.Text, txtnewpass.Text, txtname.Text, txtposition.Text);
            if (isSaved)
            {
                await DisplayAlertAsync("Notice", "Password Updated!", "Ok");
                btnSaveChanges.IsEnabled = true;
                btnUpdatePass.IsEnabled = true;
                gridChangePass.IsVisible = false;
            }
            else
            {
                await DisplayAlertAsync("Notice", "Something went wrong. Failed to Update Password.", "Ok");
            }
        }
    }

    private void OnCancel(object sender, EventArgs e)
    {
        btnSaveChanges.IsEnabled = true;
        btnUpdatePass.IsEnabled = true;
        gridChangePass.IsVisible = false;
    }
}