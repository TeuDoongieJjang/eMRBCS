using eMRBCS.Global;
using eMRBCS.Services;
using MSRBC.Services;

namespace eMRBCS.Pages;

public partial class AppSettings : ContentPage
{
    DBService dBService;
    FileService fileService;
    public AppSettings()
    {
        InitializeComponent();

        import_ipaddress.Text = Serverdetails.ImportServerIPAddress;
        import_sharedfolder.Text = Serverdetails.ImportSharedFolder;
        import_username.Text = Serverdetails.ImportServerUserName;
        import_pass.Text = Serverdetails.ImportServerPassword;

        export_ipaddress.Text = Serverdetails.ExportServerIPAddress;
        export_sharedfolder.Text = Serverdetails.ExportSharedFolder;
        export_username.Text = Serverdetails.ExportServerUserName;
        export_pass.Text = Serverdetails.ExportServerPassword;
        CheckServerStatus();
        // GetExternalStorage();

    }



    private async void CheckServerStatus()
    {
        fileService = new FileService();
        frameStatus.BackgroundColor = Colors.Red;
        if (Serverdetails.Validnetwork == 1)
        {
            bool isConnected = await fileService.CheckSMBStatus();
            if (isConnected)
            {
                frameStatus.BackgroundColor = Colors.Green;
            }
        }
    }

    public async void OnSaveChanges(object sender, EventArgs e)
    {
        string new_importipaddress = import_ipaddress.Text;
        string new_importfolder = import_sharedfolder.Text;
        string new_importusername = import_username.Text;
        string new_importpassword = import_pass.Text;

        string new_exportipaddress = export_ipaddress.Text;
        string new_exportfolder = export_sharedfolder.Text;
        string new_exportusername = export_username.Text;
        string new_exportpassword = export_pass.Text;

        Serverdetails.ImportServerIPAddress = new_importipaddress;
        Serverdetails.ImportSharedFolder = new_importfolder;
        Serverdetails.ImportServerUserName = new_importusername;
        Serverdetails.ImportServerPassword = new_importpassword;

        Serverdetails.ExportServerIPAddress = new_exportipaddress;
        Serverdetails.ExportSharedFolder = new_exportfolder;
        Serverdetails.ExportServerUserName = new_exportusername;
        Serverdetails.ExportServerPassword = new_exportpassword;

        dBService = new DBService();

        bool isConfigSave = await dBService.NetworkConfigSave();
        if (isConfigSave)
        {
            Serverdetails.Validnetwork = 1;
            CheckServerStatus();
            await DisplayAlertAsync("Notice", "Server Configuration has been saved!", "Ok");
        }
        else
        {
            await DisplayAlertAsync("Error", "Server Configuration failed to saved!", "Ok");
        }


    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new Utilities());
    }

    private void OnCheckSameDetails(object sender, EventArgs e)
    {
        if (chkSameDetails.IsChecked)
        {
            export_ipaddress.Text = import_ipaddress.Text;
            export_sharedfolder.Text = import_sharedfolder.Text;
            export_username.Text = import_username.Text;
            export_pass.Text = import_pass.Text;

            export_ipaddress.IsReadOnly = true;
            export_sharedfolder.IsReadOnly = true;
            export_username.IsReadOnly = true;
            export_pass.IsReadOnly = true;
        }
        else
        {
            export_ipaddress.IsReadOnly = false;
            export_sharedfolder.IsReadOnly = false;
            export_username.IsReadOnly = false;
            export_pass.IsReadOnly = false;
        }


    }




}