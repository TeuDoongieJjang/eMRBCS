using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using Honeywell.AIDC.CrossPlatform.Print;

#if ANDROID
using eMRBCS.Platforms.Android;
#endif

namespace eMRBCS.Pages;

public partial class Reports : ContentPage
{
    DBService dbService;

    string profiles;

    //private static string logoBase64ImgStr = null;
    private string? logoBase64ImgStr;

    string imgLogoPath = "";
    public Reports()
    {
        InitializeComponent();
        LoadReports();

    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(100);

        if (MyDeviceInfo.currentPrintConfig == null)
        {
            await DisplayAlertAsync("Notice", "No Printer Configuration loaded.", "Ok");
            btnPrint.IsEnabled = false;
        }

    }

    private async void LoadReports()
    {
        lstReports.ItemsSource = new List<string>
        {
            "Consumer List with Remarks",
            "Billed Consumers",
            "Unbilled Consumers"
        };
    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnPrintClick(object sender, EventArgs e)
    {
        if (lstReports.SelectedItem == null)
            return;

        string printStat = "";
        List<Consumers> consumers = new List<Consumers>();
        dbService = new DBService();
        string printItem = lstReports.SelectedItem.ToString();
        var selectedConsumer = ConsumerRecords.lstconsumers?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.accountNo));

        switch (printItem)
        {
            case "Consumer List with Remarks":
                consumers = await dbService.LoadReportConsumerswithRemarks(1);
                if (consumers != null && consumers.Count > 0)
                {
                    //printStat = await DoPrintReportConsumerWithRemarks_HoneyWell(consumers);
                    printStat = await DoPrintReportConsumerWithRemarks_EscPOS(selectedConsumer, consumers);
                }
                else
                {
                    printStat = "No consumer records with remarks found. Nothing to print.";
                }
                break;

            case "Billed Consumers":
                consumers = await dbService.LoadReportConsumersToList(1);
                if (consumers != null && consumers.Count > 0)
                {
                    //printStat = await DoPrintBilledSummary_Honeywell(consumers);
                    printStat = await DoPrintBilledSummary_EscPOS(selectedConsumer, consumers);
                }
                else
                {
                    printStat = "No billed consumers found. Nothing to print.";
                }
                break;

            case "Unbilled Consumers":
                consumers = await dbService.LoadReportUnbilledConsumers(1);
                if (consumers != null && consumers.Count > 0)
                {
                    //printStat = await DoPrintUnbilled_Honeywell(consumers);
                    printStat = await DoPrintUnbilled_EscPOS(selectedConsumer, consumers);
                }
                else
                {
                    printStat = "No unbilled consumers found. Nothing to print.";
                }
                break;

            default:
                printStat = "Unknown report selection.";
                break;
        }

        if (!string.IsNullOrWhiteSpace(printStat))
            await DisplayAlertAsync("Notice", printStat, "Ok");
    }






    public async Task<string> DoPrintBilledSummary_Honeywell(List<Consumers> consumers)
    {
        // Guard: never touch the printer with an empty dataset.
        if (consumers == null || consumers.Count == 0)
            return "No billed consumers found. Nothing to print.";

        string result = "";
        //int spacesleft = 0;
        string itExsits = "";
        await Task.Run(() =>
        {

            LinePrinter linePrinter = null;

            try
            {

                ReadProFile();
                //ReadImgToBase64String();

                string bt_address = "bt://" + MyDeviceInfo.currentPrintConfig.bluetooth_address;
                string printer_profile = "PB21"; //MyDeviceInfo.currentPrintConfig.profileconfig;
                int printer_space = 33; //MyDeviceInfo.currentPrintConfig.linespacing;
                string print_dash = new string('-', printer_space);

                linePrinter = new LinePrinter(profiles, printer_profile, bt_address);
                linePrinter.Connect();


                //MOA_Pending - Continue Printing of Logo
                //if (null == logoBase64ImgStr)
                //{
                //    ReadImgToBase64String();
                //}
                //linePrinter.WriteGraphicBase64(logoBase64ImgStr, ImageRotation.DEGREE_0, 0, 0, 0);
                //linePrinter.WriteGraphicBase64(logoBase64ImgStr, ImageRotation.DEGREE_0, 0, 300, 56);

                //if (string.IsNullOrWhiteSpace(logoBase64ImgStr))
                //{
                //    logoBase64ImgStr = await ReadImgToBase64StringAsync();
                //}

                //if (!string.IsNullOrWhiteSpace(logoBase64ImgStr))
                //{
                //    linePrinter.WriteGraphicBase64(logoBase64ImgStr, ImageRotation.DEGREE_0, 0, 0, 0);
                //    linePrinter.WriteGraphicBase64(logoBase64ImgStr, ImageRotation.DEGREE_0, 0, 300, 56);
                //}

                linePrinter.DoubleHigh = true;
                string title1 = "    Summary of Billed Consumers";
                int spaceleft = (printer_space - title1.Length) / 2;
                //title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                linePrinter.DoubleHigh = false;

                title1 = "For the month of " + DateTime.Now.ToString("MMM yyyy");
                spaceleft = (printer_space - title1.Length) / 2;
                title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                linePrinter.Write("\n");

                string content = "Meter Reader: ";
                content = content.PadLeft(15) + consumers[0].billedby;
                linePrinter.Write(content + "\n");

                DateTime currentbilldate = DateTime.Parse(consumers[0].billdate);
                string printBilldate = currentbilldate.ToString("MM/dd/yyyy");
                content = "Date: ";
                content = content.PadLeft(15) + printBilldate;
                linePrinter.Write(content + "\n");

                linePrinter.Write(print_dash + "\n");
                //13           7      6
                int pres_space = printer_space > 33 ? 8 : 6;
                int usage_space = printer_space > 33 ? 8 : 6;
                //string strheader = "Acct#-------|--Pres|-Usage----Amnt";
                string strHeader1 = "Acct#";
                strHeader1 = strHeader1.PadRight(11);
                string strHeader2 = "Pres";
                strHeader2 = strHeader2.PadLeft(pres_space);
                string strHeader3 = "Usage";
                strHeader3 = strHeader3.PadLeft(usage_space);
                spaceleft = printer_space - (strHeader1.Length + strHeader2.Length + strHeader3.Length);
                string strHeader4 = "Amnt";
                strHeader4 = strHeader4.PadLeft(spaceleft);

                content = strHeader1 + strHeader2 + strHeader3 + strHeader4;
                linePrinter.Write(content + "\n");
                linePrinter.Write(print_dash + "\n");
                //linePrinter.Write(print_dash + "\n\n");

                string strlinedata;
                decimal dTotalAmt = 0.00m;
                long lTotalCnt = 0;
                foreach (Consumers consumer in consumers)
                {
                    strlinedata = consumer.accountNo.PadRight(11) + consumer.presreading.ToString().PadLeft(pres_space) + consumer.usage.ToString().PadLeft(usage_space) + consumer.total_bill.ToString("#,##0.00").PadLeft(spaceleft);
                    linePrinter.Write(strlinedata + "\n");
                    lTotalCnt = lTotalCnt + 1;
                    dTotalAmt = dTotalAmt + ((decimal)consumer.total_bill);
                }
                linePrinter.Write(print_dash + "\n");
                strlinedata = "No of Consumers   : " + lTotalCnt.ToString();
                linePrinter.Write(strlinedata + "\n");
                strlinedata = "Total Bill Amount : " + dTotalAmt.ToString("#,##0.00");
                linePrinter.Write(strlinedata + "\n");
                //linePrinter.Write(print_dash);

                result = "Done printing.";
            }
            catch (Exception ex)
            {
                result = "DoPrint exception: " + ex.Message + "\n" + imgLogoPath + "\n" + itExsits;
            }
            finally
            {
                if (null != linePrinter)
                {
                    linePrinter.Disconnect();
                    linePrinter.Dispose();
                }
            }


        });

        return result;

    }


    public async Task<string> DoPrintUnbilled_Honeywell(List<Consumers> consumers)
    {
        // Guard: never touch the printer with an empty dataset.
        if (consumers == null || consumers.Count == 0)
            return "No unbilled consumers found. Nothing to print.";

        string result = "";
        //int spacesleft = 0;
        string itExsits = "";
        await Task.Run(() =>
        {

            LinePrinter linePrinter = null;

            try
            {

                ReadProFile();
                //ReadImgToBase64String();

                string bt_address = "bt://" + MyDeviceInfo.currentPrintConfig.bluetooth_address;
                string printer_profile = "PB21"; //MyDeviceInfo.currentPrintConfig.profileconfig;
                int printer_space = 33; //MyDeviceInfo.currentPrintConfig.linespacing;
                string print_dash = new string('-', printer_space);

                linePrinter = new LinePrinter(profiles, printer_profile, bt_address);
                linePrinter.Connect();


                linePrinter.DoubleHigh = true;
                string title1 = "    Summary of Unbilled Consumers";
                int spaceleft = (printer_space - title1.Length) / 2;
                //title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                linePrinter.DoubleHigh = false;

                title1 = "For the month of " + DateTime.Now.ToString("MMM yyyy");
                spaceleft = (printer_space - title1.Length) / 2;
                title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                linePrinter.Write("\n");

                string content = "Meter Reader: ";
                content = content.PadLeft(15) + consumers[0].billedby;
                linePrinter.Write(content + "\n");

                DateTime currentbilldate = DateTime.Parse(consumers[0].billdate);
                string printBilldate = currentbilldate.ToString("MM/dd/yyyy");
                content = "Date: ";
                content = content.PadLeft(15) + printBilldate;
                linePrinter.Write(content + "\n");

                linePrinter.Write(print_dash + "\n");
                //13           7      6
                int pres_space = printer_space > 33 ? 8 : 6;
                int usage_space = printer_space > 33 ? 8 : 6;
                //string strheader = "Acct#-------|--Prev|-Avrge-Arrears";
                string strHeader1 = "Acct#";
                strHeader1 = strHeader1.PadRight(11);
                string strHeader2 = "Prev";
                strHeader2 = strHeader2.PadLeft(pres_space);
                string strHeader3 = "Avrge";
                strHeader3 = strHeader3.PadLeft(usage_space);
                spaceleft = printer_space - (strHeader1.Length + strHeader2.Length + strHeader3.Length);
                string strHeader4 = "Arrears";
                strHeader4 = strHeader4.PadLeft(spaceleft);

                content = strHeader1 + strHeader2 + strHeader3 + strHeader4;
                linePrinter.Write(content + "\n");
                linePrinter.Write(print_dash + "\n");
                //linePrinter.Write(print_dash + "\n\n");

                string strlinedata;
                decimal dTotalAmt = 0.00m;
                long lTotalCnt = 0;
                foreach (Consumers consumer in consumers)
                {
                    strlinedata = consumer.accountNo.PadRight(11) + consumer.prevreading.ToString().PadLeft(pres_space) + consumer.average_usage.ToString().PadLeft(usage_space) + consumer.arrears.ToString("#,##0.00").PadLeft(spaceleft);
                    linePrinter.Write(strlinedata + "\n");
                    lTotalCnt = lTotalCnt + 1;
                    dTotalAmt = dTotalAmt + ((decimal)consumer.arrears);
                }
                linePrinter.Write(print_dash + "\n");
                strlinedata = "No of Consumers   : " + lTotalCnt.ToString();
                linePrinter.Write(strlinedata + "\n");
                strlinedata = "Total Arrears : " + dTotalAmt.ToString("#,##0.00");
                linePrinter.Write(strlinedata + "\n");
                //linePrinter.Write(print_dash);

                result = "Done printing.";
            }
            catch (Exception ex)
            {
                result = "DoPrint exception: " + ex.Message + "\n" + imgLogoPath + "\n" + itExsits;
            }
            finally
            {
                if (null != linePrinter)
                {
                    linePrinter.Disconnect();
                    linePrinter.Dispose();
                }
            }


        });

        return result;

    }

    public async Task<string> DoPrintUnbilled_EscPOS(Consumers consumer, List<Consumers> consumers)
    {
        if (consumer == null)
            return "No consumer record selected.";
        if (consumers == null || consumers.Count == 0)
            return "No unbilled consumers found. Nothing to print.";

        string result = "";

        string strHeaderFileName = MOA.GetClientLogo(consumer.parameters);

        // ===== GLOBAL ALIGNMENT =====
        // 32/48 = cols only;  321/482 = cols + align (1=L, 2=C, 3=R)
        // PaperProfile computes OffsetDots -> EscPOScmdPrint sends GS L / GS W
        var paper = PaperProfile.FromLineSpace(MyDeviceInfo.currentPrintConfig.linespacing);

#if ANDROID
        bool hasPermission = await BluetoothPermissionHelper.EnsureBluetoothPermissionsAsync();
        if (!hasPermission)
            return "Bluetooth permission was not granted. Please allow Bluetooth access in Settings and try again.";

        byte[] logoBytes;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(strHeaderFileName.Trim());
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            logoBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return "Logo load failed: " + ex.GetType().Name + " - " + ex.Message;
        }

        var printer = new EscPOScmdPrint { Profile = paper };
        try
        {
            string macAddress = EscPOScmdPrint.NormalizeMacAddress(MyDeviceInfo.currentPrintConfig.bluetooth_address);
            await printer.ConnectAsync(macAddress);
            await Task.Delay(200);
            await printer.InitializeAsync();
            await printer.AlignAsync(PrintAlign.Left);   // we do alignment ourselves

            // ---- LOGO ----
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            // ---- TITLE ----
            await printer.PrintTextAsync("Summary of", PrintAlign.Center, new PrintStyle(High: true));
            await printer.PrintTextAsync("Unbilled", PrintAlign.Center, new PrintStyle(High: true));
            await printer.PrintTextAsync("Consumers", PrintAlign.Center, new PrintStyle(High: true));

            // ---- MONTH ----
            await printer.PrintTextAsync("For the month of " + DateTime.Now.ToString("MMM yyyy"),
                                         PrintAlign.Center, new PrintStyle(Bold: true));
            await printer.FeedAsync(1);

            // ---- METER READER / DATE ----
            await printer.PrintRowAsync("Meter Reader:", consumer.billedby);

            DateTime currentbilldate = DateTime.Parse(consumers[0].billdate);
            await printer.PrintRowAsync("Date:", currentbilldate.ToString("MM/dd/yyyy"));

            await printer.PrintSeparatorAsync('-');

            // ---- COLUMN HEADERS ----
            // Widths are based on paper.ColsA (32 / 48 …), not the raw linespacing digit.
            int cols = paper.ColsA;
            int pres_space = cols > 33 ? 8 : 6;
            int usage_space = cols > 33 ? 8 : 6;
            int amount_space = Math.Max(7, cols - 11 - pres_space - usage_space);

            string header = "Acct#".PadRight(11)
                          + "Prev".PadLeft(pres_space)
                          + "Avrge".PadLeft(usage_space)
                          + "Arrears".PadLeft(amount_space);
            await printer.WriteLineAsync(header);
            await printer.PrintSeparatorAsync('-');

            // ---- DETAIL ROWS ----
            decimal dTotalAmt = 0.00m;
            long lTotalCnt = 0;
            foreach (Consumers c in consumers)
            {
                string line = c.accountNo.PadRight(11)
                            + c.prevreading.ToString().PadLeft(pres_space)
                            + c.average_usage.ToString().PadLeft(usage_space)
                            + c.arrears.ToString("#,##0.00").PadLeft(amount_space);
                await printer.WriteLineAsync(line);
                lTotalCnt++;
                dTotalAmt += (decimal)c.arrears;
            }

            await printer.PrintSeparatorAsync('-');
            await printer.PrintRowAsync("No of Consumers", lTotalCnt.ToString());
            await printer.PrintRowAsync("Total Arrears", dTotalAmt.ToString("#,##0.00"));
            await printer.FeedAsync(3);

            await printer.FlushAsync();
            result = "Done printing.";
        }
        catch (Exception ex)
        {
            result = "Print failed: " + ex.GetType().Name + " - " + ex.Message;
        }
        finally
        {
            await Task.Delay(1000 + printer.RasterRows);
            printer.Disconnect();
        }
#else
    result = "Printing is only implemented for Android.";
#endif

        return result;
    }

    public async Task<string> DoPrintBilledSummary_EscPOS(Consumers consumer, List<Consumers> consumers)
    {
        if (consumer == null)
            return "No consumer record selected.";
        if (consumers == null || consumers.Count == 0)
            return "No billed consumers found. Nothing to print.";

        string result = "";

        string strHeaderFileName = MOA.GetClientLogo(consumer.parameters);

        // ===== GLOBAL ALIGNMENT =====
        var paper = PaperProfile.FromLineSpace(MyDeviceInfo.currentPrintConfig.linespacing);

#if ANDROID
        bool hasPermission = await BluetoothPermissionHelper.EnsureBluetoothPermissionsAsync();
        if (!hasPermission)
            return "Bluetooth permission was not granted. Please allow Bluetooth access in Settings and try again.";

        byte[] logoBytes;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(strHeaderFileName.Trim());
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            logoBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return "Logo load failed: " + ex.GetType().Name + " - " + ex.Message;
        }

        var printer = new EscPOScmdPrint { Profile = paper };
        try
        {
            string macAddress = EscPOScmdPrint.NormalizeMacAddress(MyDeviceInfo.currentPrintConfig.bluetooth_address);
            await printer.ConnectAsync(macAddress);
            await Task.Delay(200);
            await printer.InitializeAsync();
            await printer.AlignAsync(PrintAlign.Left);

            // ---- LOGO ----
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            // ---- TITLE ----
            await printer.PrintTextAsync("Summary of", PrintAlign.Center, new PrintStyle(High: true));
            await printer.PrintTextAsync("Billed Consumers", PrintAlign.Center, new PrintStyle(High: true));

            // ---- MONTH ----
            await printer.PrintTextAsync("For the month of " + DateTime.Now.ToString("MMM yyyy"),
                                         PrintAlign.Center, new PrintStyle(Bold: true));
            await printer.FeedAsync(1);

            // ---- METER READER / DATE ----
            await printer.PrintRowAsync("Meter Reader:", consumer.billedby);

            DateTime currentbilldate = DateTime.Parse(consumer.billdate);
            await printer.PrintRowAsync("Date:", currentbilldate.ToString("MM/dd/yyyy"));

            await printer.PrintSeparatorAsync('-');

            // ---- COLUMN HEADERS ----
            int cols = paper.ColsA;
            int pres_space = cols > 33 ? 8 : 6;
            int usage_space = cols > 33 ? 8 : 6;
            int amount_space = Math.Max(7, cols - 11 - pres_space - usage_space);

            string header = "Acct#".PadRight(11)
                          + "Pres".PadLeft(pres_space)
                          + "Usage".PadLeft(usage_space)
                          + "Amnt".PadLeft(amount_space);
            await printer.WriteLineAsync(header);
            await printer.PrintSeparatorAsync('-');

            // ---- DETAIL ROWS ----
            decimal dTotalAmt = 0.00m;
            long lTotalCnt = 0;
            foreach (Consumers c in consumers)
            {
                string line = c.accountNo.PadRight(11)
                            + c.presreading.ToString().PadLeft(pres_space)
                            + c.usage.ToString().PadLeft(usage_space)
                            + c.total_bill.ToString("#,##0.00").PadLeft(amount_space);
                await printer.WriteLineAsync(line);
                lTotalCnt++;
                dTotalAmt += (decimal)c.total_bill;
            }

            await printer.PrintSeparatorAsync('-');
            await printer.PrintRowAsync("No of Consumers", lTotalCnt.ToString());
            await printer.PrintRowAsync("Total Bill Amount", dTotalAmt.ToString("#,##0.00"));
            await printer.FeedAsync(3);

            await printer.FlushAsync();
            result = "Done printing.";
        }
        catch (Exception ex)
        {
            result = "Print failed: " + ex.GetType().Name + " - " + ex.Message;
        }
        finally
        {
            await Task.Delay(1000 + printer.RasterRows);
            printer.Disconnect();
        }
#else
    result = "Printing is only implemented for Android.";
#endif

        return result;
    }

    public async Task<string> DoPrintReportConsumerWithRemarks_Honeywell(List<Consumers> consumers)
    {
        // Guard: never touch the printer with an empty dataset.
        if (consumers == null || consumers.Count == 0)
            return "No consumer records with remarks found. Nothing to print.";

        string result = "";
        string itExsits = "";
        await Task.Run(() =>
        {

            LinePrinter linePrinter = null;

            try
            {

                ReadProFile();
                //ReadImgToBase64String();

                string bt_address = "bt://" + MyDeviceInfo.currentPrintConfig.bluetooth_address;
                string printer_profile = "PB21"; //MyDeviceInfo.currentPrintConfig.profileconfig;
                int printer_space = 33; //MyDeviceInfo.currentPrintConfig.linespacing;

                linePrinter = new LinePrinter(profiles, printer_profile, bt_address);
                linePrinter.Connect();

                string print_dash = new string('-', printer_space);

                linePrinter.DoubleHigh = true;
                string title1 = "  Billed Consumers with Remarks";
                int spaceleft = (printer_space - title1.Length) / 2;
                //title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                linePrinter.DoubleHigh = false;

                title1 = "For the month of " + DateTime.Now.ToString("MMM yyyy");
                spaceleft = (printer_space - title1.Length) / 2;
                title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                linePrinter.Write("\n");

                string content = "Meter Reader: ";
                content = content.PadLeft(15) + consumers[0].billedby;
                linePrinter.Write(content + "\n");

                DateTime currentbilldate = DateTime.Parse(consumers[0].billdate);
                string printBilldate = currentbilldate.ToString("MM/dd/yyyy");
                content = "Date: ";
                content = content.PadLeft(15) + printBilldate;
                linePrinter.Write(content + "\n");

                linePrinter.Write(print_dash + "\n");
                string strHeader1 = "Acct#";
                strHeader1 = strHeader1.PadRight(11);
                string strHeader2 = "Remarks";

                content = strHeader1 + strHeader2;
                linePrinter.Write(content + "\n");
                linePrinter.Write(print_dash + "\n");
                //linePrinter.Write(print_dash + "\n\n");

                string strlinedata;
                foreach (Consumers consumer in consumers)
                {
                    strlinedata = consumer.accountNo.PadRight(11) + consumer.remarks;
                    linePrinter.Write(strlinedata + "\n");
                }
                linePrinter.Write(print_dash + "\n\n\n");
                //linePrinter.Write(print_dash);

                result = "Done printing.";
            }
            catch (Exception ex)
            {
                result = "DoPrint exception: " + ex.Message + "\n" + imgLogoPath + "\n" + itExsits;
            }
            finally
            {
                if (null != linePrinter)
                {
                    linePrinter.Disconnect();
                    linePrinter.Dispose();
                }
            }


        });

        return result;

    }

    public async Task<string> DoPrintReportConsumerWithRemarks_EscPOS(Consumers consumer, List<Consumers> consumers)
    {
        if (consumer == null)
            return "No consumer record selected.";
        if (consumers == null || consumers.Count == 0)
            return "No consumer records with remarks found. Nothing to print.";

        string result = "";

        string strHeaderFileName = MOA.GetClientLogo(consumer.parameters);

        // ===== GLOBAL ALIGNMENT =====
        var paper = PaperProfile.FromLineSpace(MyDeviceInfo.currentPrintConfig.linespacing);

#if ANDROID
        bool hasPermission = await BluetoothPermissionHelper.EnsureBluetoothPermissionsAsync();
        if (!hasPermission)
            return "Bluetooth permission was not granted. Please allow Bluetooth access in Settings and try again.";

        byte[] logoBytes;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(strHeaderFileName.Trim());
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            logoBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return "Logo load failed: " + ex.GetType().Name + " - " + ex.Message;
        }

        var printer = new EscPOScmdPrint { Profile = paper };
        try
        {
            string macAddress = EscPOScmdPrint.NormalizeMacAddress(MyDeviceInfo.currentPrintConfig.bluetooth_address);
            await printer.ConnectAsync(macAddress);
            await Task.Delay(200);
            await printer.InitializeAsync();
            await printer.AlignAsync(PrintAlign.Left);

            // ---- LOGO ----
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            // ---- TITLE ----
            await printer.PrintTextAsync("Billed Consumers", PrintAlign.Center, new PrintStyle(High: true));
            await printer.PrintTextAsync("with Remarks", PrintAlign.Center, new PrintStyle(High: true));

            // ---- MONTH ----
            await printer.PrintTextAsync("For the month of " + DateTime.Now.ToString("MMM yyyy"),
                                         PrintAlign.Center, new PrintStyle(Bold: true));
            await printer.FeedAsync(1);

            // ---- METER READER / DATE ----
            await printer.PrintRowAsync("Meter Reader:", consumer.billedby);

            DateTime currentbilldate = DateTime.Parse(consumers[0].billdate);
            await printer.PrintRowAsync("Date:", currentbilldate.ToString("MM/dd/yyyy"));

            await printer.PrintSeparatorAsync('-');

            // ---- COLUMN HEADERS ----
            // Remarks column has no max-length, so we just left-pad the acct# and append the remarks.
            string header = "Acct#".PadRight(11) + "Remarks";
            await printer.WriteLineAsync(header);
            await printer.PrintSeparatorAsync('-');

            // ---- DETAIL ROWS ----
            foreach (Consumers c in consumers)
            {
                // Write raw so any wrapping/truncation is left to the printer hardware, like the original.
                await printer.WriteLineAsync(c.accountNo.PadRight(11) + (c.remarks ?? ""));
            }

            // ---- CLOSING + PAPER FEED ----
            await printer.PrintSeparatorAsync('-');
            await printer.FeedAsync(3);

            await printer.FlushAsync();
            result = "Done printing.";
        }
        catch (Exception ex)
        {
            result = "Print failed: " + ex.GetType().Name + " - " + ex.Message;
        }
        finally
        {
            await Task.Delay(1000 + printer.RasterRows);
            printer.Disconnect();
        }
#else
    result = "Printing is only implemented for Android.";
#endif

        return result;
    }
    private async void ReadProFile()
    {
        string sourcefilename = "Datafiles/printer_profiles.JSON";
        //Stream stream = assembly.GetManifestResourceStream(resourcePrefix + fileName);
        using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        profiles = "";
        using (var reader = new System.IO.StreamReader(stream))
        {
            profiles = reader.ReadToEnd();
        }

    }

    //private async void ReadImgToBase64String()
    //{
    //    byte[] imageBytes;
    //    string sourcefilename = "Datafiles/msl_print.bmp";

    //    using (Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename))
    //    {
    //        using (MemoryStream ms = new MemoryStream())
    //        {
    //            await stream.CopyToAsync(ms);
    //            imageBytes = ms.ToArray();
    //        }
    //    }
    //    logoBase64ImgStr = Convert.ToBase64String(imageBytes);

    //}

    private async Task<string> ReadImgToBase64StringAsync()
    {
        const string sourceFilename = "Datafiles/logo_mls.bmp";

        await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourceFilename);
        using MemoryStream ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        return Convert.ToBase64String(ms.ToArray());
    }
}