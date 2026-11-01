using Microsoft.Maui;
using eMRBCS.Global;
using System.IO;
using System.Reflection;
using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class MainPage : ContentPage
{
    int count = 0;

    static sqlitecon database;

    public static sqlitecon Database
    {
        get
        {
            if (database == null)
            {
                string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Datafiles/mrbcs_main.db");
                database = new sqlitecon(dbPath);
            }
            return database;
        }
    }

    public MainPage()
    {
        InitializeComponent();


    }


    private async void WriteSampleFile()
    {
        string fileName = "sample.txt";
        string content = "This is a sample file content.";

        string filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

        using (StreamWriter writer = new StreamWriter(filePath, false))
        {
            await writer.WriteAsync(content);
        }
    }

    private async void OnCounterClicked(object sender, EventArgs e)
    {
        

        string fileName = "sample.txt";
        string filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

        if (File.Exists(filePath))
        {
            string content = await ReadFileAsync(filePath);
            //FileContent.Text = content;
        }
        else
        {
            //FileContent.Text = "File not found.";
        }
    }

    private async void TapImgTransferFile(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new ImportCustomers());
    }

    private async void TapImgAppConfig(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new AppSettings());
    }

    private async void TapMeterReading(object sender, EventArgs e)
    {
        //await Navigation.PushAsync(new Reading());
        ReadAndProcessFileAsync();
    }

    private async Task ReadAndProcessFileAsync()
    {
        int remarkid;
        string codeid;
        string description;

        using var stream = await FileSystem.OpenAppPackageFileAsync("Datafiles/REMARKS.txt");
        //using var reader = new StreamReader(stream);
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                string[] items = line.Split('~');
                codeid = items[0].Trim();
                description= items[1].Trim();
                
            }
        }

        
    }

    

    private async Task<string> ReadFileAsync(string filePath)
    {
        using (StreamReader reader = new StreamReader(filePath))
        {
            return await reader.ReadToEndAsync();
        }
    }
}