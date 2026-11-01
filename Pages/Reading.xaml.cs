using Honeywell.AIDC.CrossPlatform.Print;
using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using SQLite;
using System.Globalization;
//using System.Collections.Generic;
//using System.IO;
//using System.Threading.Tasks;
//using SkiaSharp;
#if ANDROID
using eMRBCS.Platforms.Android;
#endif

namespace eMRBCS.Pages;

public partial class Reading : ContentPage
{

    long selected_seqno;
    int selected_idno;

    private static SQLiteConnection? Database;
    string dbname = database_info.dbName;
    private static double Fld2Dob(double value) => Math.Round(value, 2);

    private void InitializeDB()
    {
        if (Database == null)
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, dbname);
            Database = new SQLiteConnection(dbPath);
        }
    }


    List<Remarks>? remarks_items;
    DBService? dbService;

    float _currentUsage = 0;
    float _currentIsLessUsage = 0;
    float _currentBillDiscount = 0;
    float _currentBillAmount = 0;
    float _currentPenalty = 0;

    float usage = 0;

    float billratePcaRate = 0;
    int billrateGracePeriodDue = 0;
    float billrateSewerage = 0;
    float billrateBrate = 0;
    int billrateCommoditiesTo = 0;

    int selected_field = 0; //0 - Pres Reading, 1 - Acct No, 2 - Meter No

    bool isFilterChange = false;
    bool isRecompute = false;
    bool isMinus = false;
    bool showOptions = false;

    bool isFlatRate = false;
    bool isSewerage = false;
    bool isSenior = false;
    bool isZeoMin = false;

    bool isPrintOn = false;

    string profiles;

    int prevIndex = 0;

    private static string logoBase64ImgStr = null;
    private float _averageCons;

    public Reading()
    {
        InitializeComponent();
        InitializePickers();
        if (pickerBillingFilter.SelectedIndex == 0)
        {
            btnPrint.IsEnabled = false;
        }
        else
        {
            btnPrint.IsEnabled = true;
        }
        chkPrintOn.IsChecked = false;

        ScreenAdjustment();

    }

    private async void ScreenAdjustment()
    {
        if (MyDeviceInfo.device_heightpix > 2000)
        {


            txtConsumerAcctNo.FontSize = 22;

            int defaultsize = 40;
            btnStartRecord.HeightRequest = defaultsize;
            btnStartRecord.WidthRequest = defaultsize;

            btnPrevRecord.HeightRequest = defaultsize;
            btnPrevRecord.WidthRequest = defaultsize;

            btnNextRecord.HeightRequest = defaultsize;
            btnNextRecord.WidthRequest = defaultsize;

            btnLastRecord.HeightRequest = defaultsize;
            btnLastRecord.WidthRequest = defaultsize;

            //scrollDetails.HeightRequest = 430;

            int defaultfont = 14;

            lblName.Margin = new Thickness(10);
            txtConsumerName.Margin = new Thickness(10);
            txtConsumerAddress.Margin = new Thickness(10, 5, 10, 0);

            txtConsumerMeterNo.FontSize = 18;
            lbladdress.FontSize = defaultfont;

            lblpresreading.FontSize = defaultfont;
            txtConsumerPresReading.FontSize = 22;

            lblprevreading.FontSize = defaultfont;
            lblprevreading.Margin = new Thickness(20, 10, 0, 0);
            txtConsumerPrevReading.FontSize = 20;

            lblusage.FontSize = defaultfont;
            lblusage.Margin = new Thickness(10, 10, 0, 0);
            txtConsumerUsage.FontSize = 20;

            lblcurrentbill.FontSize = defaultfont;
            lblcurrentbill.Margin = new Thickness(0, 10, 10, 0);
            txtConsumerCurrentBill.FontSize = 20;

            lblArrears.FontSize = defaultfont;
            lblArrears.Margin = new Thickness(55, 10, 0, 0);

            txtConsumerArrears.FontSize = 20;

            lblOthers.FontSize = defaultfont;
            lblOthers.Margin = new Thickness(10, 10, 0, 0);
            txtConsumerOthers.FontSize = 20;

            lbltotalbill.FontSize = defaultfont;
            txtConsumerTotalBill.FontSize = 24;
        }
        else if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {

            btnok.FontSize = 16;

            txtConsumerAcctNo.FontSize = 16;

            foreach (var child in gridButtons.Children)
            {
                if (child is Button btn)
                {
                    btn.WidthRequest = 60;
                }
            }


        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(100);
#if ANDROID
        MessagingCenter.Subscribe<object, Android.Views.Keycode>(this, "KeyDown", (sender, keyCode) =>
        {
            //DisplayAlertAsync("Print Notice", $"Key Pressed: {keyCode}", "Ok");
            //switch (keyCode.ToString())
            //{
            //    case "VideoApp5":
            //        OnPresReadingTapped(txtConsumerPresReading, EventArgs.Empty);                    
            //        break;
            //    case "Num01":
            //        txtCustomEntry.Focus();
            //        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "1" : txtCustomEntry.Text + "1";
            //        break;
            //    case "Num02":
            //        DisplayAlertAsync("Print Notice", $"Key Pressed: {keyCode.ToString()}", "Ok");
            //        //OnClickB2(btnNumber2, EventArgs.Empty);
            //        break;
            //}
            //btnok.Focus();

            if (gridNumericPad.IsVisible)
            {
                switch (keyCode.ToString())
                {
                    case "Num1" or "F1":
                        OnClickB1(btnNumber1, EventArgs.Empty);
                        break;
                    case "Num2" or "F2":
                        OnClickB2(btnNumber2, EventArgs.Empty);
                        break;
                    case "Num3" or "F3":
                        OnClickB3(btnNumber3, EventArgs.Empty);
                        break;
                    case "Num4" or "F4":
                        OnClickB4(btnNumber4, EventArgs.Empty);
                        break;
                    case "Num5" or "F5":
                        OnClickB5(btnNumber5, EventArgs.Empty);
                        break;
                    case "Num6" or "F6":
                        OnClickB6(btnNumber6, EventArgs.Empty);
                        break;
                    case "Num7" or "F7":
                        OnClickB7(btnNumber7, EventArgs.Empty);
                        break;
                    case "Num8" or "F8":
                        OnClickB8(btnNumber8, EventArgs.Empty);
                        break;
                    case "Num9" or "F9":
                        OnClickB9(btnNumber9, EventArgs.Empty);
                        break;
                    case "Num0" or "F10":
                        OnClickB0(btnNumber0, EventArgs.Empty);
                        break;
                    case "Del":
                        OnClickBackSpace(btnDel, EventArgs.Empty);
                        break;
                    case "Enter":
                        OnClickOk(btnok, EventArgs.Empty);
                        break;
                }
                btnok.Focus();
            }
            else
            {
                switch (keyCode.ToString())
                {
                    case "VideoApp5":
                        OnPresReadingTapped(txtConsumerPresReading, EventArgs.Empty);
                        break;
                    case "VideoApp6":
                        OnMeterNoTapped(txtConsumerMeterNo, EventArgs.Empty);
                        break;
                    case "DpadLeft":
                        OnPrevRecordTapped(btnPrevRecord, EventArgs.Empty);
                        break;
                    case "DpadRight":
                        OnNextRecordTapped(btnNextRecord, EventArgs.Empty);
                        break;
                    case "DpadUp":
                        OnFirstRecordTapped(btnStartRecord, EventArgs.Empty);
                        break;
                    case "DpadDown":
                        OnLastRecordTapped(btnLastRecord, EventArgs.Empty);
                        break;
                    case "Enter":
                        OnSaveReading(btnSave, EventArgs.Empty);
                        break;
                }
                //DisplayAlertAsync("Print Notice", $"Key Pressed: {keyCode.ToString()}", "Ok");
            }


        });
#endif
        OnConsumerLoad(ConsumerRecords.acctNo);
        btnSave.Focus();
    }


    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MessagingCenter.Unsubscribe<object, string>(this, "KeyDown");
    }

    private async void InitializePickers()
    {
        pickerBillingFilter.ItemsSource = new List<string>
        {
            "Unbilled Consumers",
            "Billed Consumers",
            "Collected Consumers"
        };

        pickerBillingFilter.SelectedIndex = ConsumerRecords.consumer_selectedtype;

        List<string> remarksList = new List<string>();

        remarks_items = new List<Remarks>();
        dbService = new DBService();
        remarks_items = await dbService.GetAllRemarks();
        if (remarks_items.Count > 0)
        {
            remarksList.Add("Normal Reading");
            foreach (var remark in remarks_items)
            {
                remarksList.Add(remark.description);
            }

            pickerRemarks.ItemsSource = remarksList;
        }

    }

    private async void OnConsumerLoad(string parameter)
    {
        // ConsumerRecords.acctNo = parameter;
        var result = ConsumerRecords.lstconsumers.Where(c => c.accountNo == parameter).FirstOrDefault();
        if (result != null)
        {
            OnLoadConsumerDetails(result);
        }
        else
        {
            await DisplayAlertAsync("Warning!", "Account Number " + parameter + " not found. Please try again.", "Ok");
        }
    }

    private async void OnFilterNameSearch(string parameter)
    {
        //ConsumerRecords.acctNo = parameter;
        Consumers? result = new Consumers();
        string searchName = "Account No: ";
        if (selected_field == 1)
        {
            result = ConsumerRecords.lstconsumers.Where(c => c.status == pickerBillingFilter.SelectedIndex && c.accountNo.Contains(parameter, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        }
        else
        {
            searchName = "Meter No: ";
            result = ConsumerRecords.lstconsumers.Where(c => c.status == pickerBillingFilter.SelectedIndex && c.meterno.Contains(parameter, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }

        if (result != null)
        {
            OnLoadConsumerDetails(result);

        }
        else
        {
            await DisplayAlertAsync("Warning!", searchName + "that contains " + parameter + " not found. Please try again.", "Ok");
        }
        gridDetails.IsVisible = true;

    }

    private async void OnNextRecordTapped(object sender, EventArgs e)
    {
        OnNextRecord(pickerBillingFilter.SelectedIndex);
    }

    private async void OnPrevRecordTapped(object sender, EventArgs e)
    {
        OnPrevRecord(pickerBillingFilter.SelectedIndex);
    }



    private async void OnLastRecordTapped(object sender, EventArgs e)
    {
        OnFirstLastRecord(0, pickerBillingFilter.SelectedIndex);
    }

    private async void OnNextRecord(int isBilled)
    {
        //ConsumerRecords.acctNo = parameter;
        var result = ConsumerRecords.lstconsumers.Where(c => c.accountID > selected_idno && c.status == isBilled).OrderBy(c => c.accountID).FirstOrDefault();

        if (result != null)
        {
            OnLoadConsumerDetails(result);


        }
        else
        {
            await DisplayAlertAsync("Notice", "No record(s) available to display.", "Ok");
        }
        //gridDetails.IsVisible = true;

    }

    private async void OnPrevRecord(int isBilled)
    {
        //ConsumerRecords.acctNo = parameter;
        var result = ConsumerRecords.lstconsumers.Where(c => c.accountID < selected_idno && c.status == isBilled).OrderBy(c => c.accountID).LastOrDefault();
        OnLoadConsumerDetails(result);
        if (result != null)
        {
            OnLoadConsumerDetails(result);


        }
        else
        {
            await DisplayAlertAsync("Notice", "No record(s) available to display.", "Ok");
        }
        //gridDetails.IsVisible = true;

    }

    private async void OnFirstLastRecord(int isFirst, int isBilled)
    {
        Consumers result = new Consumers();

        if (isFirst == 1)
        {
            result = ConsumerRecords.lstconsumers.Where(c => c.status == isBilled).OrderBy(c => c.accountID).FirstOrDefault();
        }
        else
        {
            result = ConsumerRecords.lstconsumers.Where(c => c.status == isBilled).OrderBy(c => c.accountID).LastOrDefault();

        }

        if (result != null)
        {
            isFilterChange = false;
            OnLoadConsumerDetails(result);

        }
        else
        {
            if (isFilterChange)
            {
                lblRecordNo.Text = "No: 0";
                selected_idno = 0;
                selected_seqno = 0;
                txtConsumerAcctNo.Text = "";
                txtConsumerName.Text = "";
                txtConsumerMeterNo.Text = "";
                txtConsumerAddress.Text = "";
                txtConsumerPrevReading.Text = "0";
                txtConsumerArrears.Text = "0";
                txtConsumerUsage.Text = "0";
                txtConsumerCurrentBill.Text = "0";
                txtConsumerPresReading.Text = "0";
                txtConsumerTotalBill.Text = "0.00";
                pickerRemarks.SelectedIndex = -1;
                //txtRemarks.Text = "Normal Reading";
                isFilterChange = false;
            }
            await DisplayAlertAsync("Notice", "No record(s) available to display.", "Ok");
        }
        //gridDetails.IsVisible = true;

    }

    private void OnLastRecord()
    {
        //ConsumerRecords.acctNo = parameter;
        var result = ConsumerRecords.lstconsumers.Where(c => c.presreading == 0).OrderBy(c => c.accountID).FirstOrDefault();
        if (result != null)
        {
            OnLoadConsumerDetails(result);

        }
        gridDetails.IsVisible = true;

    }

    private async void OnLoadConsumerDetails(Consumers consumer)
    {
        try
        {
            if (consumer != null)
            {
                lblRecordNo.Text = "No: " + consumer.accountID.ToString();
                selected_idno = consumer.accountID;
                selected_seqno = consumer.seqno;
                txtConsumerAcctNo.Text = consumer.accountNo;
                txtConsumerName.Text = consumer.consumer_name;
                txtConsumerMeterNo.Text = consumer.meterno;
                _averageCons = consumer.average_usage;

                //bool IsSJWD = consumer.parameters.Contains("[SJWD]") ? true : false;
                //bool IsMVWD = consumer.parameters.Contains("[MVWD]") ? true : false;
                //if (IsMVWD || IsSJWD)
                //{
                string strAddress = consumer.address.Trim();
                //string printAddress = strAddress.Length >= 25 ? strAddress[..25] : strAddress;
                string printAddress = strAddress; //no need to substring address can grow
                if (consumer.consumer_status.ToString().Trim().ToUpper() == "A")
                {
                    txtConsumerAddress.Text = MOA.Left(printAddress, 42) + "\nSTATUS: Active\nAVERAGE: " + consumer.average_usage.ToString();
                }
                else
                {
                    txtConsumerAddress.Text = MOA.Left(printAddress, 42) + "\nSTATUS: Inactive\nAVERAGE: " + consumer.average_usage.ToString();
                }

                //}                    
                //else
                //{
                //    txtConsumerAddress.Text = consumer.address.Trim();
                //}

                txtConsumerPrevReading.Text = consumer.prevreading.ToString();
                txtConsumerUsage.Text = consumer.usage.ToString();
                txtConsumerCurrentBill.Text = consumer.currentbill.ToString();

                float arrears = consumer.arrears;
                if (arrears < 0)
                {
                    lblArrears.Text = "Advance:";
                    lblArrears.Margin = new Thickness(50, 10, 0, 0);
                }
                else
                {
                    lblArrears.Text = "Arrears:";
                    lblArrears.Margin = new Thickness(55, 10, 0, 0);
                }
                txtConsumerArrears.Text = consumer.arrears.ToString("0.00");
                txtConsumerOthers.Text = consumer.othercharges.ToString("0.00");
                float presReading = consumer.presreading;
                float totalbill = consumer.total_bill;
                txtConsumerPresReading.Text = "0";
                txtConsumerTotalBill.Text = "0.00";
                pickerRemarks.SelectedIndex = -1;
                if (presReading > 0)
                {
                    txtConsumerPresReading.Text = presReading.ToString();
                }
                if (totalbill > 0)
                {
                    txtConsumerTotalBill.Text = totalbill.ToString();
                }
                _currentIsLessUsage = consumer.less_usage;

            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Error loading record.\nError: " + ex.Message, "Ok");
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
    private async void OnCloseDetails(object sender, EventArgs e)
    {
        //await Navigation.PopAsync();
        //restruct list before going back to consumer list
        ConsumerRecords.AutoID_UIndex = new List<string>();
        ConsumerRecords.AutoID_BIndex = new List<string>();

        int ctrx = 0;
        if (ConsumerRecords.gridConsumerUnbilledCollection.Count > 0)
        {
            foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerUnbilledCollection)
            {
                string gridautoID = _gridConsumer.AutomationId;
                ConsumerRecords.AutoID_UIndex.Add(gridautoID + "," + ctrx);
                ctrx++;

            }
        }

        ctrx = 0;
        if (ConsumerRecords.gridConsumerBilledCollection.Count > 0)
        {
            foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerBilledCollection)
            {
                string gridautoID = _gridConsumer.AutomationId;
                ConsumerRecords.AutoID_BIndex.Add(gridautoID + "," + ctrx);
                ctrx++;
            }
        }

        await Navigation.PushAsync(new ReadingConsumers());
        //await Navigation.PopAsync();
    }



    private async void OnSaveDetails_new(object sender, EventArgs e)
    {
        if (txtConsumerAcctNo.Text == string.Empty)
        {
            return;
        }
        bool confirm = await DisplayAlertAsync("Confirm", "Are you sure you want to save?", "Yes", "No");
        if (!confirm)
        {
            return;
        }


        Grid grid_child = new Grid();
        Grid grid_parent = new Grid();
        //int selectedindex = pickerRemarks.SelectedIndex;
        string selectedRemarks = "";
        //if (selectedindex != -1)
        //{
        //    selectedRemarks = pickerRemarks.Items[selectedindex];
        //}
        selectedRemarks = txtRemarks.Text;


        Consumers? consumer = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();

        if (consumer == null)
        {
            return;
        }

        float presReading = ParseFloat(txtConsumerPresReading.Text);

        consumer.presreading = presReading;
        consumer.remarks = selectedRemarks;
        dbService = new DBService();
        bool isSaveSuccess = await dbService.ConsumerUpdateItemToDatabase(consumer);
        if (isSaveSuccess)
        {

            if (consumer != null)
            {

                var _gridBoxU = ConsumerRecords.gridConsumerUnbilledCollection;

                int batchctr = consumer.accountID > 10 ? 0 : consumer.accountID / 10;
                //batchctr = batchctr;
                string gridAutoID = "UGrid_" + batchctr;

                string gridInnerAutoID = $"UInnerGrid_{selected_seqno}";


                //Look for the grid and update
                foreach (Grid _gridConsumer in _gridBoxU)
                {
                    if (_gridConsumer.AutomationId == gridAutoID)
                    {
                        grid_parent = _gridConsumer;
                        foreach (Grid _gridInner in _gridConsumer.Children)
                        {
                            if (_gridInner.AutomationId == gridInnerAutoID)
                            {
                                //_gridInner.IsVisible = false;
                                grid_child = _gridInner;
                                break;
                            }
                        }
                        break;
                    }
                }

                //Add it to Billed Collections
                int maxbatchbilled = ConsumerRecords.gridBilled_rowindex;
                if (maxbatchbilled > 0)
                {
                    maxbatchbilled++;
                }

                grid_parent.Children.Remove(grid_child);

                List<Grid> _gridBoxB = new List<Grid>();

                if (ConsumerRecords.gridConsumerBilledCollection != null)
                {
                    _gridBoxB = ConsumerRecords.gridConsumerBilledCollection;
                }


                if (_gridBoxB == null)
                {
                    Grid tmpgridConsumerBilled = new Grid
                    {
                        AutomationId = $"BGrid_{maxbatchbilled}",
                        RowDefinitions =
                            {
                                new RowDefinition { Height = GridLength.Auto }
                            },
                        ColumnDefinitions =
                            {
                                new ColumnDefinition { Width = GridLength.Star }
                            }

                    };
                    tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    tmpgridConsumerBilled.Add(grid_child, 0, maxbatchbilled);
                    _gridBoxB.Add(tmpgridConsumerBilled);
                    ConsumerRecords.gridConsumerBilledCollection = _gridBoxB;
                }
                else
                {
                    if (_gridBoxB.Count == 0)
                    {
                        Grid tmpgridConsumerBilled = new Grid
                        {
                            AutomationId = $"BGrid_{maxbatchbilled}",
                            RowDefinitions =
                            {
                                new RowDefinition { Height = GridLength.Auto }
                            },
                            ColumnDefinitions =
                            {
                                new ColumnDefinition { Width = GridLength.Star }
                            }

                        };
                        tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        tmpgridConsumerBilled.Add(grid_child, 0, maxbatchbilled);
                        _gridBoxB.Add(tmpgridConsumerBilled);
                    }
                    else
                    {
                        foreach (Grid _gridchild in _gridBoxB)
                        {
                            if (_gridchild.AutomationId == "BGrid_" + maxbatchbilled)
                            {
                                int maxrow = _gridchild.RowDefinitions.Count;
                                _gridchild.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                _gridchild.Add(grid_child, 0, maxrow + 1);
                                break;
                            }
                        }
                    }

                }


            }
            //lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.lstconsumers.Count.ToString();
            int selectedItem = pickerBillingFilter.SelectedIndex;
            if (selectedItem == 0)
            {

                lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else if (selectedItem == 1)
            {
                lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else
            {

                lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
            }
            //await DisplayAlertAsync("Notice", "Reading Saved!", "Ok");

            if (MyDeviceInfo.currentPrintConfig != null)
            {
                bool toPrint = false;
                if (isPrintOn)
                {
                    toPrint = true;
                    //string output = await DoPrintv2(consumer);
                    //await DisplayAlertAsync("Print Notice", output, "Ok");
                }
                else
                {
                    toPrint = await DisplayAlertAsync("Confirm", "Print Transaction?", "Yes", "No");
                }

                if (toPrint)
                {
                    string output = await DoPrintBill(consumer);
                    await DisplayAlertAsync("Print Notice", output, "Ok");
                }


            }

            //Move to next seq no
            OnNextRecord(pickerBillingFilter.SelectedIndex);

        }
        else
        {
            await DisplayAlertAsync("Error", "Failed to saved ", "Ok");
        }
    }

    private async void OnSaveDetails(object sender, EventArgs e)
    {
        if (txtConsumerAcctNo.Text == string.Empty)
        {
            return;
        }
        bool confirm = await DisplayAlertAsync("Confirm", "Are you sure you want to save?", "Yes", "No");
        if (!confirm)
        {
            return;
        }

        AbsoluteLayout absoluteLayout_remove = new AbsoluteLayout();
        Grid grid_remove = new Grid();
        var uframe_remove = new Frame();
        var lframe_remove = new Frame();
        int selectedindex = pickerRemarks.SelectedIndex;
        string selectedRemarks = "";
        if (selectedindex != -1)
        {
            selectedRemarks = pickerRemarks.Items[selectedindex];
        }

        Consumers? consumer = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();

        if (consumer == null)
        {
            return;
        }

        float presReading = ParseFloat(txtConsumerPresReading.Text);

        consumer.presreading = presReading;
        consumer.remarks = selectedRemarks;
        dbService = new DBService();
        bool isSaveSuccess = await dbService.ConsumerUpdateItemToDatabase(consumer);
        if (isSaveSuccess)
        {

            if (consumer != null)
            {

                var _gridBoxU = ConsumerRecords.gridConsumerUnbilledCollection;

                //Look for the grid and update
                foreach (Grid _gridConsumer in _gridBoxU)
                {
                    foreach (var child in _gridConsumer.Children)
                    {
                        if (child is AbsoluteLayout tmpAbsolutelayout)
                        {
                            if (tmpAbsolutelayout.AutomationId == "AbsoluteLayout_" + selected_seqno)
                            {
                                foreach (var child_labels in tmpAbsolutelayout.Children)
                                {
                                    if (child_labels is Label tmpLabel)
                                    {
                                        if (tmpLabel.Text == txtConsumerAcctNo.Text)
                                        {
                                            //Check again the grid
                                            bool isLabelUpdated = false;
                                            bool isFrameColorUpdated = false;
                                            foreach (var child_correctGrid in tmpAbsolutelayout.Children)
                                            {

                                                if (child_correctGrid is Label lblobject_correctGrid)
                                                {
                                                    if (lblobject_correctGrid.Text == "U")
                                                    {
                                                        lblobject_correctGrid.Text = "B";
                                                        isLabelUpdated = true;
                                                    }
                                                }
                                                else if (child_correctGrid is Frame frameObject_correctGrid)
                                                {
                                                    if (frameObject_correctGrid.BackgroundColor == Colors.Red)
                                                    {
                                                        frameObject_correctGrid.BackgroundColor = Colors.Blue;
                                                        isFrameColorUpdated = true;
                                                    }
                                                }
                                                if (isFrameColorUpdated && isLabelUpdated)
                                                {

                                                    absoluteLayout_remove = (AbsoluteLayout)child;
                                                    grid_remove = _gridConsumer;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                //Get the Upper and Lower Frame
                foreach (var child in grid_remove.Children)
                {
                    if (child is Frame sframe)
                    {
                        if (sframe.AutomationId == "Lframe_" + selected_seqno)
                        {
                            lframe_remove = sframe;
                        }
                        if (sframe.AutomationId == "Uframe_" + selected_seqno)
                        {
                            uframe_remove = sframe;
                        }
                    }
                }
                //Add it to Billed Collections
                int maxbatchbilled = ConsumerRecords.gridBilled_rowindex;
                if (maxbatchbilled > 0)
                {
                    maxbatchbilled++;
                }
                //ConsumerRecords.gridConsumerBilledRecords.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                //ConsumerRecords.gridConsumerBilledRecords.Add(lframe_remove, maxbatchbilled);
                //ConsumerRecords.gridConsumerBilledRecords.Add(uframe_remove, maxbatchbilled);
                //ConsumerRecords.gridConsumerBilledRecords.Add(absoluteLayout_remove, maxbatchbilled);

                grid_remove.Children.Remove(absoluteLayout_remove);
                grid_remove.Children.Remove(uframe_remove);
                grid_remove.Children.Remove(lframe_remove);

                List<Grid> _gridBoxB = new List<Grid>();

                if (ConsumerRecords.gridConsumerBilledCollection != null)
                {
                    _gridBoxB = ConsumerRecords.gridConsumerBilledCollection;
                }


                if (_gridBoxB == null)
                {
                    Grid tmpgridConsumerBilled = new Grid
                    {
                        AutomationId = $"BGrid_{maxbatchbilled}",
                        RowDefinitions =
                            {
                                new RowDefinition { Height = GridLength.Auto }
                            },
                        ColumnDefinitions =
                            {
                                new ColumnDefinition { Width = GridLength.Star }
                            }

                    };
                    tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    tmpgridConsumerBilled.Add(lframe_remove, 0, maxbatchbilled);
                    tmpgridConsumerBilled.Add(uframe_remove, 0, maxbatchbilled);
                    tmpgridConsumerBilled.Add(absoluteLayout_remove, 0, maxbatchbilled);
                    _gridBoxB.Add(tmpgridConsumerBilled);
                    ConsumerRecords.gridConsumerBilledCollection = _gridBoxB;
                }
                else
                {
                    if (_gridBoxB.Count == 0)
                    {
                        Grid tmpgridConsumerBilled = new Grid
                        {
                            AutomationId = $"BGrid_{maxbatchbilled}",
                            RowDefinitions =
                            {
                                new RowDefinition { Height = GridLength.Auto }
                            },
                            ColumnDefinitions =
                            {
                                new ColumnDefinition { Width = GridLength.Star }
                            }

                        };
                        tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        tmpgridConsumerBilled.Add(lframe_remove, 0, maxbatchbilled);
                        tmpgridConsumerBilled.Add(uframe_remove, 0, maxbatchbilled);
                        tmpgridConsumerBilled.Add(absoluteLayout_remove, 0, maxbatchbilled);
                        _gridBoxB.Add(tmpgridConsumerBilled);
                    }
                    else
                    {
                        foreach (Grid _gridchild in _gridBoxB)
                        {
                            if (_gridchild.AutomationId == "BGrid_" + maxbatchbilled)
                            {
                                int maxrow = _gridchild.RowDefinitions.Count;
                                _gridchild.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                _gridchild.Add(lframe_remove, 0, maxrow + 1);
                                _gridchild.Add(uframe_remove, 0, maxrow + 1);
                                _gridchild.Add(absoluteLayout_remove, 0, maxrow + 1);
                                break;
                            }
                        }
                    }

                }


            }
            //lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.lstconsumers.Count.ToString();
            int selectedItem = pickerBillingFilter.SelectedIndex;
            if (selectedItem == 0)
            {

                lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else if (selectedItem == 1)
            {

                lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else
            {

                lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
            }
            await DisplayAlertAsync("Notice", "Reading Saved!", "Ok");

            if (MyDeviceInfo.currentPrintConfig != null)
            {
                bool toPrint = false;
                if (isPrintOn)
                {
                    toPrint = true;
                    //string output = await DoPrintv2(consumer);
                    //await DisplayAlertAsync("Print Notice", output, "Ok");
                }
                else
                {
                    toPrint = await DisplayAlertAsync("Confirm", "Print Transaction?", "Yes", "No");
                }

                if (toPrint)
                {
                    string output = await DoPrintBill(consumer);
                    await DisplayAlertAsync("Print Notice", output, "Ok");
                }


            }

            //Move to next seq no
            OnNextRecord(pickerBillingFilter.SelectedIndex);

        }
        else
        {
            await DisplayAlertAsync("Error", "Failed to saved ", "Ok");
        }
    }


    private async void TapImgBack(object sender, EventArgs e)
    {
        //restruct list before going back to consumer list
        ConsumerRecords.AutoID_UIndex = new List<string>();
        ConsumerRecords.AutoID_BIndex = new List<string>();

        int ctrx = 0;
        if (ConsumerRecords.gridConsumerUnbilledCollection.Count > 0)
        {
            foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerUnbilledCollection)
            {
                string gridautoID = _gridConsumer.AutomationId;
                ConsumerRecords.AutoID_UIndex.Add(gridautoID + "," + ctrx);
                ctrx++;

            }
        }

        ctrx = 0;
        if (ConsumerRecords.gridConsumerBilledCollection.Count > 0)
        {
            foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerBilledCollection)
            {
                string gridautoID = _gridConsumer.AutomationId;
                ConsumerRecords.AutoID_BIndex.Add(gridautoID + "," + ctrx);
                ctrx++;
            }
        }

        await Navigation.PushAsync(new ReadingConsumers());
        //await Navigation.PopAsync();
    }

    private void OnClickB7(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "7" : txtCustomEntry.Text + "7";
    }

    private void OnClickB8(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "8" : txtCustomEntry.Text + "8";
    }

    private void OnClickB9(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "9" : txtCustomEntry.Text + "9";
    }

    private void OnClickClear(object sender, EventArgs e)
    {
        txtCustomEntry.Text = "0";
    }

    private void OnClickB4(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "4" : txtCustomEntry.Text + "4";
    }

    private void OnClickB5(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "5" : txtCustomEntry.Text + "5";
    }

    private void OnClickB6(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "6" : txtCustomEntry.Text + "6";
    }

    private void OnClickDash(object sender, EventArgs e)
    {
        if (selected_field == 0)
        {
            txtCustomEntry.Text = txtCustomEntry.Text.Contains("-") ? txtCustomEntry.Text.Replace("-", "") : "-" + txtCustomEntry.Text;
            return;
        }
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "-" : txtCustomEntry.Text + "-";
    }

    private void OnClickB1(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "1" : txtCustomEntry.Text + "1";
    }

    private void OnClickB2(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "2" : txtCustomEntry.Text + "2";
    }

    private void OnClickB3(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "3" : txtCustomEntry.Text + "3";
    }

    private async void OnClickOk(object sender, EventArgs e)
    {

        gridNumericPad.IsVisible = false;
        gridDetails.IsEnabled = true;
        if (txtCustomEntry.Text == "")
        {
            return;
        }

        switch (selected_field)
        {
            case 0:

                txtConsumerPresReading.Text = ParseFloat(txtCustomEntry.Text).ToString();
                //Compute
                Consumers? consumer = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();
                if (consumer != null)
                {
                    consumer = await OnInitialComputation(consumer);
                    txtConsumerCurrentBill.Text = consumer.currentbill.ToString("#,##0.00");
                    txtConsumerTotalBill.Text = consumer.total_bill.ToString("#,##0.00");
                    txtConsumerUsage.Text = consumer.usage.ToString("0.00");
                    //consumer.collectiondate = DateTime.Now.AddDays(billrateGracePeriodDue).ToString();
                }
                //btnSave.Focus();
                break;
            case 1:
                // txtConsumerAcctNo.Text = txtCustomEntry.Text;
                OnFilterNameSearch(txtCustomEntry.Text);
                break;
            case 2:
                //txtConsumerMeterNo.Text = txtCustomEntry.Text;
                OnFilterNameSearch(txtCustomEntry.Text);
                break;
        }
        btnSave.Focus();
    }

    private void OnClickB0(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "0" : txtCustomEntry.Text + "0";
    }

    private void OnClickPeriod(object sender, EventArgs e)
    {
        txtCustomEntry.Text = txtCustomEntry.Text + ".";
    }

    private void OnClickBackSpace(object sender, EventArgs e)
    {
        if (txtCustomEntry.Text.Length == 0)
        {
            return;
        }

        if (selected_field == 0)
        {
            txtCustomEntry.Text = txtCustomEntry.Text.Length == 1 ? "0" : txtCustomEntry.Text.Substring(0, txtCustomEntry.Text.Length - 1);
        }
        else
        {
            txtCustomEntry.Text = txtCustomEntry.Text.Length == 1 ? "" : txtCustomEntry.Text.Substring(0, txtCustomEntry.Text.Length - 1);
        }
    }

    private void OnAcctNoTapped(object sender, EventArgs e)
    {
        gridDetails.IsEnabled = false;

        selected_field = 1;

        gridNumericPad.IsVisible = true;
        gridNumericPad.IsEnabled = true;
        txtCustomEntry.Text = "";
    }

    private void OnMeterNoTapped(object sender, EventArgs e)
    {
        gridDetails.IsEnabled = false;


        selected_field = 2;

        gridNumericPad.IsVisible = true;
        gridNumericPad.IsEnabled = true;
        txtCustomEntry.Text = txtConsumerMeterNo.Text;
    }

    private void OnClickCancel(object sender, EventArgs e)
    {
        gridNumericPad.IsVisible = false;
        gridDetails.IsEnabled = true;


    }

    private void OnPresReadingTapped(object sender, EventArgs e)
    {
        gridDetails.IsEnabled = false;

        selected_field = 0;

        gridNumericPad.IsVisible = true;
        gridNumericPad.IsEnabled = true;
        txtCustomEntry.Text = txtConsumerPresReading.Text;

    }

    private void OnFirstRecordTapped(object sender, EventArgs e)
    {
        OnFirstLastRecord(1, pickerBillingFilter.SelectedIndex);
    }

    private void OnConsumerTypeChanged(object sender, EventArgs e)
    {
        isFilterChange = true;
        int selectedItem = pickerBillingFilter.SelectedIndex;
        if (selectedItem == 0)
        {
            btnPrint.IsEnabled = false;
            lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
        }
        else if (selectedItem == 1)
        {
            btnPrint.IsEnabled = true;
            lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
        }
        else
        {

            lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
        }

        OnFirstLastRecord(0, pickerBillingFilter.SelectedIndex);
    }

    private async Task<Consumers> OnInitialComputation(Consumers consumer)
    {
        Consumers _tempconsumer = consumer;
        string? remarks = pickerRemarks.SelectedIndex != -1 ? pickerRemarks.SelectedItem.ToString() : "";
        float presReading = ParseFloat(txtConsumerPresReading.Text);

        if (presReading == 0 && remarks == "Zero Reading")
        {
            _tempconsumer.usage = 0;
            _tempconsumer.total_bill = 0;



            return _tempconsumer;
        }

        if (remarks == "" || remarks == "Normal Reading")
        {
            usage = ParseFloat(txtConsumerPresReading.Text) - ParseFloat(txtConsumerPrevReading.Text);
        }
        else if (remarks == "" || remarks == "Abnoral Reading")
        {
            usage = ParseFloat(txtConsumerUsage.Text);
        }
        else if (remarks == "Meter Reset")
        {
            if (txtConsumerPrevReading.Text.Length <= 4)
            {
                usage = (10000 - ParseFloat(txtConsumerPrevReading.Text)) + ParseFloat(txtConsumerPresReading.Text);
            }
            if (txtConsumerPrevReading.Text.Length == 5)
            {
                usage = (100000 - ParseFloat(txtConsumerPrevReading.Text)) + ParseFloat(txtConsumerPresReading.Text);
            }
        }
        else if (remarks == "Meter Reset")
        {
            usage = 0;
        }
        else if (remarks.Contains("-AM"))
        {
            //Put Average Moving
            usage = _tempconsumer.average_usage;
            float fAverageMoving = ParseFloat(txtConsumerPrevReading.Text) + usage;
            txtConsumerPresReading.Text = fAverageMoving.ToString();
        }
        else if (remarks.Contains("-ANM"))
        {
            //Put Average Not Moving
            usage = _tempconsumer.average_usage;
            txtConsumerPresReading.Text = txtConsumerPrevReading.Text;
        }
        else
        {
            usage = ParseFloat(txtConsumerPresReading.Text) - ParseFloat(txtConsumerPrevReading.Text);
        }

        //Add Usage After Change MEter
        if (consumer.change_meter_usage > 0)
        {
            usage = usage + consumer.change_meter_usage;
        }

        if (usage < 0)
        {
            _tempconsumer.usage = 0;
            usage = 0;
            //_tempconsumer.total_bill = 0;            
        }
        else
        {
            consumer.usage = usage;
        }

        if (_tempconsumer.less_usage > 0)
        {
            _currentBillDiscount = await OnTotalBillCompute(_tempconsumer, _tempconsumer.less_usage);
        }

        float _currentTotalBill = await OnTotalBillCompute(_tempconsumer, usage);
        _tempconsumer.penaltyamt = _currentPenalty;
        _tempconsumer.currentbill = _currentBillAmount;
        _tempconsumer.total_bill = _currentTotalBill;

        return _tempconsumer;

    }

    private async Task<float> OnTotalBillCompute(Consumers consumer, float _paramUsage)
    {
        float _TotalBill = 0;
        float[] _computedAmountperCommodities = new float[5];
        float _computedUsage = _paramUsage;

        bool IsSewerage = consumer.parameters.Contains("IsSewerage") ? true : false;
        bool IsFlatRate = consumer.parameters.Contains("IsFlatRate") ? true : false;
        if (IsSewerage && IsFlatRate)
        {
            _TotalBill = _TotalBill + consumer.sewerage;
            //Penalty
            _currentPenalty = (_TotalBill - _currentBillDiscount) * consumer.special_penalty_rate;
            if (consumer.arrears > 0)
            {
                _currentPenalty = ((_TotalBill + consumer.arrears) - _currentBillDiscount) * consumer.special_penalty_rate;
            }

            _currentBillAmount = _TotalBill;
            _TotalBill = _TotalBill + consumer.othercharges + consumer.arrears;
            return _TotalBill;
        }


        //initial value
        for (int x = 0; x < 5; x++)
        {
            _computedAmountperCommodities[x] = 0;
        }

        //change_meter_usage already added on initial computation
        ////Change Meter Usage
        //if (consumer.change_meter_usage > 0)
        //{
        //    _computedUsage = _computedUsage + consumer.change_meter_usage;
        //}

        //Minimum Usage
        if (consumer.minusage > 0)
        {
            _computedUsage = _computedUsage - consumer.minusage;
        }

        if (_computedUsage < 0)
        {
            _computedUsage = 0;
        }

        //if (_computedUsage == 0)
        //{
        //    _TotalBill = 0;
        //    return _TotalBill;
        //}

        dbService = new DBService();
        List<Billing_rates> billrate = await dbService.GetBillrates(consumer.consumer_type, consumer.metersize);
        if (billrate != null)
        {
            //index 0 is for minimum amount
            //compute usage per 10 usage
            int _maxBillrate;
            int _extraUsage;
            int _maxCommoditiesto = Math.DivRem((int)_computedUsage, 10, out _extraUsage);

            if (_maxCommoditiesto > 4)
            {
                _extraUsage = ((_maxCommoditiesto - 4) * 10) + _extraUsage;
                _maxCommoditiesto = 5;

            }
            else
            {
                _maxCommoditiesto = _extraUsage == 0 ? _maxCommoditiesto : _maxCommoditiesto + 1;
            }

            _maxBillrate = _maxCommoditiesto - 1;

            if ((_maxCommoditiesto == 0) || (_maxBillrate == 0))
            {
                _computedAmountperCommodities[0] = billrate[0].brate;
            }
            else
            {
                for (int x = 0; x <= _maxBillrate; x++)
                {
                    if (x == _maxBillrate && _extraUsage > 0)
                    {
                        _computedAmountperCommodities[x] = _extraUsage * billrate[x].brate;
                    }
                    else
                    {
                        if (x == 0)
                        {
                            _computedAmountperCommodities[x] = billrate[x].brate;
                        }
                        else
                        {
                            _computedAmountperCommodities[x] = 10 * billrate[x].brate;
                        }

                    }

                }
            }


            for (int x = 0; x < 5; x++)
            {
                _TotalBill = _TotalBill + _computedAmountperCommodities[x];
            }


            //Penalty
            _currentPenalty = (_TotalBill - _currentBillDiscount) * consumer.special_penalty_rate;
            if (consumer.arrears > 0)
            {
                _currentPenalty = ((_TotalBill + consumer.arrears) - _currentBillDiscount) * consumer.special_penalty_rate;
            }


            _currentBillAmount = _TotalBill;
            _TotalBill = _TotalBill + consumer.othercharges + consumer.arrears;

        }

        return _TotalBill;
    }

    //////private async Task<float> OnBillComputation(Consumers consumer, float _paramUsage)
    //////{
    //////    float _consumerTotalBill = 0;
    //////    float[] curWaterRate = new float[11];
    //////    float[] waterRate = new float[11];
    //////    float[] waterUsed = new float[11];
    //////    float[] curCommoditiesRange = new float[11];
    //////    float dobPCARate = 0;

    //////    string gotoAddress = "";


    //////    float curBill =0;
    //////    float used;
    //////    string stType;
    //////    string strGOTOctrl;
    //////    bool IsLessMinUsageUP;

    //////    string companyCode = "";

    //////    float consumerMinUsage = consumer.minusage;
    //////    float consumerSewerage = ParseFloat(consumer.sewerage);

    //////    used = _paramUsage;

    //////    IsLessMinUsageUP = false;        
    //////    if (consumerMinUsage > 0)
    //////    {
    //////        used = used - consumerMinUsage;
    //////        IsLessMinUsageUP = true;
    //////    }        

    //////    used = used < 0 ? 0 : used;

    //////    for (int x = 0; x < 11; x++)
    //////    {
    //////        waterRate[x] = 0;
    //////        waterUsed[x] = 0;
    //////    }

    //////    string _consumerCombineRates=consumer.combine_rates;
    //////    string _consumerCombineType="";
    //////    float _consumerCombineUsage = 0;
    //////    float _consumerUsage;
    //////    float _consumerSewerage = ParseFloat(consumer.sewerage);
    //////    float _consumerCurrentBill = 0;


    //////    int ctrTab = _consumerCombineRates.Count(c => c == '\t');

    //////    if (ctrTab > 0)
    //////    {
    //////        string[] _consumerCombine = _consumerCombineRates.Split("\t");
    //////        if (_consumerCombine.Length > 0) {
    //////            _consumerCombineType = _consumerCombine[0];
    //////            _consumerCombineRates = _consumerCombine[1];
    //////            _consumerCombineUsage = ParseFloat(_consumerCombineRates);
    //////        }

    //////        isRecompute = false;
    //////        isMinus = false;
    //////        //_consumerUsage = ParseFloat(txtConsumerUsage.Text);
    //////        _consumerUsage = used;
    //////        if (_consumerCombineUsage < _consumerUsage && _consumerCombineUsage > 0)
    //////        {
    //////            used = _consumerCombineUsage;
    //////            isRecompute = true;
    //////            isMinus = false;
    //////            gotoAddress = "OriginalType";                            

    //////        }
    //////    }

    //////startMO:
    //////    try
    //////    {
    //////        if (txtConsumerAcctNo.Text != string.Empty && used >= 0)
    //////        {
    //////            //get billing rates
    //////            dbService = new DBService();
    //////            List<Billing_rates> billrate = await dbService.GetBillrates(consumer.consumer_type,consumer.metersize);
    //////            if (billrate != null)
    //////            {
    //////                int maxctr = billrate.Count;
    //////                billratePcaRate = billrate[0].pca_rate;
    //////                billrateGracePeriodDue = billrate[0].graceperioddue;

    //////                isSewerage = consumer.parameters.Contains("IsSewerage") ? true : false;

    //////                if (isSewerage)
    //////                {
    //////                    consumerSewerage = billrate[0].sewerage;
    //////                }

    //////                int maxbill_index = 10;
    //////                if (billrate.Count < 11)
    //////                {
    //////                    maxbill_index = billrate.Count-1;
    //////                }

    //////                for (int x = 0;x < maxbill_index; x++)
    //////                {
    //////                    curWaterRate[x + 1] = billrate[x].brate;
    //////                    curCommoditiesRange[x + 1] = 10;
    //////                }

    //////                int indexctr = 1;
    //////                if (indexctr < maxctr)
    //////                {
    //////                    if (used >= 0 || used <= curCommoditiesRange[1])
    //////                    {
    //////                        if (!IsLessMinUsageUP)
    //////                        {
    //////                            if (used > curCommoditiesRange[1])
    //////                            {
    //////                                used = used - curCommoditiesRange[1];
    //////                            }
    //////                            else
    //////                            {
    //////                                used = 0;
    //////                            }
    //////                        }

    //////                        waterRate[1] = curWaterRate[1];
    //////                        waterUsed[1] = used;
    //////                    }
    //////                }

    //////                isFlatRate = consumer.parameters.Contains("IsFlatRate") ? true : false;

    //////                if (isFlatRate || isSewerage)
    //////                {
    //////                    curBill = ParseFloat(consumerSewerage);
    //////                }
    //////                else
    //////                {
    //////                    if (isSewerage)
    //////                    {
    //////                        curBill = curBill + ParseFloat(consumerSewerage);
    //////                    }
    //////                }

    //////                if (isZeoMin)
    //////                {
    //////                    if (used <= 0)
    //////                    {
    //////                        curBill = 0;
    //////                    }                            
    //////                }

    //////                for (indexctr = 2; indexctr < maxctr; indexctr++)
    //////                {
    //////                    if (indexctr == 5)
    //////                    {
    //////                        if (used >= 0)
    //////                        {
    //////                            curBill = curBill + (curWaterRate[indexctr] * used);
    //////                            waterRate[indexctr] = curWaterRate[indexctr];
    //////                            waterUsed[indexctr] = used;
    //////                        }
    //////                        break;
    //////                    }
    //////                    if (used >= curCommoditiesRange[indexctr])
    //////                    {
    //////                        curBill = curBill + (curWaterRate[indexctr] * curCommoditiesRange[indexctr]);
    //////                        waterUsed[indexctr] = curCommoditiesRange[indexctr];
    //////                        used = used - curCommoditiesRange[indexctr];
    //////                    }
    //////                    else
    //////                    {
    //////                        curBill = curBill + (curWaterRate[indexctr] * used);
    //////                        waterUsed[indexctr] = used;
    //////                        used = 0;
    //////                    }
    //////                    waterRate[indexctr] = curWaterRate[indexctr];
    //////                }

    //////                if (isMinus)
    //////                {
    //////                    _consumerCurrentBill = _consumerCurrentBill - curBill;
    //////                }
    //////                else
    //////                {
    //////                    _consumerCurrentBill = _consumerCurrentBill + curBill;
    //////                }

    //////                if (isRecompute)
    //////                {
    //////                    if (gotoAddress == "OriginalType")
    //////                    {
    //////                        stType = _consumerCombineType;
    //////                        used = usage;
    //////                        isRecompute = true;
    //////                        isMinus = false;
    //////                        gotoAddress = "CombineType";
    //////                        goto startMO;
    //////                    }
    //////                    else if (gotoAddress == "CombineType")
    //////                    {
    //////                        stType = _consumerCombineType;
    //////                        used = _consumerCombineUsage;
    //////                        isRecompute = false;
    //////                        isMinus = true;
    //////                        goto startMO;
    //////                    }
    //////                }

    //////                float taxAmount = (float)(curBill * (billrate[0].ftax * 0.01));
    //////                float surcharageAmount = 0;
    //////                _currentBillAmount = _consumerCurrentBill + billratePcaRate + taxAmount + surcharageAmount;

    //////                _consumerTotalBill = _currentBillAmount + consumer.othercharges + consumer.arrears;


    //////                if (_consumerTotalBill > 0)
    //////                {
    //////                    if (consumer.isnopenalty==1)
    //////                    {
    //////                        _currentPenalty = 0;
    //////                    }
    //////                    else
    //////                    {
    //////                        if (consumer.arrears > 0)
    //////                        {
    //////                            _currentPenalty = (_consumerCurrentBill - _currentBillDiscount) * consumer.special_penalty_rate;
    //////                        }
    //////                        else
    //////                        {
    //////                            _currentPenalty = (_consumerCurrentBill - _currentBillDiscount + consumer.arrears) * consumer.special_penalty_rate;
    //////                        }
    //////                    }
    //////                }
    //////                else
    //////                {
    //////                    _currentPenalty = 0;
    //////                }

    //////                if (_currentPenalty < 0)
    //////                {
    //////                    _currentPenalty = 0;
    //////                }

    //////                _consumerTotalBill = _consumerTotalBill + _currentPenalty;
    //////            }
    //////        }
    //////    }
    //////    catch (Exception ex)
    //////    {

    //////    }

    //////    return _consumerTotalBill;

    //////}

    private void GetRoundAmount()
    {

    }




    //////private static string NormalizeMacAddress(string raw)
    //////{
    //////    string hexOnly = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    //////    if (hexOnly.Length != 12)
    //////        throw new ArgumentException($"'{raw}' does not contain a valid 12-hex-digit MAC address.");

    //////    return string.Join(":", Enumerable.Range(0, 6).Select(i => hexOnly.Substring(i * 2, 2)));
    //////}



    public async Task<string> DoPrintBill(Consumers consumer)
    {
        string result = "";
        string strHeaderFileName = MOA.GetClientLogo(consumer.parameters);

        InitializeDB();

        bool IsSJWD = consumer.parameters.Contains("[SJWD]");
        bool IsMVWD = consumer.parameters.Contains("[MVWD]");
        bool IsIndangWD = consumer.parameters.Contains("[IWD]");
        bool IsCWD = consumer.parameters.Contains("[CWD]");

        // ==================================================================
        // GLOBAL ALIGNMENT: parse the Line Space into a PaperProfile.
        //   32 / 48       => 32 / 48 columns, DefaultAlignment (usually left)
        //   321 / 482     => columns + alignment digit  (1 = L, 2 = C, 3 = R)
        //   323           => 32 columns, right aligned
        // The PaperProfile computes OffsetDots, and EscPOScmdPrint sends
        // GS L / GS W from that offset, so the WHOLE receipt is physically
        // shifted on the paper — no per-line PadLeft/PadRight needed.
        // ==================================================================
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
            await printer.AlignAsync(PrintAlign.Left);   // we do alignment ourselves — profile OffsetDots handles the rest

            // ================= LOGO =================
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            // ================= DATES =================
            string[] inputFormats =
            {
            "yyyy-MM-dd hh:mm:ss tt", "yyyy-MM-dd h:mm:ss tt",
            "yyyy-MM-dd", "yyyy-dd-MM", "yyyy-M-d", "yyyy-d-m",
            "MM-dd-yyyy", "dd-MM-yyyy", "M/d/yyyy", "d/M/yyyy",
            "MM/d/yyyy", "dd/M/yyyy", "M/dd/yyyy", "d/MM/yyyy",
            "MM/dd/yyyy", "dd/MM/yyyy"
        };

            DateTime billduedate = DateTime.ParseExact(consumer.duedate, inputFormats, CultureInfo.InvariantCulture);
            DateTime prevbilldate = DateTime.ParseExact(consumer.prevreadingdate, inputFormats, CultureInfo.InvariantCulture);
            DateTime currentbilldate = DateTime.ParseExact(consumer.billdate, inputFormats, CultureInfo.InvariantCulture);

            string printDueDate = billduedate.ToString("MM/dd/yyyy");
            string printPrevDate = prevbilldate.ToString("MM/dd/yyyy");
            string printCurrentDate = currentbilldate.ToString("MM/dd/yyyy");
            string printCurrentMonthYear = currentbilldate.ToString("MMM yyyy");
            string printPrevMonth = currentbilldate.AddMonths(-1).ToString("MMMM");

            string discDay = billduedate.AddDays(2).ToString("dddd");
            string printDiscDate =
                discDay == "Saturday" ? billduedate.AddDays(4).ToString("MM/dd/yy") :
                discDay == "Sunday" ? billduedate.AddDays(3).ToString("MM/dd/yy") :
                                        billduedate.AddDays(2).ToString("MM/dd/yy");

            // ============= COMPANY HEADER =============
            if (IsIndangWD)
            {
                await printer.PrintTextAsync("San Gregorio St., Indang, Cavite", PrintAlign.Center);
                await printer.PrintTextAsync("NON VAT Reg.TIN 000-773-614-000", PrintAlign.Center);
                await printer.PrintTextAsync("Mobile#: 0917-825-3813", PrintAlign.Center);
            }
            else if (IsCWD)
            {
                await printer.PrintTextAsync("Lakeview Subd.Halang,Calamba City", PrintAlign.Center);
                await printer.PrintTextAsync("NON VAT Reg.TIN 001-222-423-00000", PrintAlign.Center);
                await printer.PrintTextAsync("Tel#: 545-2863/545-1614/545-9344", PrintAlign.Center);
            }
            else if (IsSJWD)
            {
                await printer.PrintTextAsync("HallSt,Poblacion,SanJuan,Batangas", PrintAlign.Center);
                await printer.PrintTextAsync("NON VAT Reg.TIN 001-774-488-000", PrintAlign.Center);
                await printer.PrintTextAsync("Mobile#: 09199947756", PrintAlign.Center);
            }
            else if (IsMVWD)
            {
                await printer.PrintTextAsync("Pantay Daya,Vigan City,IlocosSur", PrintAlign.Center);
                await printer.PrintTextAsync("TIN 000-566-175-000 Non-VAT", PrintAlign.Center);
                await printer.PrintTextAsync("Tel. No. 077-674-0870", PrintAlign.Center);
            }

            await printer.FeedAsync(1);

            // ================= TITLE =================
            await printer.PrintTextAsync("BILLING NOTICE", PrintAlign.Center, new PrintStyle(Bold: true, High: true));
            await printer.PrintTextAsync("For the month of " + printCurrentMonthYear, PrintAlign.Center);
            await printer.PrintTextAsync("(" + printPrevMonth + " Consumption)", PrintAlign.Center);
            await printer.FeedAsync(1);

            // ============= CONSUMER INFO =============
            await printer.PrintTextAsync(consumer.consumer_name, PrintAlign.Left, new PrintStyle(Bold: true));
            await printer.PrintTextAsync(consumer.address, PrintAlign.Left);

            string accountNoText = (IsMVWD || IsSJWD)
                ? "AccountNo:" + consumer.accountNo + "-" + consumer.consumer_type + consumer.metersize
                : "AccountNo:" + consumer.accountNo;
            await printer.PrintTextAsync(accountNoText, PrintAlign.Left, new PrintStyle(High: true));

            if (IsSJWD)
            {
                string consumerTypeDescription = Database.Table<Billing_rates>()
                    .Where(b => b.consumer_type == consumer.consumer_type && b.metersize == consumer.metersize)
                    .Select(b => b.constype_description)
                    .FirstOrDefault() ?? "";

                if (!string.IsNullOrWhiteSpace(consumerTypeDescription))
                    await printer.PrintTextAsync(consumerTypeDescription.Trim(), PrintAlign.Left, new PrintStyle(Bold: true));
            }

            await printer.PrintTextAsync("Meter No.   : " + consumer.meterno, PrintAlign.Left);
            if (IsSJWD)
                await printer.PrintTextAsync("Seq. No.    : " + consumer.seqno, PrintAlign.Left);

            await printer.PrintSeparatorAsync('-');

            await printer.PrintRowAsync("Period From", printPrevDate);
            await printer.PrintRowAsync("Period To", printCurrentDate);
            await printer.PrintRowAsync("Due Date", printDueDate, new PrintStyle(High: true));
            await printer.PrintSeparatorAsync('-');

            // ============= AMOUNT CALCULATION =============
            decimal runningTotalBill = 0.00m;
            decimal currentBill = Convert.ToDecimal(consumer.currentbill);
            decimal sewerageAmount = 0.00m;

            bool IsSewerage = consumer.parameters.Contains("IsSewerage");
            bool IsFlatRate = consumer.parameters.Contains("IsFlatRate");

            if (IsMVWD)
            {
                if (IsSewerage)
                {
                    sewerageAmount = Convert.ToDecimal(consumer.sewerage);
                    if (IsFlatRate) currentBill = 0.00m;
                }
            }
            else
            {
                float sewerageRate = Database.Table<Billing_rates>()
                    .Where(b => b.consumer_type == consumer.consumer_type && b.metersize == consumer.metersize)
                    .Select(b => b.sewerage)
                    .FirstOrDefault();

                sewerageAmount = Convert.ToDecimal(sewerageRate);

                if (double.IsNaN(Convert.ToDouble(sewerageAmount)) || sewerageAmount == 0)
                {
                    sewerageAmount = 0.00m;
                    if (IsCWD) sewerageAmount = 2.93m;
                }

                if (sewerageAmount > 0)
                {
                    decimal usage = Convert.ToDecimal(consumer.usage);
                    sewerageAmount = Math.Round(sewerageAmount * usage, 2);
                    decimal esfFtax = Math.Round(sewerageAmount * 0.02m, 2);
                    sewerageAmount = Math.Round(sewerageAmount + esfFtax, 2);
                }
            }

            if (!IsFlatRate || !IsMVWD)
            {
                await printer.PrintRowAsync("Pres Reading", consumer.presreading.ToString());
                await printer.PrintRowAsync("Prev Reading", consumer.prevreading.ToString());
                await printer.PrintRowAsync("Consumption", consumer.usage.ToString());
                await printer.PrintSeparatorAsync('-');
            }

            if (currentBill > 0)
            {
                runningTotalBill += currentBill;
                await printer.PrintRowAsync("Current Bill", currentBill.ToString("#,##0.00"));
            }

            if (sewerageAmount > 0)
            {
                runningTotalBill += sewerageAmount;
                await printer.PrintRowAsync(IsMVWD ? "Sewerage" : "Septage Mgmt. Fee",
                                            sewerageAmount.ToString("#,##0.00"));
            }

            if (!IsIndangWD)
            {
                isSenior = consumer.parameters.Contains("IsSenior");
                if (isSenior && consumer.usage <= 30)
                {
                    decimal seniorDiscount = Math.Round(currentBill * 0.05m * -1, 2);
                    runningTotalBill += seniorDiscount;
                    await printer.PrintRowAsync("Senior Discount", seniorDiscount.ToString("#,##0.00"));
                }
            }

            decimal penaltyAmount = runningTotalBill * Convert.ToDecimal(consumer.special_penalty_rate);

            decimal dArrears = Convert.ToDecimal(consumer.arrears);
            runningTotalBill += dArrears;
            if (consumer.arrears < 0)
                penaltyAmount = runningTotalBill * Convert.ToDecimal(consumer.special_penalty_rate);

            await printer.PrintRowAsync(consumer.arrears < 0 ? "Advance" : "Arrears", dArrears.ToString("#,##0.00"));

            decimal dOthers = Convert.ToDecimal(consumer.othercharges);
            if (dOthers > 0)
            {
                runningTotalBill += dOthers;
                await printer.PrintRowAsync("Others", dOthers.ToString("#,##0.00"));
            }

            await printer.PrintRowAsync("BEFORE DUE", runningTotalBill.ToString("#,##0.00"),
                                        new PrintStyle(High: true));

            await printer.PrintSeparatorAsync('-');
            await printer.PrintTextAsync("* If unpaid after Due Date.", PrintAlign.Left, new PrintStyle(Underline: true));

            if (penaltyAmount < 0 || consumer.isnopenalty == 1)
                penaltyAmount = 0.00m;
            else
                penaltyAmount = IsMVWD ? Math.Round(penaltyAmount, 1) : Math.Round(penaltyAmount, 2);

            await printer.PrintRowAsync("Penalty", penaltyAmount.ToString("#,##0.00"));
            runningTotalBill += penaltyAmount;

            await printer.PrintRowAsync("AFTER DUE", runningTotalBill.ToString("#,##0.00"),
                                        new PrintStyle(High: true));
            await printer.PrintSeparatorAsync('=');

            if (IsSJWD)
            {
                if (runningTotalBill > 0.00m)
                {
                    await printer.PrintTextAsync("DUE FOR DISCONNECTION ON " + printDiscDate,
                                                 PrintAlign.Center, new PrintStyle(High: true));
                    await printer.PrintSeparatorAsync('-');
                }
            }
            else
            {
                if (IsMVWD && dArrears >= 215.5m)
                {
                    await printer.PrintRowAsync("Discon Date", printDiscDate, new PrintStyle(High: true));
                    await printer.PrintSeparatorAsync('-');
                }
                await printer.PrintRowAsync("Reading Date", currentbilldate.ToString("MM/dd/yyyy"));
                await printer.PrintSeparatorAsync('-');
            }

            string printRemarks = string.IsNullOrWhiteSpace(consumer.remarks) ? "Normal Reading" : consumer.remarks;
            await printer.PrintRowAsync("Remarks", printRemarks);
            await printer.PrintRowAsync("Meter Reader", consumer.billedby);

            // ================= NOTICES =================
            await printer.PrintTextAsync("This is not valid as", PrintAlign.Center);
            await printer.PrintTextAsync("Official Receipt", PrintAlign.Center);
            await printer.PrintTextAsync("Please disregard arrears if", PrintAlign.Center);
            await printer.PrintTextAsync("payment has been made", PrintAlign.Center);
            await printer.PrintTextAsync("PAY YOUR BILLS ON TIME TO AVOID", PrintAlign.Center);
            await printer.PrintTextAsync("PENALTY", PrintAlign.Center);

            string strNotif = LoginUser.login_user.msgmo;
            if (!string.IsNullOrWhiteSpace(strNotif))
                await printer.PrintTextAsync("REMINDER : " + strNotif, PrintAlign.Left, new PrintStyle(Bold: true));

            await printer.FeedAsync(2);

            // ================= BARCODE =================
            await printer.WriteBarcodeAsync(consumer.accountNo.Trim(), BarcodeType.Code128,
                                            widthFraction: 0.8, heightDots: 70,
                                            humanText: false, maxModuleDots: 3,
                                            align: PrintAlign.Center);

            // ============== SEND EVERYTHING ==============
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
    public async Task<string> DoPrintBill_HoneyWell(Consumers consumer)
    {
        string result = "";
        int spacesleft = 0;
        int printer_space = 33;

        await Task.Run(() =>
        {
            LinePrinter linePrinter = null;

            try
            {

                InitializeDB();
                ReadProFile();

                //{Init Printer
                string bt_address = "bt://" + MyDeviceInfo.currentPrintConfig.bluetooth_address;
                //Based on config
                //string printer_profile = MyDeviceInfo.currentPrintConfig.profileconfig; 
                //int printer_space = MyDeviceInfo.currentPrintConfig.linespacing;

                //Hard Coded
                string printer_profile = "PB21"; //2-inch(58mm) - Font A = 32 cols, Font B = 42 cols  
                                                 //string printer_profile = "PB31"; //3-inch(80mm) - Font A = 46 cols, Font B = 62 cols 


                linePrinter = new LinePrinter(profiles, printer_profile, bt_address);
                linePrinter.Connect();
                //}Init Printer



                string[] inputFormats =
                {
                    "yyyy-MM-dd hh:mm:ss tt",
                    "yyyy-MM-dd h:mm:ss tt",
                    "MM/dd/yyyy",
                    "dd/MM/yyyy"
                };

                DateTime billduedate = DateTime.ParseExact(consumer.duedate, inputFormats, CultureInfo.InvariantCulture);
                DateTime prevbilldate = DateTime.ParseExact(consumer.prevreadingdate, inputFormats, CultureInfo.InvariantCulture);
                DateTime currentbilldate = DateTime.ParseExact(consumer.billdate, inputFormats, CultureInfo.InvariantCulture);

                string printDueDate = billduedate.ToString("MM/dd/yyyy");
                string printPrevDate = prevbilldate.ToString("MM/dd/yyyy");
                string printCurrentDate = currentbilldate.ToString("MM/dd/yyyy");
                string printCurrentMonthYear = currentbilldate.ToString("MMM yyyy");
                string printPrevMonth = currentbilldate.AddMonths(-1).ToString("MMMM");

                string printDiscDate = "";
                if (billduedate.AddDays(2).ToString("dddd") == "Saturday")
                {
                    printDiscDate = billduedate.AddDays(4).ToString("MM/dd/yy");
                }
                else if (billduedate.AddDays(2).ToString("dddd") == "Sunday")
                {
                    printDiscDate = billduedate.AddDays(3).ToString("MM/dd/yy");
                }
                else
                {
                    printDiscDate = billduedate.AddDays(2).ToString("MM/dd/yy");
                }

                bool IsSJWD = consumer.parameters.Contains("[SJWD]") ? true : false;
                bool IsMVWD = consumer.parameters.Contains("[MVWD]") ? true : false;
                bool IsIndangWD = consumer.parameters.Contains("[IWD]") ? true : false;
                bool IsCWD = consumer.parameters.Contains("[CWD]") ? true : false;

                if (IsIndangWD)
                {
                    linePrinter.Bold = true;
                    linePrinter.DoubleWide = true;
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("     INDANG" + "\n");
                    linePrinter.Write(" WATER DISTRICT" + "\n");
                    linePrinter.DoubleHigh = false;
                    linePrinter.DoubleWide = false;
                    linePrinter.Bold = false;

                    linePrinter.Write("San Gregorio St., Indang, Cavite" + "\n");
                    linePrinter.Write(" NON VAT Reg.TIN 000-773-614-000" + "\n");
                    linePrinter.Write("    Mobile#: 0917-825-3813" + "\n\n");
                }
                else if (IsCWD)
                {
                    linePrinter.Bold = true;
                    linePrinter.DoubleWide = true;
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("     CALAMBA" + "\n");
                    linePrinter.Write(" WATER DISTRICT" + "\n");
                    linePrinter.DoubleHigh = false;
                    linePrinter.DoubleWide = false;
                    linePrinter.Bold = false;

                    linePrinter.Write("Lakeview Subd.Halang,Calamba City" + "\n");
                    linePrinter.Write("NON VAT Reg.TIN 001-222-423-00000" + "\n");
                    linePrinter.Write(" Tel#: 545-2863/545-1614/545-9344" + "\n\n");
                }
                else if (IsSJWD)
                {
                    linePrinter.Bold = true;
                    linePrinter.DoubleWide = true;
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("    SAN JUAN" + "\n");
                    linePrinter.Write(" WATER DISTRICT" + "\n");
                    linePrinter.DoubleHigh = false;
                    linePrinter.DoubleWide = false;
                    linePrinter.Bold = false;

                    linePrinter.Write("HallSt,Poblacion,SanJuan,Batangas" + "\n");
                    linePrinter.Write(" NON VAT Reg.TIN 001-774-488-000" + "\n");
                    linePrinter.Write("      Mobile#: 09199947756" + "\n\n");
                }
                else if (IsMVWD)
                {
                    linePrinter.Bold = true;
                    linePrinter.DoubleWide = true;
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("  METRO VIGAN" + "\n");
                    linePrinter.Write(" WATER DISTRICT" + "\n");
                    linePrinter.DoubleHigh = false;
                    linePrinter.DoubleWide = false;
                    linePrinter.Bold = false;

                    linePrinter.Write("Pantay Daya,Vigan City,Ilocos Sur" + "\n");
                    linePrinter.Write("   TIN 000-566-175-000 Non-VAT" + "\n");
                    linePrinter.Write("      Tel. No. 077-674-0870" + "\n\n");
                }

                linePrinter.Bold = true;
                //linePrinter.DoubleHigh = true;
                linePrinter.DoubleWide = true;
                string title1 = " BILLING NOTICE";
                int spaceleft = (printer_space - title1.Length) / 2;
                //title1 = title1.PadLeft(spaceleft + title1.Length);
                linePrinter.Write(title1 + "\n");
                //linePrinter.DoubleHigh = false;
                linePrinter.DoubleWide = false;
                linePrinter.Bold = false;

                string title2 = "For the month of " + printCurrentMonthYear;
                spaceleft = (printer_space - title2.Length) / 2;
                title2 = title2.PadLeft(spaceleft + title2.Length);
                linePrinter.Write(title2 + "\n");

                string title3 = "(" + printPrevMonth + " Consumption)";
                spaceleft = (printer_space - title3.Length) / 2;
                title3 = title3.PadLeft(spaceleft + title3.Length);
                linePrinter.Write(title3 + "\n");
                linePrinter.Write("\n");

                linePrinter.DoubleHigh = true;
                linePrinter.Write(consumer.consumer_name + "\n");
                linePrinter.DoubleHigh = false;

                linePrinter.Bold = true;
                linePrinter.Italic = true;
                linePrinter.Write(consumer.address + "\n");
                linePrinter.Bold = false;
                linePrinter.Italic = false;
                if (IsMVWD)
                {
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("Account No. : " + consumer.accountNo + "-" + consumer.consumer_type.ToString() + consumer.metersize.ToString() + "\n");
                    linePrinter.DoubleHigh = false;
                }
                else if (IsSJWD)
                {
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("Account No. : " + consumer.accountNo + "-" + consumer.consumer_type.ToString() + consumer.metersize.ToString() + "\n");
                    //linePrinter.Write("Account No. : " + consumer.accountNo.Substring(1,2).ToString  + consumer.accountNo.Substring(5, 1) + "-" + consumer.consumer_type.ToString() + consumer.metersize.ToString() + "-" + consumer.accountNo.Substring(6, 4)  + "\n");
                    linePrinter.DoubleHigh = false;
                }
                else
                {
                    linePrinter.DoubleHigh = true;
                    linePrinter.Write("Account No. : " + consumer.accountNo + "\n");
                    linePrinter.DoubleHigh = false;
                }


                if (IsSJWD)
                {
                    ////Get ConsumerType Description //SAMPLE LOOKUP ni MOA                    
                    string consumerTypeDescription = Database.Table<Billing_rates>()
                                                    .Where(b => b.consumer_type == consumer.consumer_type
                                                             && b.metersize == consumer.metersize)
                                                    .Select(b => b.constype_description)
                                                    .FirstOrDefault() ?? "";
                    if (!string.IsNullOrWhiteSpace(consumerTypeDescription))
                    {
                        linePrinter.Bold = true;
                        linePrinter.Italic = true;
                        linePrinter.Write($"{consumerTypeDescription.Trim()}\n");
                        linePrinter.Bold = false;
                        linePrinter.Italic = false;
                    }
                }

                linePrinter.Bold = true;
                linePrinter.Write("Meter No.   : " + consumer.meterno + "\n");
                linePrinter.Bold = false;

                if (IsSJWD)
                {
                    linePrinter.Bold = true;
                    linePrinter.Write("Seq. No.    : " + consumer.seqno + "\n");
                    linePrinter.Bold = false;
                }

                string print_dash = new string('-', printer_space);
                linePrinter.Write(print_dash + "\n");

                string newline = "Period From";
                spaceleft = printer_space - newline.Length;
                newline = newline + printPrevDate.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");

                newline = "Period To";
                spaceleft = printer_space - newline.Length;
                newline = newline + printCurrentDate.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");
                //linePrinter.Write(print_dash + "\n");

                linePrinter.DoubleHigh = true;
                newline = "Due Date";
                spaceleft = printer_space - newline.Length;
                newline = newline + printDueDate.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Write(print_dash + "\n");


                //Init Value
                decimal runningTotalBill = 0.00m;
                decimal penaltyAmount = 0.00m;
                decimal currentBill = 0.00m;
                decimal sewerageAmount = 0.00m;
                decimal esfFtax = 0.00m;

                currentBill = Convert.ToDecimal(consumer.currentbill);

                bool IsSewerage = consumer.parameters.Contains("IsSewerage") ? true : false;
                bool IsFlatRate = consumer.parameters.Contains("IsFlatRate") ? true : false;
                if (IsMVWD)
                {
                    if (IsSewerage)
                    {
                        sewerageAmount = Convert.ToDecimal(consumer.sewerage);
                        if (IsFlatRate)
                        {
                            currentBill = 0.00m;
                        }
                    }
                }
                else //IsCWD
                {
                    float sewerageRate = Database.Table<Billing_rates>()
                        .Where(b => b.consumer_type == consumer.consumer_type &&
                                    b.metersize == consumer.metersize)
                        .Select(b => b.sewerage)
                        .FirstOrDefault();

                    sewerageAmount = Convert.ToDecimal(sewerageRate);

                    if (double.IsNaN(Convert.ToDouble(sewerageAmount)) || sewerageAmount == 0)
                    {
                        sewerageAmount = 0.00m;
                        if (IsCWD)
                        {
                            sewerageAmount = 2.93m;
                        }
                    }


                    if (sewerageAmount > 0)
                    {
                        decimal usage = Convert.ToDecimal(consumer.usage);

                        sewerageAmount = sewerageAmount * usage;
                        sewerageAmount = Math.Round(sewerageAmount, 2);
                        esfFtax = sewerageAmount * 0.02m;
                        esfFtax = Math.Round(esfFtax, 2);

                        sewerageAmount = Math.Round(sewerageAmount + esfFtax, 2);
                    }

                }

                if (IsFlatRate == false || IsMVWD == false)
                {
                    string printPresReading = consumer.presreading.ToString();
                    newline = "Pres Reading"; //13
                    spaceleft = printer_space - newline.Length; //35-13 = 22
                    newline = newline + printPresReading.PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");

                    string printPrevReading = consumer.prevreading.ToString();
                    newline = "Prev Reading";
                    spaceleft = printer_space - newline.Length;
                    newline = newline + printPrevReading.PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");

                    string printUsage = consumer.usage.ToString();
                    newline = "Consumption";
                    spaceleft = printer_space - newline.Length;
                    newline = newline + printUsage.PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");
                    linePrinter.Write(print_dash + "\n"); //46 chars
                }

                if (currentBill > 0)
                {
                    runningTotalBill = runningTotalBill + currentBill;
                    string printCurrentBill = currentBill.ToString("#,##0.00");
                    newline = "Current Bill";
                    spaceleft = printer_space - newline.Length;
                    newline = newline + printCurrentBill.PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");
                }


                if (sewerageAmount > 0)
                {
                    if (IsMVWD)
                    {
                        newline = "Sewerage";
                    }
                    else
                    {
                        newline = "Septage Mgmt. Fee";
                    }
                    runningTotalBill = runningTotalBill + sewerageAmount;
                    string printSewerage = sewerageAmount.ToString("#,##0.00");
                    spaceleft = printer_space - newline.Length;
                    newline = newline + printSewerage.PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");
                }



                //Less Senior
                if (IsIndangWD)
                {
                    //DO Nothing
                }
                else
                {
                    isSenior = consumer.parameters.Contains("IsSenior") ? true : false;
                    if (isSenior && consumer.usage <= 30)
                    {
                        decimal seniorDiscount = 0.00m;
                        seniorDiscount = currentBill * 0.05m;
                        seniorDiscount = seniorDiscount * -1;
                        runningTotalBill = runningTotalBill + seniorDiscount;
                        newline = "Senior Discount";
                        string printseniorDiscount = seniorDiscount.ToString("#,##0.00");
                        spaceleft = printer_space - newline.Length;
                        newline = newline + printseniorDiscount.PadLeft(spaceleft);
                        linePrinter.Write(newline + "\n");
                    }
                }


                //Penalty Before Advance and Arrears
                penaltyAmount = runningTotalBill * Convert.ToDecimal(consumer.special_penalty_rate);


                decimal dArrears = Convert.ToDecimal(consumer.arrears);
                string printArrears = dArrears.ToString("#,##0.00");
                runningTotalBill = runningTotalBill + dArrears;
                if (consumer.arrears < 0)
                {
                    newline = "Advance";
                    penaltyAmount = runningTotalBill * Convert.ToDecimal(consumer.special_penalty_rate);
                }
                else
                {
                    newline = "Arrears";
                }
                spaceleft = printer_space - newline.Length;
                newline = newline + printArrears.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");


                decimal dOthers = Convert.ToDecimal(consumer.othercharges);
                if (dOthers > 0)
                {
                    string printOthers = dOthers.ToString("#,##0.00");
                    runningTotalBill = runningTotalBill + dOthers;
                    newline = "Others";
                    spaceleft = printer_space - newline.Length;
                    newline = newline + printOthers.PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");
                }


                linePrinter.DoubleHigh = true;
                string printTotalBill = runningTotalBill.ToString("#,##0.00");
                newline = "AMOUNT BEFORE DUE";
                spaceleft = printer_space - newline.Length;
                newline = newline + printTotalBill.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");
                linePrinter.DoubleHigh = false;


                linePrinter.Write(print_dash + "\n");
                linePrinter.Italic = true;
                //linePrinter.Write("* If no payment received after due date\n");
                linePrinter.Write("* If unpaid after Due Date.\n");
                linePrinter.Italic = false;


                if (penaltyAmount < 0 || consumer.isnopenalty == 1)
                {
                    penaltyAmount = 0.00m;
                }
                else
                {
                    if (IsMVWD)
                    {
                        penaltyAmount = Math.Round(penaltyAmount, 1);
                    }
                    else
                    {
                        penaltyAmount = Math.Round(penaltyAmount, 2);
                    }
                }

                newline = "Penalty";
                string printPenalty = penaltyAmount.ToString("#,##0.00");
                spaceleft = printer_space - newline.Length;
                newline = newline + printPenalty.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");
                //Add Penalty                                 
                runningTotalBill = runningTotalBill + penaltyAmount;

                linePrinter.DoubleHigh = true;
                newline = "AMOUNT AFTER DUE";
                string printTotalAfterDue = runningTotalBill.ToString("#,##0.00");
                spaceleft = printer_space - newline.Length;
                newline = newline + printTotalAfterDue.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");
                linePrinter.DoubleHigh = false;


                string print_doubledash = new string('=', printer_space);
                linePrinter.Write(print_doubledash + "\n");

                if (IsSJWD)
                {
                    if (runningTotalBill > 0.00m)
                    {
                        linePrinter.DoubleHigh = true;
                        string dueForDisconOn = "DUE FOR DISCONNECTION ON " + printDiscDate;
                        spaceleft = (printer_space - dueForDisconOn.Length) / 2;
                        dueForDisconOn = dueForDisconOn.PadLeft(spaceleft + dueForDisconOn.Length);
                        linePrinter.Write(dueForDisconOn + "\n");
                        linePrinter.DoubleHigh = false;
                        linePrinter.Write(print_dash + "\n");
                    }
                }
                else
                {
                    if (IsMVWD && (dArrears >= 215.5m))
                    {
                        linePrinter.DoubleHigh = true;
                        newline = "Discon Date";
                        spaceleft = printer_space - newline.Length;
                        newline = newline + printDiscDate.PadLeft(spaceleft);
                        linePrinter.Write(newline + "\n");
                        linePrinter.DoubleHigh = false;
                        linePrinter.Write(print_dash + "\n");
                    }

                    newline = "Reading Date";
                    spaceleft = printer_space - newline.Length;
                    newline = newline + currentbilldate.ToString("MM/dd/yyyy").PadLeft(spaceleft);
                    linePrinter.Write(newline + "\n");
                    linePrinter.Write(print_dash + "\n");
                }

                string printRemarks = consumer.remarks;
                if (string.IsNullOrWhiteSpace(printRemarks))
                {
                    printRemarks = "Normal Reading";
                }
                newline = "Remarks";
                spaceleft = printer_space - newline.Length;
                newline = newline + printRemarks.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");


                string printReader = consumer.billedby;
                newline = "Meter Reader";
                spaceleft = printer_space - newline.Length;
                newline = newline + printReader.PadLeft(spaceleft);
                linePrinter.Write(newline + "\n");


                linePrinter.Italic = true;
                string notice1 = "This is not valid as";
                spacesleft = (printer_space - notice1.Length) / 2;
                string print_notice1 = notice1.PadLeft(notice1.Length + spacesleft);
                linePrinter.Write(print_notice1 + "\n");
                string notice2 = "Official Receipt";
                spacesleft = (printer_space - notice2.Length) / 2;
                string print_notice2 = notice2.PadLeft(notice2.Length + spacesleft);
                linePrinter.Write(print_notice2 + "\n");
                string notice3 = "Please disregard arrears if";
                spacesleft = (printer_space - notice3.Length) / 2;
                string print_notice3 = notice3.PadLeft(notice3.Length + spacesleft);
                linePrinter.Write(print_notice3 + "\n");
                string notice4 = "payment has been made";
                spacesleft = (printer_space - notice4.Length) / 2;
                string print_notice4 = notice4.PadLeft(notice4.Length + spacesleft);
                linePrinter.Write(print_notice4 + "\n");
                string notice5 = "PAY YOUR BILLS ON TIME TO AVOID";
                spacesleft = (printer_space - notice5.Length) / 2;
                string print_notice5 = notice5.PadLeft(notice5.Length + spacesleft);
                linePrinter.Write(print_notice5 + "\n");
                string notice6 = "PENALTY";
                spacesleft = (printer_space - notice6.Length) / 2;
                string print_notice6 = notice6.PadLeft(notice6.Length + spacesleft);
                linePrinter.Write(print_notice6 + "\n");
                linePrinter.Italic = false;

                string strNotif = LoginUser.login_user.msgmo;
                if (!string.IsNullOrWhiteSpace(strNotif))
                {
                    linePrinter.Bold = true;
                    linePrinter.Write("REMINDER : " + strNotif + "\n");
                    linePrinter.Bold = false;
                }


                linePrinter.Write("\n");
                result = "Done printing.";

            }
            catch (Exception ex)
            {
                result = "DoPrint exception: " + ex.Message;
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



    public async Task<string> DoPrint(Consumers consumer)
    {
        string result = "";
        int spacesleft = 0;
        await Task.Run(() =>
        {

            LinePrinter linePrinter = null;

            try
            {

                ReadProFile();

                //prepare data to write
                DateTime billduedate = DateTime.Parse(consumer.duedate);
                DateTime prevbilldate = DateTime.Parse(consumer.prevreadingdate);
                DateTime currentbilldate = DateTime.Parse(consumer.billdate);

                string printDueDate = billduedate.ToString("MM/dd/yyyy");
                string printPrevDate = prevbilldate.ToString("MM/dd/yyyy");
                string printCurrentDate = currentbilldate.ToString("MM/dd/yyyy");

                string strBilldate = "Reading Date:";
                strBilldate = strBilldate.PadRight((46 - consumer.billdate.ToString().Length) - strBilldate.Length) + consumer.billdate.ToString();

                string strRemarks = "Remarks:";
                strRemarks = strRemarks.PadRight((46 - consumer.remarks.Length) - strRemarks.Length) + consumer.remarks;

                string strReader = "Meter Reader:";
                strReader = strReader.PadRight((46 - consumer.billedby.Length) - strReader.Length) + consumer.billedby;


                //PB21
                string profilename = "PB31";

                linePrinter = new LinePrinter(profiles, profilename, "bt://DC:1D:30:C8:88:5A");
                linePrinter.Connect();
                //if (null == logoBase64ImgStr)
                //{
                //    ReadImgToBase64String();
                //}

                ////Print barista_supply_logo_receipt.bmp logo
                //linePrinter.WriteGraphicBase64(logoBase64ImgStr, ImageRotation.DEGREE_0, 0, 0, 0);
                //string companyname = "MICRO LINE SYSTEMS";
                //int spacesleft = (46 - companyname.Length) / 2;
                //string print_companyname = companyname.PadLeft(companyname.Length + (spacesleft));
                //linePrinter.DoubleHigh = true;
                //linePrinter.Write(print_companyname + "\n");
                //linePrinter.DoubleHigh = false;
                //string tindetails = "TIN 000 000 000 000";
                //spacesleft = (46 - tindetails.Length) / 2;
                //string print_tindetails = tindetails.PadLeft(tindetails.Length + spacesleft);
                //linePrinter.Write(print_tindetails + "\n");
                //string telnos = "Tel. No. 000 000 1234";
                //spacesleft = (46 - telnos.Length) / 2;
                //string print_telnoss = telnos.PadLeft(telnos.Length + spacesleft);
                //linePrinter.Write(print_telnoss + "\n\n");
                linePrinter.Write("----------------------------------------------\n"); //46 chars
                linePrinter.DoubleHigh = true;
                linePrinter.Write("                BILLING NOTICE\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Write("          For the month of Nov 2025\n");
                linePrinter.Write("              (October Consumption)\n");
                linePrinter.Write("\n\n");
                linePrinter.DoubleHigh = true;
                linePrinter.Write(consumer.consumer_name + "\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Bold = true;
                linePrinter.Write("Address - Test Print\n");
                linePrinter.Bold = false;
                linePrinter.DoubleHigh = true;
                linePrinter.Write("Account No: " + consumer.accountNo + "\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Bold = true;
                linePrinter.Write("Meter No: " + consumer.meterno + "\n");
                linePrinter.Bold = false;
                linePrinter.Write("----------------------------------------------\n"); //46 chars  
                                                                                       //Pres Reading                              5000
                                                                                       //linePrinter.Write("Period From                           7/5/2024\n");
                                                                                       //linePrinter.Write("Period To                             7/5/2024\n");
                linePrinter.Write("Period From                          " + printPrevDate + "\n");
                linePrinter.Write("Period To                            " + printCurrentDate + "\n");

                linePrinter.DoubleHigh = true;
                //linePrinter.Write("Due Date:                           " + billduedate.ToString() + "\n");
                linePrinter.Write("Due Date:                            " + printDueDate + "\n");
                //linePrinter.Write(strbillduedate + "\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Write("----------------------------------------------\n"); //46 chars
                string printPresReading = consumer.presreading.ToString();
                spacesleft = 5 - printPresReading.Length;
                printPresReading = printPresReading.PadLeft(printPresReading.Length + spacesleft);
                string toprintPresReading = "Pres Reading                             " + printPresReading;
                linePrinter.Write(toprintPresReading + "\n");

                string printPrevReading = consumer.prevreading.ToString();
                spacesleft = 5 - printPrevReading.Length;
                printPrevReading = printPrevReading.PadLeft(printPrevReading.Length + spacesleft);
                string toprintPrevReading = "Prev Reading                             " + printPrevReading;
                linePrinter.Write(toprintPrevReading + "\n");
                //linePrinter.Write(strPresReading + "\n");
                //linePrinter.Write(strPrevReading + "\n");
                //linePrinter.Write(strUsage + "\n");
                string printUsage = consumer.usage.ToString();
                spacesleft = 5 - printUsage.Length;
                printUsage = printUsage.PadLeft(printUsage.Length + spacesleft);
                string toprintUsage = "Consumption                              " + printUsage;
                linePrinter.Write(toprintUsage + "\n");
                linePrinter.Write("----------------------------------------------\n"); //46 chars

                string printCurrentBill = consumer.currentbill.ToString("#,##0.00");
                spacesleft = 10 - printCurrentBill.Length;
                printCurrentBill = printCurrentBill.PadLeft(printCurrentBill.Length + spacesleft);
                string toprintCurrentBill = "Current Bill                        " + printCurrentBill;
                linePrinter.Write(toprintCurrentBill + "\n");
                //linePrinter.Write(strBill + "\n");
                //linePrinter.Write(strArrears + "\n");
                string printArrears = consumer.arrears.ToString("#,##0.00");
                spacesleft = 10 - printArrears.Length;
                printArrears = printArrears.PadLeft(printArrears.Length + spacesleft);
                string toprintArrears = "Arrears                             " + printArrears;
                linePrinter.Write(toprintArrears + "\n");
                linePrinter.DoubleHigh = true;
                string printtotalbill = consumer.total_bill.ToString("#,##0.00");
                spacesleft = 10 - printtotalbill.Length;
                printtotalbill = printtotalbill.PadLeft(printtotalbill.Length + spacesleft);
                string beforebill = "Amount Before Due:                  " + printtotalbill;
                linePrinter.Write(beforebill + "\n");
                //linePrinter.Write(strTotalBill + "\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Write("----------------------------------------------\n"); //46 chars                                 
                linePrinter.Write("* If no payment received after due date\n\n");
                linePrinter.Write("Penalty                                   0.00\n");
                linePrinter.DoubleHigh = true;
                string afterbill = "Amount After Due:                   " + printtotalbill;
                linePrinter.Write(afterbill + "\n");
                //linePrinter.Write("Amount After Due:                       294.80\n");
                //linePrinter.Write(strAfterDue + "\n");
                linePrinter.DoubleHigh = false;
                linePrinter.Write("----------------------------------------------\n"); //46 chars
                linePrinter.Write("----------------------------------------------\n"); //46 chars
                linePrinter.Bold = true;
                //linePrinter.Write("Reading Date:                         8/5/2024\n");
                linePrinter.Write(strBilldate + "\n");
                linePrinter.Bold = false;
                linePrinter.Write("----------------------------------------------\n"); //46 chars
                //linePrinter.Write("Remarks                         Normal Reading\n");
                linePrinter.Write(strRemarks + "\n");
                //linePrinter.Write("Meter Reader                             Marco\n");
                linePrinter.Write(strReader + "\n");

                string notice1 = "This is not valid as";
                spacesleft = (46 - notice1.Length) / 2;
                string print_notice1 = notice1.PadLeft(notice1.Length + spacesleft);
                linePrinter.Write(print_notice1 + "\n");
                string notice2 = "Official Receipt";
                spacesleft = (46 - notice2.Length) / 2;
                string print_notice2 = notice2.PadLeft(notice2.Length + spacesleft);
                linePrinter.Write(print_notice2 + "\n");
                string notice3 = "Please disregard arrears if";
                spacesleft = (46 - notice3.Length) / 2;
                string print_notice3 = notice3.PadLeft(notice3.Length + spacesleft);
                linePrinter.Write(print_notice3 + "\n");
                string notice4 = "payment has been made";
                spacesleft = (46 - notice4.Length) / 2;
                string print_notice4 = notice4.PadLeft(notice4.Length + spacesleft);
                linePrinter.Write(print_notice4 + "\n");
                string notice5 = "PAY YOUR BILLS ON TIME TO AVOID";
                spacesleft = (46 - notice5.Length) / 2;
                string print_notice5 = notice5.PadLeft(notice5.Length + spacesleft);
                linePrinter.Write(print_notice5 + "\n");
                string notice6 = "PENALTY";
                spacesleft = (46 - notice6.Length) / 2;
                string print_notice6 = notice6.PadLeft(notice6.Length + spacesleft);
                linePrinter.Write(print_notice6 + "\n");

                result = "Done printing.";
            }
            catch (Exception ex)
            {
                result = "DoPrint exception: " + ex.Message;
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

    private async void ReadImgToBase64String()
    {

        string sourcefilename = "Datafiles/msl_print.bmp";
        //Stream stream = assembly.GetManifestResourceStream(resourcePrefix + fileName);
        using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        byte[] imgByteArray = new byte[stream.Length];
        stream.Read(imgByteArray, 0, (int)stream.Length);
        logoBase64ImgStr = Convert.ToBase64String(imgByteArray);

    }

    private async void OnPrintBill(object sender, EventArgs e)
    {
        if (txtConsumerAcctNo.Text == string.Empty)
        {
            return;
        }
        bool toPrint = await DisplayAlertAsync("Confirm", "Print Selected Transaction?", "Ok", "Cancel");
        if (toPrint)
        {
            var result = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();
            if (result != null)
            {
                //MOA_For_Testing 
                //string output = await DoPrint(result);
                string output = await DoPrintBill(result);
                await DisplayAlertAsync("Print Notice", output, "Ok");
            }

        }
    }

    private async void OnCheckPrint(object sender, CheckedChangedEventArgs e)
    {
        if (chkPrintOn.IsChecked)
        {
            if (MyDeviceInfo.currentPrintConfig != null)
            {
                isPrintOn = true;
            }
            else
            {
                await DisplayAlertAsync("Print Notice", "No default printer loaded. Please check under Utilities.", "Ok");
            }
        }
        else
        {
            isPrintOn = false;
        }
    }



    #region newlogic

    private async void OnSaveReading(object sender, EventArgs e)
    {
        if (txtConsumerAcctNo.Text == string.Empty)
        {
            return;
        }


        if (decimal.TryParse(txtConsumerCurrentBill.Text, out decimal bill) && bill == 0)
        {
            return;
        }

        //if (decimal.TryParse(txtConsumerTotalBill.Text, out decimal bill) && bill == 0)
        //{
        //    return;
        //}

        string strRemarks = "";
        double present = Convert.ToDouble(txtConsumerPresReading.Text);
        double previous = Convert.ToDouble(txtConsumerPrevReading.Text);
        double usage = present - previous;

        bool isPresentLess = decimal.TryParse(txtConsumerPresReading.Text, out var pres) &&
                             decimal.TryParse(txtConsumerPrevReading.Text, out var prev) &&
                             pres < prev;
        if ((decimal.TryParse(txtConsumerUsage.Text, out decimal Usage) && Usage < 0) || (isPresentLess))
        {
            bool confirmUsage = await DisplayAlertAsync("NEGATIVE CONSUMPTION!", "Present reading is less than the Previous reading!\n\nDo you want to continue?", "Yes", "No");
            if (!confirmUsage)
            {
                return;
            }

            bool confirmUsage2x = await DisplayAlertAsync("NEGATIVE CONSUMPTION!", $"Present   = {present}\n" +
                                                                              $"Previous = {previous}\n" +
                                                                              $"----------------------\n" +
                                                                              $"Usage     = {usage}\n\n" +
                                                                              $"Are you sure you want to save NEGATIVE consumption?",
                                                     "Yes", "No");
            if (!confirmUsage2x)
            {
                return;
            }
        }
        else
        {
            strRemarks = UsageAnalysisHelper.GstrUsageAnalysisRemarks(usage, Fld2Dob(_averageCons));

            if (strRemarks != "")
            {
                bool continueEntry = await DisplayAlertAsync("VALIDATE CONSUMPTION!", $"Current usage is " + strRemarks + "!\n\n" +
                                                                                  $"Do you want to continue?",
                                                         "Yes", "No");

                if (!continueEntry)
                {
                    return;
                }
            }
        }




        //Confirm Saving of Transactions 
        bool confirm = await DisplayAlertAsync("Confirm Saving", "Save this transaction?", "Yes", "No");
        if (!confirm)
        {
            return;
        }


        Grid grid_child = new Grid();
        Grid grid_parent = new Grid();
        int selectedindex = pickerRemarks.SelectedIndex;
        string selectedRemarks = "";
        if (selectedindex != -1)
        {
            selectedRemarks = pickerRemarks.Items[selectedindex];
        }


        Consumers? consumer = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();

        if (consumer == null)
        {
            return;
        }

        float presReading = ParseFloat(txtConsumerPresReading.Text);

        consumer.presreading = presReading;
        if (strRemarks != "" && string.IsNullOrEmpty(selectedRemarks))
        {
            consumer.remarks = strRemarks;
        }
        else
        {
            consumer.remarks = selectedRemarks;
        }

        dbService = new DBService();
        bool isSaveSuccess = await dbService.ConsumerUpdateItemToDatabase(consumer);
        if (isSaveSuccess)
        {

            if (consumer != null)
            {

                string gridInnerAutoID = $"UInnerGrid_{consumer.accountID}";

                //Look for the grid and update
                foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerUnbilledCollection)
                {
                    if (_gridConsumer.AutomationId == gridInnerAutoID)
                    {
                        //_gridConsumer.AutomationId = $"BInnerGrid_{consumer.accountID}";

                        //remove the grid to unbilled collection
                        ConsumerRecords.gridConsumerUnbilledCollection.Remove(_gridConsumer);

                        //add it to billed collection
                        ConsumerRecords.gridConsumerBilledCollection.Add(_gridConsumer);
                        break;
                    }
                }
            }
            //lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.lstconsumers.Count.ToString();
            int selectedItem = pickerBillingFilter.SelectedIndex;
            if (selectedItem == 0)
            {

                lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else if (selectedItem == 1)
            {

                lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else
            {

                lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
            }
            //await DisplayAlertAsync("Notice", "Reading Saved!", "Ok");


            if (MyDeviceInfo.currentPrintConfig != null)
            {
                bool toPrint = false;
                if (isPrintOn)
                {
                    toPrint = true;
                    //string output = await DoPrintv2(consumer);
                    //await DisplayAlertAsync("Print Notice", output, "Ok");
                }
                else
                {
                    toPrint = await DisplayAlertAsync("Confirm", "Print Transaction?", "Yes", "No");
                }


                if (toPrint)
                {
                    string output = await DoPrintBill(consumer);
                    if (output != "Done printing.")
                    {
                        //Error printer. Remove default printer
                        MyDeviceInfo.currentPrintConfig = null;
                    }
                    //MOA_Removed
                    //await DisplayAlertAsync("Print Notice", "Printing will be disabled due to error: " + output + "\nTo enable printing again, Check Utilities.", "Ok");
                }


            }

            //Move to next seq no
            OnNextRecord(pickerBillingFilter.SelectedIndex);

        }
        else
        {
            await DisplayAlertAsync("Error", "Failed to saved ", "Ok");
        }
    }
    #endregion

    private void TapShowOPtions(object sender, TappedEventArgs e)
    {
        if (!showOptions)
        {
            gridOptions.IsVisible = true;


            showOptions = true;
            imgoption.Source = "menuicons/options.png";
        }
        else
        {
            gridOptions.IsVisible = false;


            showOptions = false;
            imgoption.Source = "menuicons/options1.png";
        }
    }

    private void OnRemarkTapped(object sender, TappedEventArgs e)
    {
        //pickerRemarks.SelectedIndex = prevIndex;
        txtRemarks.IsVisible = false;
        pickerRemarks.IsVisible = true;
        pickerRemarks.Focus();
    }

    private void OnRemarkChanged(object sender, EventArgs e)
    {
        int selectedindex = pickerRemarks.SelectedIndex;
        string selectedRemarks = "";
        if (selectedindex != -1)
        {
            selectedRemarks = pickerRemarks.Items[selectedindex];
            txtRemarks.Text = selectedRemarks;
        }
        prevIndex = selectedindex;
        txtRemarks.IsVisible = true;
        pickerRemarks.IsVisible = false;
        btnSave.Focus();
    }

    private void OnRemarkFocus(object sender, FocusEventArgs e)
    {
        txtRemarks.IsVisible = true;
        pickerRemarks.IsVisible = false;
    }

    private void OnRemarkUnFocus(object sender, FocusEventArgs e)
    {
        //txtRemarks.IsVisible = true;
        //pickerRemarks.IsVisible = false;
    }

    private void OnSaveFocus(object sender, FocusEventArgs e)
    {
        if (sender is Button btnclick)
        {
            btnclick.BackgroundColor = Colors.Yellow;
        }

    }

    private void OnSaveUnFocus(object sender, FocusEventArgs e)
    {
        if (sender is Button btnclick)
        {
            btnclick.BackgroundColor = Colors.White;
        }

    }

    public static class UsageAnalysisHelper
    {
        private const double AcceptedPercentage = 50; // Flag remarks if 50% below/above average

        public static string GstrUsageAnalysisRemarks(double dobCurrentUsage, double dobAverage)
        {
            double dobPerCent;

            if (dobCurrentUsage != 0 || dobAverage != 0)
            {
                if (dobCurrentUsage > dobAverage)
                {
                    dobPerCent = dobAverage == 0
                        ? dobCurrentUsage - dobAverage
                        : (dobCurrentUsage - dobAverage) / Math.Abs(dobAverage);
                }
                else
                {
                    dobPerCent = dobAverage == 0
                        ? dobAverage - dobCurrentUsage
                        : (dobAverage - dobCurrentUsage) / Math.Abs(dobAverage);
                }
            }
            else
            {
                dobPerCent = 0;
            }

            if (dobPerCent >= (AcceptedPercentage / 100))
            {
                double difference = dobAverage - dobCurrentUsage;

                if (difference > 0)
                    return Fld2Dob(dobPerCent * 100) + "% Below Average";
                else
                    return Fld2Dob(dobPerCent * 100) + "% Above Average";
            }

            return "";
        }
    }

}