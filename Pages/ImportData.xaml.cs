
using eMRBCS.Global;
using eMRBCS.Models;
using MSRBC.Services;
using SharpCifs.Smb;
using SQLite;

namespace eMRBCS.Pages;

public partial class ImportData : ContentPage
{
    private readonly FileService _fileService = new();
    int isConnected = 0;

    string config_ipaddress = "";
    string config_folder = "";
    string config_username = "";
    string config_password = "";
    SmbFile folder = null;
    string smbPath = "";
    List<string> localfiles = null;

    private static SQLiteConnection Database;
    string targetfilename = database_info.dbName;

    List<Remarks> remark_items;
    List<Consumers> consumer_items;

    int noofconsumer = 0;
    int noofremarks = 0;

    public ImportData()
    {
        InitializeComponent();

        btnCopy.IsVisible = false;
    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }


    private async void OnClickConnect(object sender, EventArgs e)
    {
        // Already connected -> this tap means "Disconnect"
        if (isConnected == 1)
        {
            isConnected = 0;
            btnConnect.BackgroundColor = Colors.Red;
            btnConnect.Text = "Connect";
            lblNoData.IsVisible = true;
            FilesListView.ItemsSource = null;
            lblNotice.Text = "No connection available.";
            btnCopy.IsVisible = false;
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
            // import side (1), lists the SOURCE folder (or whatever the configured path resolves to)
            result = await _fileService.ListFilesAsync(1, FileService.ImportSubFolder)
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
            FilesListView.ItemsSource = result.Files;
            lblNoData.IsVisible = result.Files.Count == 0;
            isConnected = 1;
            btnConnect.BackgroundColor = Colors.Green;
            btnConnect.Text = "Disconnect";
            lblNotice.Text = "You are connected. Displaying Files.";
            btnCopy.IsVisible = true;
        }
        else
        {
            isConnected = 0;
            btnConnect.BackgroundColor = Colors.Red;
            btnConnect.Text = "Connect";
            lblNotice.Text = result.Message;
            lblNoData.IsVisible = true;
            FilesListView.ItemsSource = null;
            btnCopy.IsVisible = false;
            await DisplayAlertAsync("Connection Error", result.Message, "Ok");
        }
    }
    private async void OnImportFile(object sender, EventArgs e)
    {
        if (FilesListView.SelectedItem == null)
        {
            await DisplayAlertAsync("Failed", "No file selected", "OK");
            return;
        }

        string selectedFile = FilesListView.SelectedItem.ToString()!;
        if (!await DownloadAsync(selectedFile)) return;

        lblNotice.Text = "File import success!";
        await DisplayAlertAsync("Success", "File restored successfully! You need to restart the application in order to apply the changes.", "OK");
    }
    /// Downloads one file from the import folder into AppDataDirectory.
    private async Task<bool> DownloadAsync(string fileName)
    {
        string localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
        string r = await _fileService.DownloadFileAsync(1, fileName, localPath);

        var parts = r.Split(';', 2);
        if (parts[0].Trim() == "success") return true;

        string msg = parts.Length > 1 ? parts[1] : "Download failed.";
        lblNotice.Text = msg;
        await DisplayAlertAsync("Error", msg, "Ok");
        return false;
    }

    private async void OnGetConsumer(object sender, EventArgs e)
    {
        if (FilesListView.SelectedItem != null)
        {
            string selectedFile = FilesListView.SelectedItem.ToString();

            if (selectedFile.Contains("CONSUMER"))
            {
                DateTime today = DateTime.Now;
                string newFileName = selectedFile;

                if (!await DownloadAsync(newFileName)) return;



                await ConsumerReadAndProcessileAsync(newFileName);

                lblNotice.Text = "Consumer File import success!";

                await DisplayAlertAsync("Success", "Consumer file imported successfully! New records added: " + noofremarks, "OK");
            }
            else
            {
                lblNotice.Text = "Consumer File error!";
                await DisplayAlertAsync("Failed", "Selected Consumer file not vald!", "OK");
            }


        }
        else
        {
            await DisplayAlertAsync("Failed", "No file selected", "OK");
        }

    }

    private async void OnGetRemarks(object sender, EventArgs e)
    {
        if (FilesListView.SelectedItem != null)
        {
            string selectedFile = FilesListView.SelectedItem.ToString();

            if (selectedFile.Contains("REMARK"))
            {
                DateTime today = DateTime.Now;
                string newFileName = selectedFile;

                if (!await DownloadAsync(newFileName)) return;


                await ReadAndProcessRemarkFileAsync(newFileName);

                lblNotice.Text = "Remark File import success!";

                await DisplayAlertAsync("Success", "Remark file imported successfully! New records added: " + noofremarks, "OK");
            }
            else
            {
                lblNotice.Text = "Remark File error!";
                await DisplayAlertAsync("Failed", "Selected Remark file not vald!", "OK");
            }


        }
        else
        {
            await DisplayAlertAsync("Failed", "No file selected", "OK");
        }

    }

    private void InitializeDatabase()
    {
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, targetfilename);
        Database = new SQLiteConnection(dbPath);
    }

    private async Task ReadAndProcessRemarkFileAsync(string remarkfile)
    {

        string newcodeid;
        string newdescription;

        InitializeDatabase();

        remark_items = new List<Remarks>();

        string filePath = Path.Combine(FileSystem.AppDataDirectory, remarkfile);

        using (StreamReader reader = new StreamReader(filePath))
        {
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                string[] items = line.Split('~');
                newcodeid = items[0].Trim();
                newdescription = items[1].Trim();

                if (!string.IsNullOrWhiteSpace(newcodeid))
                {
                    Remarks newItem = new Remarks
                    {
                        codeid = int.Parse(newcodeid),
                        description = newdescription
                    };

                    remark_items.Add(newItem);

                }

            }
        }

        SaveItemsToDatabase(remark_items);

    }

    private static int CheckRecordExists(int codeid)
    {
        var remark = Database.Table<Remarks>().FirstOrDefault(p => p.codeid == codeid);
        return remark == null ? 0 : 1;
    }

    private void SaveItemsToDatabase(List<Remarks> items)
    {
        noofremarks = 0;
        foreach (var item in items)
        {
            int isExists = CheckRecordExists(item.codeid);
            if (isExists == 0)
            {
                noofremarks++;
                Database.Insert(item);
            }


        }
    }


    private static float ParseFloat(string value)
    {
        if (float.TryParse(value, out float result))
        {
            return result;
        }
        else
        {
            return 0f;
        }


    }

    private async Task ConsumerReadAndProcessileAsync(string remarkfile)
    {
        string accountNo;
        string consumer_name;
        string address;
        string meterno;
        float prevreading;
        float presreading;
        string consumer_status;
        float arrears;
        float average_usage;
        float special_penalty_rate;
        string billdate;
        string duedate;
        int metersize;
        float othercharges;
        int consumer_type;
        long seqno;
        string othercharges_details;
        int agecurrent;
        float penaltyamt;
        int isnopenalty;
        int minusage;
        float combine_rates;
        float change_meter_usage;
        string zonecode;
        string prevreadingdate;
        int unpaidmonths;
        string waterbill_details;
        float sewerage;
        string parameters;

        InitializeDatabase();

        consumer_items = new List<Consumers>();

        string filePath = Path.Combine(FileSystem.AppDataDirectory, remarkfile);

        using (StreamReader reader = new StreamReader(filePath))
        {
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                string[] items = line.Split('~');
                accountNo = items[0].Trim();

                if (!string.IsNullOrWhiteSpace(accountNo))
                {
                    Consumers newItem = new Consumers
                    {
                        accountNo = items[0].Trim(),
                        consumer_name = items[1].Trim(),
                        address = items[2].Trim(),
                        meterno = items[3].Trim(),
                        prevreading = ParseFloat(items[4]),
                        presreading = 0,
                        consumer_status = items[6].Trim(),
                        arrears = ParseFloat(items[7]),
                        average_usage = ParseFloat(items[8]),
                        special_penalty_rate = ParseFloat(items[9]),
                        billdate = items[10].Trim(),
                        duedate = items[11].Trim(),
                        metersize = int.Parse(items[12].Trim()),
                        othercharges = ParseFloat(items[13].Trim()),
                        consumer_type = int.Parse(items[14].Trim()),
                        seqno = long.Parse(items[15].Trim()),
                        othercharges_details = items[16].Trim(),
                        agecurrent = int.Parse(items[17].Trim()),
                        penaltyamt = ParseFloat(items[18].Trim()),
                        isnopenalty = items[19].Trim() == "Y" ? 1 : 0,
                        minusage = int.Parse(items[20].Trim()),
                        combine_rates = items[21].Trim(),
                        change_meter_usage = ParseFloat(items[22].Trim()),
                        zonecode = items[23].Trim(),
                        prevreadingdate = items[24].Trim(),
                        unpaidmonths = int.Parse(items[25].Trim()),
                        waterbill_details = items[26].Trim(),
                        sewerage = ParseFloat(items[27].Trim()),
                        parameters = items[28].Trim()
                    };

                    consumer_items.Add(newItem);

                }

            }
        }

        ConsumerSaveItemsToDatabase(consumer_items);

    }

    private static int ConsumerCheckRecordExists(string acctno)
    {
        var consumer = Database.Table<Consumers>().FirstOrDefault(p => p.accountNo == acctno);
        return consumer == null ? 0 : 1;
    }

    private void ConsumerSaveItemsToDatabase(List<Consumers> items)
    {
        noofconsumer = 0;
        foreach (var item in items)
        {

            int isExists = ConsumerCheckRecordExists(item.accountNo);
            if (isExists == 0)
            {
                noofconsumer++;
                Database.Insert(item);
            }


        }
    }


}