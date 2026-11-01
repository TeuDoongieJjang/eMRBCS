using Microsoft.Maui.Controls;
using eMRBCS.Global;
using SharpCifs.Smb;
using SharpCifs.Util.Sharpen;

namespace eMRBCS.Pages;

public partial class ExportData : ContentPage
{
    int isConnected = 0;

    string config_ipaddress = "";
    string config_folder = "";
    string config_username = "";
    string config_password = "";
    SmbFile folder = null;
    string smbPath = "";
    List<string> localfiles = null;
    public ExportData()
	{
		InitializeComponent();


        //Load the loca files
        try
        {
            string localfolder = FileSystem.AppDataDirectory;
            string[] filePaths = Directory.GetFiles(localfolder);
            localfiles = filePaths.Select(Path.GetFileName).ToList();

            if (localfiles != null)
            {
                FilesListView.ItemsSource = localfiles;
                lblNoData.IsVisible = false;
            }
            else
            {
                lblNoData.IsVisible = true;    
            }

            


        }
        catch (Exception ex)
        {
            lblNotice.Text = ex.Message;
        }
    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new Utilities());
    }

    private async void OnClickConnect(object sender, EventArgs e)
    {
        if (isConnected == 0)
        {

            config_ipaddress = Serverdetails.ExportServerIPAddress;
            config_folder = Serverdetails.ExportSharedFolder;
            config_username = Serverdetails.ExportServerUserName;
            config_password = Serverdetails.ExportServerPassword;

            smbPath = "smb://" + config_username + ":" + config_password + "@" + config_ipaddress + "/" + config_folder + "/";
            folder = new SmbFile(smbPath);

            try
            {
                var files = folder.ListFiles();
               

                lblNoData.IsVisible = false;                
                isConnected = 1;
                btnConnect.BackgroundColor = Colors.Green;
                btnConnect.Text = "Disconnect";
                lblNotice.Text = "You are connected to the server.";
                btnCopy.IsVisible = true;


            }
            catch (Exception ex)
            {

                btnConnect.BackgroundColor = Colors.Red;
                btnConnect.Text = "Connect";
                lblNotice.Text = ex.Message;
                lblNoData.IsVisible = true;
                isConnected = 0;
                FilesListView.ItemsSource = null;
                btnCopy.IsVisible = false;
            }
        }
        else
        {
            config_ipaddress = "";
            config_folder = "";
            config_username = "";
            config_password = "";
            
            btnConnect.BackgroundColor = Colors.Red;
            btnConnect.Text = "Connect";
            isConnected = 0;
            
            lblNotice.Text = "Not connected to server.";
          
        }


    }

    private async void OnExportFile(object sender, EventArgs e)
    {
        if (isConnected == 1)
        {

            if (FilesListView.SelectedItem != null)
            {
                string selectedFile = FilesListView.SelectedItem.ToString();

                DateTime today = DateTime.Now;
                string newFileName = selectedFile;

                await CopyFileAsync2(selectedFile, newFileName);

                await DisplayAlertAsync("Success", "File exported successfully!", "OK");
            }
            else
            {
                await DisplayAlertAsync("Failed", "No file selected", "OK");
            }
        }
        else
        {
            await DisplayAlertAsync("Failed", "Not connected to server", "OK");
        }

    }

    private async Task CopyFileAsync2(string sourcefile, string targetfile)
    {
        await Task.Run(() =>
        {
            SmbFile smbFile = null;
            FileInputStream inputStream = null;
            SmbFileOutputStream fileStream = null;

            try
            {
                string appDataDirectory = FileSystem.AppDataDirectory;
                string localFilePath = Path.Combine(appDataDirectory, sourcefile);

                inputStream = new FileInputStream(localFilePath);

                var newSMBFile = smbPath + targetfile;

                fileStream = new SmbFileOutputStream(newSMBFile);

                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileStream.Write(buffer, 0, bytesRead);
                }
            }
            catch (Exception ex)
            {
                DisplayAlertAsync("Error", $"An error occurred: {ex.Message}", "OK");
            }
            finally
            {
                inputStream?.Close();
                fileStream?.Close();
            }
        });
    }
}