using eMRBCS.Models;
using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class UserManagement : ContentPage
{
    List<Users_admin> adminusers;
    DBService dbService;

    bool isNewuser = false;
    public UserManagement()
    {
        InitializeComponent();
        LoadUsers();

    }

    private async void LoadUsers()
    {
        adminusers = new List<Users_admin>();
        dbService = new DBService();
        adminusers = await dbService.LoadAllAdmins();
        List<string> user_fullnames = new List<string>();
        if (adminusers != null && adminusers.Count > 0)
        {

            foreach (Users_admin user in adminusers)
            {
                user_fullnames.Add(user.name);
            }
            lstUsers.ItemsSource = user_fullnames;
            lblnodata.IsVisible = false;
            lstUsers.IsVisible = true;
        }
        else
        {
            lblnodata.IsVisible = true;
            lstUsers.IsVisible = false;

        }



    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnEditUser(object sender, EventArgs e)
    {
        if (lstUsers.SelectedItem != null)
        {
            btnSaveUser.IsEnabled = true;
            btnEditUser.IsEnabled = false;
            btnNewUser.IsEnabled = false;
            lstUsers.IsEnabled = false;

            Users_admin _selectedAdmin = new Users_admin();
            _selectedAdmin = adminusers.Where(a => a.name == lstUsers.SelectedItem).FirstOrDefault();
            if (_selectedAdmin != null)
            {
                txtname.Text = _selectedAdmin.name;
                txtposition.Text = _selectedAdmin.position;
                txtusername.Text = _selectedAdmin.username;
                txtpassword.Text = _selectedAdmin.password;
            }

        }
    }

    private void OnNewUser(object sender, EventArgs e)
    {
        btnSaveUser.IsEnabled = true;
        btnEditUser.IsEnabled = false;
        btnNewUser.IsEnabled = false;
        lstUsers.IsEnabled = false;
        isNewuser = true;
        txtname.Text = "";
        txtposition.Text = "";
        txtusername.Text = "";
        txtpassword.Text = "";

    }

    private async void OnSaveUser(object sender, EventArgs e)
    {
        if (isNewuser)
        {
            Users_admin _selectedAdmin = new Users_admin();
            _selectedAdmin = adminusers.Where(a => a.name == txtusername.Text).FirstOrDefault();
            if (_selectedAdmin != null)
            {
                await DisplayAlertAsync("Notice", "Username already exists. Please try again", "Ok");
                return;
            }
        }

        bool isSucess = await dbService.SaveAdminOperators(txtusername.Text, txtpassword.Text, txtname.Text, txtposition.Text);
        if (isSucess)
        {
            await DisplayAlertAsync("Notice", "User has been saved!", "Ok");
            btnSaveUser.IsEnabled = false;
            btnEditUser.IsEnabled = true;
            btnNewUser.IsEnabled = true;
            lstUsers.IsEnabled = true;
            LoadUsers();
            lstUsers.Focus();
        }
        else
        {
            await DisplayAlertAsync("Notice", "Something went wrong. Failed to save user!", "Ok");
        }

    }
}