using eMRBCS.Global;
using eMRBCS.Services;
using MSRBC.Services;

namespace eMRBCS.Pages;

public partial class ExportData : ContentPage
{
    int isConnected = 0;
    private readonly FileService _fileService = new();

    public ExportData()
    {
        InitializeComponent();

        try
        {
            var localfiles = Directory.GetFiles(FileSystem.AppDataDirectory)
                                      .Select(Path.GetFileName)
                                      .ToList();

            FilesListView.ItemsSource = localfiles;
            lblNoData.IsVisible = localfiles.Count == 0;
        }
        catch (Exception ex)
        {
            lblNotice.Text = ex.Message;
        }
    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PushOnceAsync(() => new Utilities());
    }

    private async void OnClickConnect(object sender, EventArgs e)
    {
        if (isConnected == 1)
        {
            isConnected = 0;
            btnConnect.BackgroundColor = Colors.Red;
            btnConnect.Text = "Connect";
            lblNotice.Text = "Not connected to server.";
            return;
        }

        if (Serverdetails.Validnetwork == 0)
        {
            await DisplayAlertAsync("Warning", "Invalid Server address. Please contact administrator.", "Ok");
            return;
        }

        btnConnect.IsEnabled = false;
        lblNotice.Text = "Connecting...";

        (bool Success, string Message, List<string> Files) result;
        try
        {
            // export side (0). Listing the folder proves it exists, so a missing OUTPUT folder
            // is reported here instead of failing later when sending.
            result = await _fileService.ListFilesAsync(0, FileService.ExportSubFolder)
                                       .WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            result = (false, "Connection timed out. Check that you are on the server's network.", new List<string>());
        }
        finally
        {
            btnConnect.IsEnabled = true;
        }

        if (result.Success)
        {
            isConnected = 1;
            btnConnect.BackgroundColor = Colors.Green;
            btnConnect.Text = "Disconnect";
            lblNotice.Text = "You are connected to the server.";
            btnCopy.IsVisible = true;
        }
        else
        {
            isConnected = 0;
            btnConnect.BackgroundColor = Colors.Red;
            btnConnect.Text = "Connect";
            lblNotice.Text = result.Message;
            btnCopy.IsVisible = false;
        }
    }

    private async void OnExportFile(object sender, EventArgs e)
    {
        if (isConnected != 1)
        {
            await DisplayAlertAsync("Failed", "Not connected to server", "OK");
            return;
        }

        if (FilesListView.SelectedItem == null)
        {
            await DisplayAlertAsync("Failed", "No file selected", "OK");
            return;
        }

        string selectedFile = FilesListView.SelectedItem.ToString()!;
        string localPath = Path.Combine(FileSystem.AppDataDirectory, selectedFile);

        btnCopy.IsEnabled = false;
        lblNotice.Text = "Sending file...";
        string r = await _fileService.UploadFileAsync(0, localPath, selectedFile);
        btnCopy.IsEnabled = true;

        var parts = r.Split(';', 2);
        if (parts[0].Trim() == "success")
        {
            lblNotice.Text = "File exported successfully!";
            await DisplayAlertAsync("Success", "File exported successfully!", "OK");
        }
        else
        {
            string msg = parts.Length > 1 ? parts[1] : "Upload failed.";
            lblNotice.Text = msg;
            await DisplayAlertAsync("Error", msg, "OK");
        }
    }
}