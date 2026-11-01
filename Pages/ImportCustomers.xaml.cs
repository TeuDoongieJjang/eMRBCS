using eMRBCS.Global;
using SharpCifs.Smb;
using SharpCifs.Util.Sharpen;

namespace eMRBCS.Pages;

public partial class ImportCustomers : ContentPage
{
    int isWeb = 0;
    int isLocal = 0;
    int isBT = 0;
    int isConnected = 0;

    string config_ipaddress = "";
    string config_folder = "";
    string config_username = "";
    string config_password = "";
    SmbFile folder = null;
    string smbPath = "";
    List<string> localfiles = null;



    public ImportCustomers()
    {
        InitializeComponent();

        btnWebAPI.BackgroundColor = Colors.Red;
        btnLocal.BackgroundColor = Colors.Red;
        btnBluetooth.BackgroundColor = Colors.Red;

        //Load the loca files
        try
        {
            string localfolder = FileSystem.AppDataDirectory;
            string[] filePaths = Directory.GetFiles(localfolder);
            localfiles = filePaths.Select(Path.GetFileName).ToList();

            FilesListView1.ItemsSource = localfiles;
        }
        catch (Exception ex)
        {
            lblNotice.Text = ex.Message;
        }


    }



    private async Task CopyFileAsync(string networkPath, string localPath)
    {
        await Task.Run(() =>
        {
            SmbFile smbFile = null;
            SmbFileInputStream inputStream = null;
            FileStream fileStream = null;

            try
            {
                smbFile = new SmbFile(networkPath);
                inputStream = new SmbFileInputStream(smbFile);
                fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write);

                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileStream.Write(buffer, 0, bytesRead);
                }
            }
            finally
            {
                inputStream?.Close();
                fileStream?.Close();
            }
        });
    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new MainPage());
    }

    private async void OnClickWeb(object sender, EventArgs e)
    {
        if (isWeb == 0)
        {
            btnWebAPI.BackgroundColor = Colors.Blue;
            btnBluetooth.BackgroundColor = Colors.Red;
            btnLocal.BackgroundColor = Colors.Red;
            isWeb = 1;
            isLocal = 0;
            isBT = 0;
            FilesListView.ItemsSource = null;

            lblStatus.Text = "Offline";
            isConnected = 0;
            colorStatus.BackgroundColor = Colors.Red;
            lblNotice.Text = "Connection not available.";
        }


    }

    private async void OnClickLocal(object sender, EventArgs e)
    {
        if (isLocal == 0)
        {
            btnWebAPI.BackgroundColor = Colors.Red;
            btnBluetooth.BackgroundColor = Colors.Red;
            btnLocal.BackgroundColor = Colors.Blue;
            isWeb = 0;
            isLocal = 1;
            isBT = 0;

            config_ipaddress = Serverdetails.ImportServerIPAddress;
            config_folder = Serverdetails.ImportSharedFolder;
            config_username = Serverdetails.ImportServerUserName;
            config_password = Serverdetails.ImportServerPassword;

            smbPath = "smb://" + config_username + ":" + config_password + "@" + config_ipaddress + "/" + config_folder + "/";

            folder = new SmbFile(smbPath);

            try
            {
                var files = folder.ListFiles();
                var list = new List<string>();


                foreach (var file in files)
                {
                    list.Add(file.GetName());


                }

                FilesListView.ItemsSource = list;

                lblStatus.Text = "Online";
                isConnected = 1;
                colorStatus.BackgroundColor = Colors.Green;
                lblNotice.Text = "You are connected. Displaying Files.";


            }
            catch (Exception ex)
            {
                lblStatus.Text = "Offline";
                colorStatus.BackgroundColor = Colors.Red;
                lblNotice.Text = ex.Message;

                isConnected = 0;
                //await DisplayAlertAsync("Notice", ex.Message, "Ok");
            }
        }


    }

    private async void OnClickBT(object sender, EventArgs e)
    {
        if (isBT == 0)
        {
            btnWebAPI.BackgroundColor = Colors.Red;
            btnBluetooth.BackgroundColor = Colors.Blue;
            btnLocal.BackgroundColor = Colors.Red;
            isWeb = 0;
            isLocal = 0;
            isBT = 1;

            isConnected = 0;
            FilesListView.ItemsSource = null;

            lblStatus.Text = "Offline";
            colorStatus.BackgroundColor = Colors.Red;
            lblNotice.Text = "Connection not available.";
        }


    }

    private async void OnImportFile(object sender, EventArgs e)
    {
        if (FilesListView.SelectedItem != null)
        {
            string selectedFile = FilesListView.SelectedItem.ToString();

            DateTime today = DateTime.Now;
            string newFileName = selectedFile;

            string localPath = Path.Combine(FileSystem.AppDataDirectory, newFileName);

            string sourcePath = smbPath + selectedFile;

            await CopyFileAsync(sourcePath, localPath);

            await DisplayFiles();

            FilesListView1.ItemsSource = localfiles;

            lblNotice.Text = "File import success!";

            await DisplayAlertAsync("Success", "File copied successfully!", "OK");
        }
        else
        {
            await DisplayAlertAsync("Failed", "No file selected", "OK");
        }

    }

    private async Task DisplayFiles()
    {
        await Task.Run(() =>
        {
            try
            {
                string localfolder = FileSystem.AppDataDirectory;
                string[] filePaths = Directory.GetFiles(localfolder);
                localfiles = filePaths.Select(Path.GetFileName).ToList();

            }
            catch (Exception ex)
            {

                lblNotice.Text = ex.Message;

            }
        });
    }

    private async void OnExportFile(object sender, EventArgs e)
    {
        if (isConnected == 1)
        {

            if (FilesListView1.SelectedItem != null)
            {
                string selectedFile = FilesListView1.SelectedItem.ToString();

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
                //smbFile = new SmbFile(networkPath);
                //inputStream = new SmbFileInputStream(smbFile);

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