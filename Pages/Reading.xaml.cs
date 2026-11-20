
using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using SQLite;
using System.Globalization;
using System.Text.RegularExpressions;

//using System.Collections.Generic;
//using System.IO;
//using System.Threading.Tasks;
//using SkiaSharp;
#if ANDROID
using eMRBCS.Platforms.Android;
#endif

namespace eMRBCS.Pages;

public sealed record SearchResultItem(int Index, string Text);
public partial class Reading : ContentPage
{
    private static string ListTitle(int idx) => idx switch
    {
        0 => "Unbilled Consumer",
        1 => "Billed Consumer",
        _ => "Collected Consumer"
    };
    private enum RoundingRule { Truncate, Nearest }
    int selected_field = 0; //0 - Pres Reading, 1 - Acct No, 2 - Meter No, 3 - Name

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
            Database = new SQLiteConnection(dbPath) { BusyTimeout = TimeSpan.FromSeconds(15) };
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
    bool isFilterChange = false;
    bool isRecompute = false;
    bool isMinus = false;
    bool showOptions = false;

    bool isFlatRate = false;
    bool isSewerage = false;
    bool isSenior = false;
    bool isZeoMin = false;

    bool isPrintOn = false;
    bool _searchPromptOpen = false;   // blocks hardware-key handling while the name prompt is open
    string profiles;

    int prevIndex = 0;

    private static string logoBase64ImgStr = null;
    private float _averageCons;

    // =========================================================================
    // NEW: result container for the print-time bill computation
    // =========================================================================
    private sealed class BillAmounts
    {
        public decimal CurrentBill { get; set; }
        public decimal SewerageAmount { get; set; }
        public decimal MaintenanceFee { get; set; }
        public decimal SeniorDiscount { get; set; }
        public decimal Arrears { get; set; }
        public decimal Others { get; set; }
        public decimal BeforeDueTotal { get; set; }
        public decimal PenaltyAmount { get; set; }
        public decimal AfterDueTotal { get; set; }

        public bool IsSewerage { get; set; }
        public bool IsFlatRate { get; set; }
        public bool ShowSeniorDiscount { get; set; }

    }

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
    // ---------- numeric pad ----------
    private void AppendDigit(char d)
    {
        string t = txtCustomEntry.Text ?? "";
        txtCustomEntry.Text = (t == "0" && selected_field == 0) ? d.ToString() : t + d;
    }

    private void OnDigitClicked(object? sender, EventArgs e)
    {
        if (sender is Button b && b.Text?.Length == 1) AppendDigit(b.Text[0]);
    }

    // OnClickOk: cases 1 and 2 were identical
    private async void OnClickOk(object sender, EventArgs e)
    {
        gridNumericPad.IsVisible = false;
        gridDetails.IsEnabled = true;
        if (string.IsNullOrEmpty(txtCustomEntry.Text)) return;

        if (selected_field == 0)
        {
            txtConsumerPresReading.Text = ParseFloat(txtCustomEntry.Text).ToString();
            await RecomputeCurrentAsync(force: true);
        }
        else
        {
            OnFilterNameSearch(txtCustomEntry.Text);   // account no / meter no
        }
        btnSave.Focus();
    }

    // ---------- hardware keys ----------
#if ANDROID
    private static readonly Dictionary<string, char> DigitKeys = new()
    {
        ["Num0"] = '0',
        ["F10"] = '0',
        ["Num1"] = '1',
        ["F1"] = '1',
        ["Num2"] = '2',
        ["F2"] = '2',
        ["Num3"] = '3',
        ["F3"] = '3',
        ["Num4"] = '4',
        ["F4"] = '4',
        ["Num5"] = '5',
        ["F5"] = '5',
        ["Num6"] = '6',
        ["F6"] = '6',
        ["Num7"] = '7',
        ["F7"] = '7',
        ["Num8"] = '8',
        ["F8"] = '8',
        ["Num9"] = '9',
        ["F9"] = '9'
    };

    private void OnHardwareKey(object sender, Android.Views.Keycode keyCode)
    {
        if (_searchPromptOpen) return;
        string key = keyCode.ToString();

        if (gridNumericPad.IsVisible)
        {
            if (DigitKeys.TryGetValue(key, out char d)) AppendDigit(d);
            else if (key == "Del") OnClickBackSpace(btnDel, EventArgs.Empty);
            else if (key == "Enter") OnClickOk(btnok, EventArgs.Empty);
            btnok.Focus();
            return;
        }

        switch (key)
        {
            case "VideoApp5": OnPresReadingTapped(txtConsumerPresReading, EventArgs.Empty); break;
            case "VideoApp6": OnMeterNoTapped(txtConsumerMeterNo, EventArgs.Empty); break;
            case "DpadLeft": OnPrevRecordTapped(btnPrevRecord, EventArgs.Empty); break;
            case "DpadRight": OnNextRecordTapped(btnNextRecord, EventArgs.Empty); break;
            case "DpadUp": OnFirstRecordTapped(btnStartRecord, EventArgs.Empty); break;
            case "DpadDown": OnLastRecordTapped(btnLastRecord, EventArgs.Empty); break;
            case "Enter": OnSaveReading(btnSave, EventArgs.Empty); break;
        }
    }
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(100);
#if ANDROID
        MessagingCenter.Unsubscribe<object, Android.Views.Keycode>(this, "KeyDown");
        MessagingCenter.Subscribe<object, Android.Views.Keycode>(this, "KeyDown", OnHardwareKey);
#endif
        OnConsumerLoad(ConsumerRecords.acctNo);
        btnSave.Focus();
    }

    // ---------- shared helpers ----------
    private void UpdateSummaryCount()
    {
        int total = ConsumerRecords.noofrecords_import;
        int billed = ConsumerRecords.noofrecordsBilled;
        lblSummaryCount.Text = pickerBillingFilter.SelectedIndex switch
        {
            0 => $"{total - billed} / {total}",
            1 => $"{billed} / {total}",
            _ => $"0 / {total}"
        };
    }
    private static string FormatQty(float v)
    {
        double r = Math.Round(v, 2);
        return r == Math.Floor(r)
            ? ((long)r).ToString(CultureInfo.InvariantCulture)
            : r.ToString("0.##", CultureInfo.InvariantCulture);
    }
    private void ClearConsumerDetails()
    {
        lblRecordNo.Text = "SeqNo: 0";
        selected_idno = 0;
        selected_seqno = 0;
        txtConsumerAcctNo.Text = "";
        lblTypeSize.Text = "";
        lblPrevAcctNo.IsVisible = false;
        txtConsumerPrevAcctNo.IsVisible = false;
        txtConsumerPrevAcctNo.Text = "";
        txtConsumerName.Text = "";
        txtConsumerMeterNo.Text = "";
        txtConsumerStatus.Text = "";
        txtConsumerAddress.Text = "";
        txtConsumerPrevReading.Text = "0";
        txtConsumerAverage.Text = "0";
        txtConsumerArrears.Text = "0.00";
        txtConsumerUsage.Text = " ";
        txtConsumerCurrentBill.Text = "0.00";
        txtConsumerPresReading.Text = " ";
        txtConsumerTotalBill.Text = "0.00";
        pickerRemarks.SelectedIndex = -1;
    }
    private void ScreenAdjustment() => ApplyScreenScale();
    private const double TextBoost = 1.10;

    private void ApplyScreenScale()
    {
        double w = MyDeviceInfo.device_width;    // dp
        double h = MyDeviceInfo.device_height;   // dp
        if (w <= 0 || h <= 0) return;

        // designed for 320 x 533 dp (Honeywell EDA51K)
        double raw = Math.Min(w / 320.0, h / 533.0);

        // text / control size factor (unchanged behaviour)
        double k = Math.Clamp(raw, 0.85, 1.35) * TextBoost;

        // spacing factor: 1.0 on small screens (spacing stays exactly as you like it),
        // grows faster than the text on large screens so nothing looks tight
        double rl = Math.Clamp(raw, 0.85, 1.8);
        double g = rl <= 1.0 ? 1.0 : rl * rl;

        void Set(string key, double baseValue) => Resources[key] = Math.Round(baseValue * k, 1);
        double S(double baseValue) => Math.Round(baseValue * g, 1);

        // ---- sizes (DynamicResource in XAML) ----
        Set("FsCaption", 11);
        Set("FsBody", 13);
        Set("FsValue", 16);
        Set("FsTotal", 20);
        Set("FsPad", 22);
        Set("FieldH", 32);
        Set("PadH", 44);
        Set("CmdH", 42);

        // NEW: top bar, seq no, navigation buttons, options icon
        Set("FsTop", 16);
        Set("FsTitle", 13);
        Set("FsSeq", 13);
        Set("FsPadMode", 14);
        Set("NavIcon", 30);
        Set("OptIcon", 32);
        Set("TopH", 40);

        // ---- spacing / padding / margin ----
        double capCol = Math.Round(66 * k, 1);          // caption column grows with the caption font

        topBar.Padding = new Thickness(S(10), S(3));
        topBar.ColumnSpacing = S(5);

        detailsStack.Spacing = S(3);
        detailsStack.Padding = new Thickness(0, S(3), 0, 0);
        seqGrid.Margin = new Thickness(S(3), 0);
        navButtons.Spacing = S(10);

        identityGrid.Margin = new Thickness(S(3), 0);
        identityGrid.RowSpacing = S(2);
        identityGrid.ColumnSpacing = S(2);
        identityGrid.ColumnDefinitions[0].Width = new GridLength(capCol);

        bandBorder.Padding = new Thickness(S(3));
        bandStack.Spacing = S(3);

        totalGrid.ColumnSpacing = S(3);
        totalGrid.ColumnDefinitions[0].Width = new GridLength(capCol);
        remarksGrid.ColumnSpacing = S(3);
        remarksGrid.ColumnDefinitions[0].Width = new GridLength(capCol);

        gridDetails.RowSpacing = S(3);
        gridDetails.Padding = new Thickness(S(3));
        gridCommands.ColumnSpacing = S(5);
    }

    //    protected override async void OnAppearing()
    //    {
    //        base.OnAppearing();
    //        await Task.Delay(100);
    //#if ANDROID
    //        MessagingCenter.Subscribe<object, Android.Views.Keycode>(this, "KeyDown", (sender, keyCode) =>
    //        {
    //            if (_searchPromptOpen) return;   // <-- ADD THIS LINE
    //            //DisplayAlertAsync("Print Notice", $"Key Pressed: {keyCode}", "Ok");
    //            //switch (keyCode.ToString())
    //            //{
    //            //    case "VideoApp5":
    //            //        OnPresReadingTapped(txtConsumerPresReading, EventArgs.Empty);                    
    //            //        break;
    //            //    case "Num01":
    //            //        txtCustomEntry.Focus();
    //            //        txtCustomEntry.Text = txtCustomEntry.Text == "0" && selected_field == 0 ? "1" : txtCustomEntry.Text + "1";
    //            //        break;
    //            //    case "Num02":
    //            //        DisplayAlertAsync("Print Notice", $"Key Pressed: {keyCode.ToString()}", "Ok");
    //            //        //OnClickB2(btnNumber2, EventArgs.Empty);
    //            //        break;
    //            //}
    //            //btnok.Focus();

    //            if (gridNumericPad.IsVisible)
    //            {
    //                switch (keyCode.ToString())
    //                {
    //                    case "Num1" or "F1":
    //                        OnClickB1(btnNumber1, EventArgs.Empty);
    //                        break;
    //                    case "Num2" or "F2":
    //                        OnClickB2(btnNumber2, EventArgs.Empty);
    //                        break;
    //                    case "Num3" or "F3":
    //                        OnClickB3(btnNumber3, EventArgs.Empty);
    //                        break;
    //                    case "Num4" or "F4":
    //                        OnClickB4(btnNumber4, EventArgs.Empty);
    //                        break;
    //                    case "Num5" or "F5":
    //                        OnClickB5(btnNumber5, EventArgs.Empty);
    //                        break;
    //                    case "Num6" or "F6":
    //                        OnClickB6(btnNumber6, EventArgs.Empty);
    //                        break;
    //                    case "Num7" or "F7":
    //                        OnClickB7(btnNumber7, EventArgs.Empty);
    //                        break;
    //                    case "Num8" or "F8":
    //                        OnClickB8(btnNumber8, EventArgs.Empty);
    //                        break;
    //                    case "Num9" or "F9":
    //                        OnClickB9(btnNumber9, EventArgs.Empty);
    //                        break;
    //                    case "Num0" or "F10":
    //                        OnClickB0(btnNumber0, EventArgs.Empty);
    //                        break;
    //                    case "Del":
    //                        OnClickBackSpace(btnDel, EventArgs.Empty);
    //                        break;
    //                    case "Enter":
    //                        OnClickOk(btnok, EventArgs.Empty);
    //                        break;
    //                }
    //                btnok.Focus();
    //            }
    //            else
    //            {
    //                switch (keyCode.ToString())
    //                {
    //                    case "VideoApp5":
    //                        OnPresReadingTapped(txtConsumerPresReading, EventArgs.Empty);
    //                        break;
    //                    case "VideoApp6":
    //                        OnMeterNoTapped(txtConsumerMeterNo, EventArgs.Empty);
    //                        break;
    //                    case "DpadLeft":
    //                        OnPrevRecordTapped(btnPrevRecord, EventArgs.Empty);
    //                        break;
    //                    case "DpadRight":
    //                        OnNextRecordTapped(btnNextRecord, EventArgs.Empty);
    //                        break;
    //                    case "DpadUp":
    //                        OnFirstRecordTapped(btnStartRecord, EventArgs.Empty);
    //                        break;
    //                    case "DpadDown":
    //                        OnLastRecordTapped(btnLastRecord, EventArgs.Empty);
    //                        break;
    //                    case "Enter":
    //                        OnSaveReading(btnSave, EventArgs.Empty);
    //                        break;
    //                }
    //                //DisplayAlertAsync("Print Notice", $"Key Pressed: {keyCode.ToString()}", "Ok");
    //            }


    //        });
    //#endif
    //        OnConsumerLoad(ConsumerRecords.acctNo);
    //        btnSave.Focus();
    //    }


    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        MessagingCenter.Unsubscribe<object, Android.Views.Keycode>(this, "KeyDown");
#endif
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
    private async void OnNameTapped(object sender, EventArgs e)
    {
        selected_field = 3;
        _searchPromptOpen = true;
        string? term;
        try
        {
            term = await DisplayPromptAsync(
                "Search by Name",
                ListTitle(pickerBillingFilter.SelectedIndex) + "\nEnter at least 3 characters of the consumer name:",
                accept: "Search",
                cancel: "Cancel",
                maxLength: 50,
                keyboard: Keyboard.Text);
        }
        finally
        {
            _searchPromptOpen = false;
        }

        if (string.IsNullOrWhiteSpace(term)) return;

        term = term.Trim();
        if (term.Length < 3)
        {
            await DisplayAlertAsync("Notice", "Please enter at least 3 characters.", "Ok");
            return;
        }

        OnFilterNameSearch(term);
    }
    private async void OnConsumerLoad(string parameter)
    {
        // ConsumerRecords.acctNo = parameter;
        var result = ConsumerStore.Instance.FindByAccountNo(parameter);
        if (result != null)
        {
            OnLoadConsumerDetails(result);
        }
        else
        {
            await DisplayAlertAsync("Warning!", "Account Number " + parameter + " not found. Please try again.", "Ok");
        }
    }
    // ===== Search results overlay (with separators) =====
    private TaskCompletionSource<int>? _pickTcs;

    private async Task<int> PickSearchResultAsync(string mode, string subtitle, string[] labels)
    {
        lblSearchMode.Text = mode.ToUpper();
        lblSearchSubtitle.Text = subtitle;
        lstSearchResults.ItemsSource = labels.Select((t, i) => new SearchResultItem(i, t)).ToList();

        _pickTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _searchPromptOpen = true;                 // block hardware keys while the list is open
        gridSearchResults.IsVisible = true;
        try { return await _pickTcs.Task; }
        finally
        {
            gridSearchResults.IsVisible = false;
            lstSearchResults.ItemsSource = null;
            _searchPromptOpen = false;
        }
    }

    private void OnSearchResultTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SearchResultItem item)
            _pickTcs?.TrySetResult(item.Index);
    }

    private void OnSearchResultsCancel(object? sender, EventArgs e) => _pickTcs?.TrySetResult(-1);

    private void SetPadMode(string text, string placeholder)
    {
        lblPadMode.Text = text;
        txtCustomEntry.Placeholder = placeholder;
    }
    private const int MaxPickerItems = 30;
    private async void OnFilterNameSearch(string parameter)
    {
        string term = (parameter ?? "").Trim();
        if (term.Length == 0) return;

        var source = ConsumerRecords.lstconsumers.Where(c => c.status == pickerBillingFilter.SelectedIndex);
        var cmp = StringComparer.OrdinalIgnoreCase;
        string searchName;
        List<Consumers> matches;
        Func<Consumers, string> label;

        if (selected_field == 1)            // account no -> sorted by account no
        {
            searchName = "Account No: ";
            matches = source.Where(c => (c.accountNo ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(c => c.accountNo, cmp).ToList();
            label = c => $"{c.accountNo} - {(c.consumer_name ?? "").Trim()}";
        }
        else if (selected_field == 2)       // meter no -> sorted by meter no
        {
            searchName = "Meter No: ";
            matches = source.Where(c => (c.meterno ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(c => c.meterno, cmp).ThenBy(c => c.accountNo, cmp).ToList();
            label = c => $"{c.meterno} - {(c.consumer_name ?? "").Trim()} ({c.accountNo})";
        }
        else                                // name -> sorted alphabetically by name
        {
            searchName = "Name: ";
            var tokens = term.Split(new[] { ' ', ',', '.' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            matches = source.Where(c => !string.IsNullOrEmpty(c.consumer_name) &&
                                        tokens.All(t => c.consumer_name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                            .OrderBy(c => c.consumer_name.Trim(), cmp).ThenBy(c => c.accountNo, cmp).ToList();
            label = c => $"{c.consumer_name.Trim()} ({c.accountNo})";
        }

        Consumers? result = null;
        if (matches.Count == 1)
        {
            result = matches[0];
        }
        else if (matches.Count > 1)
        {
            var shown = matches.Take(MaxPickerItems).ToList();
            var labels = shown.Select(label).ToArray();
            string title = matches.Count > MaxPickerItems
                ? $"{matches.Count} matches (showing first {MaxPickerItems}). Type more to narrow down."
                : $"{matches.Count} matches found";

            string modeText = selected_field == 1 ? "Account No" : selected_field == 2 ? "Meter No" : "Name";
            int idx = await PickSearchResultAsync($"Search by {modeText}\n{ListTitle(pickerBillingFilter.SelectedIndex)}", title, labels);
            if (idx < 0) return;
            result = shown[idx];
        }

        if (result != null) OnLoadConsumerDetails(result);
        else await DisplayAlertAsync("Warning!", searchName + "that contains " + term + " not found. Please try again.", "Ok");

        gridDetails.IsVisible = true;
    }
    private void OnNextRecordTapped(object sender, EventArgs e) => OnNextRecord(pickerBillingFilter.SelectedIndex);

    private void OnPrevRecordTapped(object sender, EventArgs e) => OnPrevRecord(pickerBillingFilter.SelectedIndex);

    private void OnLastRecordTapped(object sender, EventArgs e) => OnFirstLastRecord(0, pickerBillingFilter.SelectedIndex);
    private List<Consumers> Ordered(int status)
    {
        var src = ConsumerRecords.lstconsumers.Where(c => c.status == status);

        if (status == 1)   // billed: newest first (stack order)
            return src.Select(c => (c, d: MOA.ParseBillDate(c.billdate)))
                      .OrderByDescending(x => x.d)
                      .ThenByDescending(x => x.c.accountID)
                      .Select(x => x.c)
                      .ToList();

        return src.OrderBy(c => c.seqno).ThenBy(c => c.accountID).ToList();
    }
    private async void OnNextRecord(int isBilled)
    {
        var list = Ordered(isBilled);
        Consumers? result;

        if (isBilled == 1)   // billed: next = one step down the newest-first list
        {
            int i = list.FindIndex(c => c.accountID == selected_idno);
            result = i < 0 ? list.FirstOrDefault() : (i + 1 < list.Count ? list[i + 1] : null);
        }
        else
        {
            result = list.FirstOrDefault(c =>
                c.seqno > selected_seqno || (c.seqno == selected_seqno && c.accountID > selected_idno));
        }

        if (result != null) OnLoadConsumerDetails(result);
        else await DisplayAlertAsync("Notice", "No record(s) available to display.", "Ok");
    }

    private async void OnPrevRecord(int isBilled)
    {
        var list = Ordered(isBilled);
        Consumers? result;

        if (isBilled == 1)   // billed: prev = one step up (newer)
        {
            int i = list.FindIndex(c => c.accountID == selected_idno);
            result = i > 0 ? list[i - 1] : null;
        }
        else
        {
            result = list.LastOrDefault(c =>
                c.seqno < selected_seqno || (c.seqno == selected_seqno && c.accountID < selected_idno));
        }

        if (result != null) OnLoadConsumerDetails(result);
        else await DisplayAlertAsync("Notice", "No record(s) available to display.", "Ok");
    }

    private async void OnFirstLastRecord(int isFirst, int isBilled)
    {
        var list = Ordered(isBilled);
        Consumers? result = isFirst == 1 ? list.FirstOrDefault() : list.LastOrDefault();

        if (result != null)
        {
            isFilterChange = false;
            OnLoadConsumerDetails(result);
        }
        else
        {
            if (isFilterChange) { ClearConsumerDetails(); isFilterChange = false; }
            await DisplayAlertAsync("Notice", "No record(s) available to display.", "Ok");
        }
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
            if (consumer == null) return;

            lblRecordNo.Text = "SeqNo: " + consumer.seqno;
            selected_idno = consumer.accountID;
            selected_seqno = consumer.seqno;

            txtConsumerAcctNo.Text = consumer.accountNo;
            lblTypeSize.Text = $"{consumer.consumer_type}{consumer.metersize}";

            // Prev. Acct No: only visible when there is a value
            string prevAcct = (consumer.prev_account_no ?? "").Trim();
            txtConsumerPrevAcctNo.Text = prevAcct;
            bool hasPrevAcct = prevAcct.Length > 0;
            lblPrevAcctNo.IsVisible = hasPrevAcct;
            txtConsumerPrevAcctNo.IsVisible = hasPrevAcct;

            txtConsumerName.Text = consumer.consumer_name;
            txtConsumerMeterNo.Text = consumer.meterno;
            _averageCons = consumer.average_usage;

            txtConsumerStatus.Text = (consumer.consumer_status ?? "").Trim().ToUpper() == "A" ? "Active" : "Inactive";
            txtConsumerAddress.Text = MOA.Left((consumer.address ?? "").Trim(), 42);
            txtConsumerAverage.Text = FormatQty(consumer.average_usage);

            bool isSaved = consumer.status != 0;     // unsaved consumers must not show leftover values

            txtConsumerPrevReading.Text = consumer.prevreading.ToString();
            txtConsumerUsage.Text = isSaved ? FormatQty(consumer.usage) : " ";
            txtConsumerCurrentBill.Text = isSaved ? consumer.currentbill.ToString("#,##0.00") : "0.00";

            lblArrears.Text = consumer.arrears < 0 ? "Advance:" : "Arrears:";
            txtConsumerArrears.Text = consumer.arrears.ToString("0.00");
            txtConsumerOthers.Text = consumer.othercharges.ToString("0.00");

            txtConsumerPresReading.Text = " ";
            txtConsumerTotalBill.Text = "0.00";
            pickerRemarks.SelectedIndex = -1;
            txtRemarks.Text = isSaved && !string.IsNullOrWhiteSpace(consumer.remarks)
                                ? consumer.remarks
                                : "Normal Reading";

            if (isSaved && consumer.presreading > 0)
                txtConsumerPresReading.Text = consumer.presreading.ToString();
            if (isSaved && consumer.total_bill > 0)
                txtConsumerTotalBill.Text = consumer.total_bill.ToString("#,##0.00");

            _currentIsLessUsage = consumer.less_usage;
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
        ////await Navigation.PopAsync();
        ////restruct list before going back to consumer list
        //ConsumerRecords.AutoID_UIndex = new List<string>();
        //ConsumerRecords.AutoID_BIndex = new List<string>();

        //int ctrx = 0;
        //if (ConsumerRecords.gridConsumerUnbilledCollection.Count > 0)
        //{
        //    foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerUnbilledCollection)
        //    {
        //        string gridautoID = _gridConsumer.AutomationId;
        //        ConsumerRecords.AutoID_UIndex.Add(gridautoID + "," + ctrx);
        //        ctrx++;

        //    }
        //}

        //ctrx = 0;
        //if (ConsumerRecords.gridConsumerBilledCollection.Count > 0)
        //{
        //    foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerBilledCollection)
        //    {
        //        string gridautoID = _gridConsumer.AutomationId;
        //        ConsumerRecords.AutoID_BIndex.Add(gridautoID + "," + ctrx);
        //        ctrx++;
        //    }
        //}

        await Navigation.PopOnceAsync();
        //await Navigation.PopAsync();
    }



    //private async void OnSaveDetails_new(object sender, EventArgs e)
    //{
    //    if (txtConsumerAcctNo.Text == string.Empty)
    //    {
    //        return;
    //    }
    //    bool confirm = await DisplayAlertAsync("Confirm", "Are you sure you want to save?", "Yes", "No");
    //    if (!confirm)
    //    {
    //        return;
    //    }


    //    Grid grid_child = new Grid();
    //    Grid grid_parent = new Grid();
    //    //int selectedindex = pickerRemarks.SelectedIndex;
    //    string selectedRemarks = "";
    //    //if (selectedindex != -1)
    //    //{
    //    //    selectedRemarks = pickerRemarks.Items[selectedindex];
    //    //}
    //    selectedRemarks = txtRemarks.Text;


    //    Consumers? consumer = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();

    //    if (consumer == null)
    //    {
    //        return;
    //    }

    //    float presReading = ParseFloat(txtConsumerPresReading.Text);

    //    consumer.presreading = presReading;
    //    consumer.remarks = selectedRemarks;
    //    dbService = new DBService();
    //    bool isSaveSuccess = await dbService.ConsumerUpdateItemToDatabase(consumer);
    //    if (isSaveSuccess)
    //    {

    //        if (consumer != null)
    //        {

    //            var _gridBoxU = ConsumerRecords.gridConsumerUnbilledCollection;

    //            int batchctr = consumer.accountID > 10 ? 0 : consumer.accountID / 10;
    //            //batchctr = batchctr;
    //            string gridAutoID = "UGrid_" + batchctr;

    //            string gridInnerAutoID = $"UInnerGrid_{selected_seqno}";


    //            //Look for the grid and update
    //            foreach (Grid _gridConsumer in _gridBoxU)
    //            {
    //                if (_gridConsumer.AutomationId == gridAutoID)
    //                {
    //                    grid_parent = _gridConsumer;
    //                    foreach (Grid _gridInner in _gridConsumer.Children)
    //                    {
    //                        if (_gridInner.AutomationId == gridInnerAutoID)
    //                        {
    //                            //_gridInner.IsVisible = false;
    //                            grid_child = _gridInner;
    //                            break;
    //                        }
    //                    }
    //                    break;
    //                }
    //            }

    //            //Add it to Billed Collections
    //            int maxbatchbilled = ConsumerRecords.gridBilled_rowindex;
    //            if (maxbatchbilled > 0)
    //            {
    //                maxbatchbilled++;
    //            }

    //            grid_parent.Children.Remove(grid_child);

    //            List<Grid> _gridBoxB = new List<Grid>();

    //            if (ConsumerRecords.gridConsumerBilledCollection != null)
    //            {
    //                _gridBoxB = ConsumerRecords.gridConsumerBilledCollection;
    //            }


    //            if (_gridBoxB == null)
    //            {
    //                Grid tmpgridConsumerBilled = new Grid
    //                {
    //                    AutomationId = $"BGrid_{maxbatchbilled}",
    //                    RowDefinitions =
    //                        {
    //                            new RowDefinition { Height = GridLength.Auto }
    //                        },
    //                    ColumnDefinitions =
    //                        {
    //                            new ColumnDefinition { Width = GridLength.Star }
    //                        }

    //                };
    //                tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //                tmpgridConsumerBilled.Add(grid_child, 0, maxbatchbilled);
    //                _gridBoxB.Add(tmpgridConsumerBilled);
    //                ConsumerRecords.gridConsumerBilledCollection = _gridBoxB;
    //            }
    //            else
    //            {
    //                if (_gridBoxB.Count == 0)
    //                {
    //                    Grid tmpgridConsumerBilled = new Grid
    //                    {
    //                        AutomationId = $"BGrid_{maxbatchbilled}",
    //                        RowDefinitions =
    //                        {
    //                            new RowDefinition { Height = GridLength.Auto }
    //                        },
    //                        ColumnDefinitions =
    //                        {
    //                            new ColumnDefinition { Width = GridLength.Star }
    //                        }

    //                    };
    //                    tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //                    tmpgridConsumerBilled.Add(grid_child, 0, maxbatchbilled);
    //                    _gridBoxB.Add(tmpgridConsumerBilled);
    //                }
    //                else
    //                {
    //                    foreach (Grid _gridchild in _gridBoxB)
    //                    {
    //                        if (_gridchild.AutomationId == "BGrid_" + maxbatchbilled)
    //                        {
    //                            int maxrow = _gridchild.RowDefinitions.Count;
    //                            _gridchild.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //                            _gridchild.Add(grid_child, 0, maxrow + 1);
    //                            break;
    //                        }
    //                    }
    //                }

    //            }


    //        }
    //        //lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.lstconsumers.Count.ToString();
    //        int selectedItem = pickerBillingFilter.SelectedIndex;
    //        if (selectedItem == 0)
    //        {

    //            lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
    //        }
    //        else if (selectedItem == 1)
    //        {
    //            lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
    //        }
    //        else
    //        {

    //            lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
    //        }
    //        //await DisplayAlertAsync("Notice", "Reading Saved!", "Ok");

    //        if (MyDeviceInfo.currentPrintConfig != null)
    //        {
    //            bool toPrint = false;
    //            if (isPrintOn)
    //            {
    //                toPrint = true;
    //                //string output = await DoPrintv2(consumer);
    //                //await DisplayAlertAsync("Print Notice", output, "Ok");
    //            }
    //            else
    //            {
    //                toPrint = await DisplayAlertAsync("Confirm", "Print Transaction?", "Yes", "No");
    //            }

    //            if (toPrint)
    //            {
    //                string output = await DoPrintBill(consumer);
    //                await DisplayAlertAsync("Print Notice", output, "Ok");
    //            }


    //        }

    //        //Move to next seq no
    //        OnNextRecord(pickerBillingFilter.SelectedIndex);

    //    }
    //    else
    //    {
    //        await DisplayAlertAsync("Error", "Failed to saved ", "Ok");
    //    }
    //}

    //private async void OnSaveDetails(object sender, EventArgs e)
    //{
    //    if (txtConsumerAcctNo.Text == string.Empty)
    //    {
    //        return;
    //    }
    //    bool confirm = await DisplayAlertAsync("Confirm", "Are you sure you want to save?", "Yes", "No");
    //    if (!confirm)
    //    {
    //        return;
    //    }

    //    AbsoluteLayout absoluteLayout_remove = new AbsoluteLayout();
    //    Grid grid_remove = new Grid();
    //    var uframe_remove = new Frame();
    //    var lframe_remove = new Frame();
    //    int selectedindex = pickerRemarks.SelectedIndex;
    //    string selectedRemarks = "";
    //    if (selectedindex != -1)
    //    {
    //        selectedRemarks = pickerRemarks.Items[selectedindex];
    //    }

    //    Consumers? consumer = ConsumerRecords.lstconsumers.Where(c => c.accountNo == txtConsumerAcctNo.Text).FirstOrDefault();

    //    if (consumer == null)
    //    {
    //        return;
    //    }

    //    float presReading = ParseFloat(txtConsumerPresReading.Text);

    //    consumer.presreading = presReading;
    //    consumer.remarks = selectedRemarks;
    //    dbService = new DBService();
    //    bool isSaveSuccess = await dbService.ConsumerUpdateItemToDatabase(consumer);
    //    if (isSaveSuccess)
    //    {

    //        if (consumer != null)
    //        {

    //            var _gridBoxU = ConsumerRecords.gridConsumerUnbilledCollection;

    //            //Look for the grid and update
    //            foreach (Grid _gridConsumer in _gridBoxU)
    //            {
    //                foreach (var child in _gridConsumer.Children)
    //                {
    //                    if (child is AbsoluteLayout tmpAbsolutelayout)
    //                    {
    //                        if (tmpAbsolutelayout.AutomationId == "AbsoluteLayout_" + selected_seqno)
    //                        {
    //                            foreach (var child_labels in tmpAbsolutelayout.Children)
    //                            {
    //                                if (child_labels is Label tmpLabel)
    //                                {
    //                                    if (tmpLabel.Text == txtConsumerAcctNo.Text)
    //                                    {
    //                                        //Check again the grid
    //                                        bool isLabelUpdated = false;
    //                                        bool isFrameColorUpdated = false;
    //                                        foreach (var child_correctGrid in tmpAbsolutelayout.Children)
    //                                        {

    //                                            if (child_correctGrid is Label lblobject_correctGrid)
    //                                            {
    //                                                if (lblobject_correctGrid.Text == "U")
    //                                                {
    //                                                    lblobject_correctGrid.Text = "B";
    //                                                    isLabelUpdated = true;
    //                                                }
    //                                            }
    //                                            else if (child_correctGrid is Frame frameObject_correctGrid)
    //                                            {
    //                                                if (frameObject_correctGrid.BackgroundColor == Colors.Red)
    //                                                {
    //                                                    frameObject_correctGrid.BackgroundColor = Colors.Blue;
    //                                                    isFrameColorUpdated = true;
    //                                                }
    //                                            }
    //                                            if (isFrameColorUpdated && isLabelUpdated)
    //                                            {

    //                                                absoluteLayout_remove = (AbsoluteLayout)child;
    //                                                grid_remove = _gridConsumer;
    //                                                break;
    //                                            }
    //                                        }
    //                                    }
    //                                }
    //                            }
    //                        }
    //                    }
    //                }
    //            }
    //            //Get the Upper and Lower Frame
    //            foreach (var child in grid_remove.Children)
    //            {
    //                if (child is Frame sframe)
    //                {
    //                    if (sframe.AutomationId == "Lframe_" + selected_seqno)
    //                    {
    //                        lframe_remove = sframe;
    //                    }
    //                    if (sframe.AutomationId == "Uframe_" + selected_seqno)
    //                    {
    //                        uframe_remove = sframe;
    //                    }
    //                }
    //            }
    //            //Add it to Billed Collections
    //            int maxbatchbilled = ConsumerRecords.gridBilled_rowindex;
    //            if (maxbatchbilled > 0)
    //            {
    //                maxbatchbilled++;
    //            }
    //            //ConsumerRecords.gridConsumerBilledRecords.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //            //ConsumerRecords.gridConsumerBilledRecords.Add(lframe_remove, maxbatchbilled);
    //            //ConsumerRecords.gridConsumerBilledRecords.Add(uframe_remove, maxbatchbilled);
    //            //ConsumerRecords.gridConsumerBilledRecords.Add(absoluteLayout_remove, maxbatchbilled);

    //            grid_remove.Children.Remove(absoluteLayout_remove);
    //            grid_remove.Children.Remove(uframe_remove);
    //            grid_remove.Children.Remove(lframe_remove);

    //            List<Grid> _gridBoxB = new List<Grid>();

    //            if (ConsumerRecords.gridConsumerBilledCollection != null)
    //            {
    //                _gridBoxB = ConsumerRecords.gridConsumerBilledCollection;
    //            }


    //            if (_gridBoxB == null)
    //            {
    //                Grid tmpgridConsumerBilled = new Grid
    //                {
    //                    AutomationId = $"BGrid_{maxbatchbilled}",
    //                    RowDefinitions =
    //                        {
    //                            new RowDefinition { Height = GridLength.Auto }
    //                        },
    //                    ColumnDefinitions =
    //                        {
    //                            new ColumnDefinition { Width = GridLength.Star }
    //                        }

    //                };
    //                tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //                tmpgridConsumerBilled.Add(lframe_remove, 0, maxbatchbilled);
    //                tmpgridConsumerBilled.Add(uframe_remove, 0, maxbatchbilled);
    //                tmpgridConsumerBilled.Add(absoluteLayout_remove, 0, maxbatchbilled);
    //                _gridBoxB.Add(tmpgridConsumerBilled);
    //                ConsumerRecords.gridConsumerBilledCollection = _gridBoxB;
    //            }
    //            else
    //            {
    //                if (_gridBoxB.Count == 0)
    //                {
    //                    Grid tmpgridConsumerBilled = new Grid
    //                    {
    //                        AutomationId = $"BGrid_{maxbatchbilled}",
    //                        RowDefinitions =
    //                        {
    //                            new RowDefinition { Height = GridLength.Auto }
    //                        },
    //                        ColumnDefinitions =
    //                        {
    //                            new ColumnDefinition { Width = GridLength.Star }
    //                        }

    //                    };
    //                    tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //                    tmpgridConsumerBilled.Add(lframe_remove, 0, maxbatchbilled);
    //                    tmpgridConsumerBilled.Add(uframe_remove, 0, maxbatchbilled);
    //                    tmpgridConsumerBilled.Add(absoluteLayout_remove, 0, maxbatchbilled);
    //                    _gridBoxB.Add(tmpgridConsumerBilled);
    //                }
    //                else
    //                {
    //                    foreach (Grid _gridchild in _gridBoxB)
    //                    {
    //                        if (_gridchild.AutomationId == "BGrid_" + maxbatchbilled)
    //                        {
    //                            int maxrow = _gridchild.RowDefinitions.Count;
    //                            _gridchild.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    //                            _gridchild.Add(lframe_remove, 0, maxrow + 1);
    //                            _gridchild.Add(uframe_remove, 0, maxrow + 1);
    //                            _gridchild.Add(absoluteLayout_remove, 0, maxrow + 1);
    //                            break;
    //                        }
    //                    }
    //                }

    //            }


    //        }
    //        //lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.lstconsumers.Count.ToString();
    //        int selectedItem = pickerBillingFilter.SelectedIndex;
    //        if (selectedItem == 0)
    //        {

    //            lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
    //        }
    //        else if (selectedItem == 1)
    //        {

    //            lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
    //        }
    //        else
    //        {

    //            lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
    //        }
    //        await DisplayAlertAsync("Notice", "Reading Saved!", "Ok");

    //        if (MyDeviceInfo.currentPrintConfig != null)
    //        {
    //            bool toPrint = false;
    //            if (isPrintOn)
    //            {
    //                toPrint = true;
    //                //string output = await DoPrintv2(consumer);
    //                //await DisplayAlertAsync("Print Notice", output, "Ok");
    //            }
    //            else
    //            {
    //                toPrint = await DisplayAlertAsync("Confirm", "Print Transaction?", "Yes", "No");
    //            }

    //            if (toPrint)
    //            {
    //                string output = await DoPrintBill(consumer);
    //                await DisplayAlertAsync("Print Notice", output, "Ok");
    //            }


    //        }

    //        //Move to next seq no
    //        OnNextRecord(pickerBillingFilter.SelectedIndex);

    //    }
    //    else
    //    {
    //        await DisplayAlertAsync("Error", "Failed to saved ", "Ok");
    //    }
    //}


    private async void TapImgBack(object sender, EventArgs e)
    {
        //restruct list before going back to consumer list
        //ConsumerRecords.AutoID_UIndex = new List<string>();
        //ConsumerRecords.AutoID_BIndex = new List<string>();

        //int ctrx = 0;
        //if (ConsumerRecords.gridConsumerUnbilledCollection.Count > 0)
        //{
        //    foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerUnbilledCollection)
        //    {
        //        string gridautoID = _gridConsumer.AutomationId;
        //        ConsumerRecords.AutoID_UIndex.Add(gridautoID + "," + ctrx);
        //        ctrx++;

        //    }
        //}

        //ctrx = 0;
        //if (ConsumerRecords.gridConsumerBilledCollection.Count > 0)
        //{
        //    foreach (Grid _gridConsumer in ConsumerRecords.gridConsumerBilledCollection)
        //    {
        //        string gridautoID = _gridConsumer.AutomationId;
        //        ConsumerRecords.AutoID_BIndex.Add(gridautoID + "," + ctrx);
        //        ctrx++;
        //    }
        //}

        //await Navigation.PushAsync(new ReadingConsumers());
        await Navigation.PopOnceAsync();
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
        txtCustomEntry.Text = "";
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

    //private async void OnClickOk(object sender, EventArgs e)
    //{
    //    gridNumericPad.IsVisible = false;
    //    gridDetails.IsEnabled = true;
    //    if (txtCustomEntry.Text == "")
    //    {
    //        return;
    //    }

    //    switch (selected_field)
    //    {
    //        case 0:
    //            txtConsumerPresReading.Text = ParseFloat(txtCustomEntry.Text).ToString();
    //            await RecomputeCurrentAsync(force: true);
    //            break;
    //        case 1:
    //            OnFilterNameSearch(txtCustomEntry.Text);
    //            break;
    //        case 2:
    //            OnFilterNameSearch(txtCustomEntry.Text);
    //            break;
    //    }
    //    btnSave.Focus();
    //}
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
        SetPadMode("SEARCH BY: ACCOUNT NO\n" + ListTitle(pickerBillingFilter.SelectedIndex), "Enter Account No");
        gridNumericPad.IsVisible = true;
        gridNumericPad.IsEnabled = true;
        txtCustomEntry.Text = "";
    }

    private void OnMeterNoTapped(object sender, EventArgs e)
    {
        gridDetails.IsEnabled = false;
        selected_field = 2;
        SetPadMode("SEARCH BY: METER NO\n" + ListTitle(pickerBillingFilter.SelectedIndex), "Enter Meter No");
        gridNumericPad.IsVisible = true;
        gridNumericPad.IsEnabled = true;
        txtCustomEntry.Text = "";
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
        SetPadMode("Prev. Reading : " + (txtConsumerPrevReading.Text ?? "").Trim(), " ");
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
        if (selectedItem < 0) return;
        ConsumerRecords.consumer_selectedtype = selectedItem;
        lblListTitle.Text = ListTitle(selectedItem);
        if (selectedItem == 0)
        {
            btnPrint.IsEnabled = false;
            UpdateSummaryCount();
        }
        else if (selectedItem == 1)
        {
            btnPrint.IsEnabled = true;
            UpdateSummaryCount();
        }
        else
        {

            UpdateSummaryCount();
        }

        OnFirstLastRecord(1, pickerBillingFilter.SelectedIndex);
    }
    private async Task RecomputeCurrentAsync(bool force = false)
    {
        if (string.IsNullOrWhiteSpace(txtConsumerAcctNo.Text)) return;

        var consumer = ConsumerStore.Instance.FindByAccountNo(txtConsumerAcctNo.Text);
        if (consumer == null) return;

        string remark = pickerRemarks.SelectedIndex != -1 ? (pickerRemarks.SelectedItem?.ToString() ?? "") : "";
        bool remarkNeedsNoReading = remark == "Zero Reading" || remark.Contains("-AM") || remark.Contains("-ANM");

        // when triggered by a remark change, do nothing until a reading was entered
        if (!force && ParseFloat(txtConsumerPresReading.Text) <= 0 && !remarkNeedsNoReading) return;

        consumer = await OnInitialComputation(consumer);
        txtConsumerCurrentBill.Text = consumer.currentbill.ToString("#,##0.00");
        txtConsumerTotalBill.Text = consumer.total_bill.ToString("#,##0.00");
        txtConsumerUsage.Text = FormatQty(consumer.usage);
    }
    private readonly record struct ChargeResult(float CurrentBill, float Total, float Penalty);

    private static async Task<ChargeResult> ComputeChargesAsync(Consumers c, float usage, float discount)
    {
        string prm = c.parameters ?? "";
        float current;

        if (prm.Contains("IsSewerage") && prm.Contains("IsFlatRate"))
        {
            current = c.sewerage;
        }
        else
        {
            float billable = Math.Max(0, usage - Math.Max(0, c.minusage));
            var rates = await new DBService().GetBillrates(c.consumer_type, c.metersize);
            if (rates == null || rates.Count == 0) return default;
            current = (float)RateCalculator.Breakdown(rates, billable).Sum(l => l.Amount);
        }

        float penaltyBase = current - discount;
        if (c.arrears > 0) penaltyBase += c.arrears;

        return new ChargeResult(current, current + c.othercharges + c.arrears, penaltyBase * c.special_penalty_rate);
    }

    private async Task<Consumers> OnInitialComputation(Consumers consumer)
    {
        string remarks = pickerRemarks.SelectedIndex != -1 ? (pickerRemarks.SelectedItem?.ToString() ?? "") : "";
        float pres = ParseFloat(txtConsumerPresReading.Text);
        float prev = ParseFloat(txtConsumerPrevReading.Text);

        if (pres == 0 && remarks == "Zero Reading")
        {
            consumer.usage = 0; consumer.currentbill = 0; consumer.penaltyamt = 0; consumer.total_bill = 0;
            return consumer;
        }

        float usage;
        if (remarks is "" or "Normal Reading")
            usage = pres - prev;
        else if (remarks is "Abnormal Reading" or "Abnoral Reading")
            usage = ParseFloat(txtConsumerUsage.Text);
        else if (remarks == "Meter Reset")
        {
            // rollover depends on digits of the previous reading (4 digits -> 10000, 5 -> 100000 ...)
            int digits = (txtConsumerPrevReading.Text ?? "").Split('.')[0].Length;
            usage = (float)((Math.Pow(10, Math.Max(4, digits)) - prev) + pres);
        }
        else if (remarks.Contains("-AM"))
        {
            usage = consumer.average_usage;
            txtConsumerPresReading.Text = (prev + usage).ToString();
        }
        else if (remarks.Contains("-ANM"))
        {
            usage = consumer.average_usage;
            txtConsumerPresReading.Text = txtConsumerPrevReading.Text;
        }
        else
            usage = pres - prev;

        if (consumer.change_meter_usage > 0) usage += consumer.change_meter_usage;
        usage = Math.Max(0, usage);
        consumer.usage = usage;

        float discount = 0;
        if (consumer.less_usage > 0)
            discount = (await ComputeChargesAsync(consumer, consumer.less_usage, 0)).CurrentBill;

        var r = await ComputeChargesAsync(consumer, usage, discount);
        consumer.penaltyamt = r.Penalty;
        consumer.currentbill = r.CurrentBill;
        consumer.total_bill = r.Total;
        return consumer;
    }
    private async Task<float> OnTotalBillCompute(Consumers consumer, float _paramUsage)
    {
        float _TotalBill = 0;
        float _computedUsage = _paramUsage;

        string prm = consumer.parameters ?? "";
        bool IsSewerage = prm.Contains("IsSewerage");
        bool IsFlatRate = prm.Contains("IsFlatRate");
        if (IsSewerage && IsFlatRate)
        {
            _TotalBill = _TotalBill + consumer.sewerage;
            _currentPenalty = (_TotalBill - _currentBillDiscount) * consumer.special_penalty_rate;
            if (consumer.arrears > 0)
            {
                _currentPenalty = ((_TotalBill + consumer.arrears) - _currentBillDiscount) * consumer.special_penalty_rate;
            }

            _currentBillAmount = _TotalBill;
            _TotalBill = _TotalBill + consumer.othercharges + consumer.arrears;
            return _TotalBill;
        }

        if (consumer.minusage > 0)
        {
            _computedUsage = _computedUsage - consumer.minusage;
        }
        if (_computedUsage < 0)
        {
            _computedUsage = 0;
        }

        dbService = new DBService();
        List<Billing_rates> billrate = await dbService.GetBillrates(consumer.consumer_type, consumer.metersize);
        if (billrate != null && billrate.Count > 0)
        {
            // minimum usage was already subtracted above
            var lines = RateCalculator.Breakdown(billrate, _computedUsage);
            _TotalBill = (float)lines.Sum(l => l.Amount);

            _currentPenalty = (_TotalBill - _currentBillDiscount) * consumer.special_penalty_rate;
            if (consumer.arrears > 0)
            {
                _currentPenalty = ((_TotalBill + consumer.arrears) - _currentBillDiscount) * consumer.special_penalty_rate;
            }

            _currentBillAmount = _TotalBill;
            _TotalBill = _TotalBill + consumer.othercharges + consumer.arrears;   // added ONCE
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

    // =========================================================================
    // NEW: single rounding entry-point used by every money value
    //   - CWD : truncate to N decimals  (.666666 -> .66, .333333 -> .33, .999999 -> .99)
    //   - else: normal Math.Round
    // =========================================================================

    private static readonly Regex OtherChargeRegex = new Regex(
    @"(?<name>[^\d\t,;|:=]+?)[\s,;:=|]*(?<amt>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?![\d.])",
    RegexOptions.Compiled);

    private static List<(string Name, decimal Amount)> ParseOtherCharges(string? details)
    {
        var list = new List<(string, decimal)>();
        if (string.IsNullOrWhiteSpace(details)) return list;

        foreach (Match m in OtherChargeRegex.Matches(details))
        {
            string name = Regex.Replace(m.Groups["name"].Value, @"\s+", " ").Trim(' ', ',', ';', ':', '=', '|', '-');
            string amtText = m.Groups["amt"].Value.Replace(",", "");
            if (name.Length == 0) continue;
            if (decimal.TryParse(amtText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amt) && amt > 0)
                list.Add((name, Math.Round(amt, 2)));
        }
        return list;
    }
    private static RoundingRule GetRoundingRule(string? parameters)
    {
        string p = parameters ?? "";
        // ===== ADD FUTURE DISTRICT RULES HERE =====
        // if (p.Contains("[XYZWD]")) return RoundingRule.Nearest;
        return RoundingRule.Truncate;      // all current districts: .666666 -> .66, .999999 -> .99
    }

    private static decimal RoundMLS(decimal value, RoundingRule rule, int decimals = 2)
    {
        if (rule == RoundingRule.Nearest) return Math.Round(value, decimals);
        decimal scale = 1m;
        for (int i = 0; i < decimals; i++) scale *= 10m;
        return Math.Truncate(value * scale) / scale;   // toward zero, so -0.666 -> -0.66
    }

    // =========================================================================
    // NEW: all money math for DoPrintBill lives here.
    //      DoPrintBill just prints the pre-computed values.
    // =========================================================================
    private static BillAmounts ComputeBillAmounts(Consumers consumer, District d,
    decimal maintenanceFee = 0m, decimal sewerageRate = 0m,
    List<CommodityLine>? commodityLines = null)
    {
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));

        var rule = GetRoundingRule(consumer.parameters);
        string prm = consumer.parameters ?? "";

        var bill = new BillAmounts
        {
            IsSewerage = prm.Contains("IsSewerage"),
            IsFlatRate = prm.Contains("IsFlatRate"),
            MaintenanceFee = RoundMLS(maintenanceFee, rule)          // everyone
        };

        decimal currentBill;
        if (d.PLAWD && commodityLines != null && commodityLines.Count > 0)
            currentBill = commodityLines.Sum(l => RoundMLS(l.Amount, rule));
        else
            currentBill = RoundMLS(Convert.ToDecimal(consumer.currentbill), rule);

        decimal fileSewerage = float.IsNaN(consumer.sewerage) ? 0m : Convert.ToDecimal(consumer.sewerage);
        decimal sewerageAmount = 0m;

        if (d.MVWD)
        {
            if (bill.IsSewerage)
            {
                sewerageAmount = RoundMLS(fileSewerage, rule);       // MVWD = flat amount
                if (bill.IsFlatRate) currentBill = 0m;
            }
        }
        else
        {
            // everyone else: rate from Billing_rates (fallback: rate from the consumer file)
            decimal rate = sewerageRate > 0 ? sewerageRate : fileSewerage;
            if (rate > 0)
            {
                decimal usageDec = Convert.ToDecimal(consumer.usage);
                sewerageAmount = RoundMLS(rate * usageDec, rule);
                decimal esfFtax = RoundMLS(sewerageAmount * 0.02m, rule);
                sewerageAmount = RoundMLS(sewerageAmount + esfFtax, rule);
            }
        }

        bill.CurrentBill = currentBill;
        bill.SewerageAmount = sewerageAmount;

        decimal runningTotalBill = 0m;
        if (bill.CurrentBill > 0) runningTotalBill += bill.CurrentBill;
        if (bill.SewerageAmount > 0) runningTotalBill += bill.SewerageAmount;
        if (bill.MaintenanceFee > 0) runningTotalBill += bill.MaintenanceFee;

        // districts whose penalty is NOT charged on the sewerage / maintenance fees
        bool feesOutOfPenalty = d.CWD || d.PLAWD;      // add future districts here
        decimal penaltyBase = runningTotalBill;
        if (feesOutOfPenalty) penaltyBase -= bill.SewerageAmount + bill.MaintenanceFee;

        bill.SeniorDiscount = 0m;
        if (!d.IWD)
        {
            bool isSenior = prm.Contains("IsSenior");
            if (isSenior && consumer.usage <= 30)
            {
                decimal seniorDiscount = RoundMLS(bill.CurrentBill * 0.05m, rule) * -1m;
                runningTotalBill += seniorDiscount;
                penaltyBase += seniorDiscount;
                bill.SeniorDiscount = seniorDiscount;
                bill.ShowSeniorDiscount = true;
            }
        }

        decimal penaltyRate = Convert.ToDecimal(consumer.special_penalty_rate);
        decimal penaltyAmount = penaltyBase * penaltyRate;

        bill.Arrears = RoundMLS(Convert.ToDecimal(consumer.arrears), rule);
        runningTotalBill += bill.Arrears;
        penaltyBase += bill.Arrears;
        if (consumer.arrears < 0) penaltyAmount = penaltyBase * penaltyRate;

        bill.Others = RoundMLS(Convert.ToDecimal(consumer.othercharges), rule);
        if (bill.Others > 0) runningTotalBill += bill.Others;

        bill.BeforeDueTotal = RoundMLS(runningTotalBill, rule);

        int penaltyDecimals = d.MVWD ? 1 : 2;          // MVWD keeps 1 decimal on the penalty
        if (penaltyAmount < 0 || consumer.isnopenalty == 1) penaltyAmount = 0m;
        else penaltyAmount = RoundMLS(penaltyAmount, rule, penaltyDecimals);

        bill.PenaltyAmount = penaltyAmount;
        bill.AfterDueTotal = RoundMLS(runningTotalBill + penaltyAmount, rule);
        return bill;
    }
    private async Task<decimal> GetMaintenanceFeeAsync(Consumers c)
    {
        var rates = await new DBService().GetBillrates(c.consumer_type, c.metersize);
        // every tier row of a type/size carries the same fee
        return rates != null && rates.Count > 0 ? Convert.ToDecimal(rates[0].maintenance_fee) : 0m;
    }
    private readonly record struct District(bool SJWD, bool MVWD, bool IWD, bool CWD, bool PLAWD)
    {
        public static District From(string? p)
        {
            p ??= "";
            return new(p.Contains("[SJWD]"),
                              p.Contains("[MVWD]"),
                               p.Contains("[IWD]"),
                               p.Contains("[CWD]"),
                             p.Contains("[PLAWD]"));
        }
    }

    private static readonly (string Tag, string[] Lines)[] CompanyHeaders =
    {
        ("[IWD]",   new[] { "San Gregorio St., Indang, Cavite", "NON VAT Reg.TIN 000-773-614-000", "Mobile#: 0917-825-3813" }),
        ("[CWD]",   new[] { "Lakeview Subd.Halang,Calamba City", "NON VAT Reg.TIN 001-222-423-00000", "Tel#: 545-2863/545-1614/545-9344" }),
        ("[SJWD]",  new[] { "HallSt,Poblacion,SanJuan,Batangas", "NON VAT Reg.TIN 001-774-488-000", "Mobile#: 09199947756" }),
        ("[MVWD]",  new[] { "Pantay Daya,Vigan City,IlocosSur", "TIN 000-566-175-000 Non-VAT", "Tel. No. 077-674-0870" }),
        ("[PLAWD]", new[] { "Poblacion,Plaridel,Bulacan", "NON VAT Reg. TIN 000-547-793-00000", "Tel. No: 795-01-02" }),
    };
    public Task<string> DoPrintBill(Consumers consumer) => PrintGate.RunAsync(() => DoPrintBillCore(consumer));
    public async Task<string> DoPrintBillCore(Consumers consumer)
    {
        string result = "";
        string parameters = consumer.parameters ?? "";
        string strHeaderFileName = MOA.GetClientLogo(parameters);
        var d = District.From(parameters);
        var rule = GetRoundingRule(parameters);
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
            // ---- rates (cached, off the UI thread) ----
            var rates = await new DBService().GetBillrates(consumer.consumer_type, consumer.metersize);
            decimal maintenanceFee = 0m, sewerageRate = 0m;
            string typeDesc = "";
            if (rates.Count > 0)
            {
                maintenanceFee = Convert.ToDecimal(rates[0].maintenance_fee);
                sewerageRate = Convert.ToDecimal(rates[0].sewerage);
                typeDesc = (rates[0].constype_description ?? "").Trim();
            }

            List<CommodityLine>? commodityLines = null;
            if (d.PLAWD && consumer.currentbill > 0)
                commodityLines = RateCalculator.Breakdown(rates, consumer.usage, consumer.minusage);

            BillAmounts bill = ComputeBillAmounts(consumer, d, maintenanceFee, sewerageRate, commodityLines);

            // ---- dates ----
            DateTime currentbilldate = MOA.ParseBillDate(consumer.billdate);
            DateTime billduedate = MOA.ParseBillDate(consumer.duedate, currentbilldate);
            DateTime prevbilldate = MOA.ParseBillDate(consumer.prevreadingdate, currentbilldate);

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

            string macAddress = EscPOScmdPrint.NormalizeMacAddress(MyDeviceInfo.currentPrintConfig.bluetooth_address);
            await printer.ConnectAndPrepareAsync(macAddress);

            // ================= LOGO + HEADER =================
            await printer.WriteLogoAsync(logoBytes, widthFraction: 1.0);
            await printer.FeedAsync(1);

            var header = CompanyHeaders.FirstOrDefault(h => parameters.Contains(h.Tag)).Lines;
            if (header != null)
                foreach (var line in header)
                    await printer.PrintTextAsync(line, PrintAlign.Center, smallOn32: true);

            await printer.PrintSeparatorAsync(' ');

            // ================= TITLE =================
            if (d.PLAWD)
                await printer.PrintTextAsync("STATEMENT OF ACCOUNT", PrintAlign.Center,
                    new PrintStyle(Bold: true, Wide: true, High: true), new PrintStyle(Bold: true, High: true));
            else
                await printer.PrintTextAsync("BILLING NOTICE", PrintAlign.Center,
                    new PrintStyle(Bold: true, Wide: true, High: true));

            await printer.PrintTextAsync("For the month of " + printCurrentMonthYear, PrintAlign.Center);
            await printer.PrintTextAsync("(" + printPrevMonth + " Consumption)", PrintAlign.Center);
            await printer.PrintSeparatorAsync(' ');

            // ============= CONSUMER INFO =============
            await printer.PrintTextAsync(consumer.consumer_name, PrintAlign.Left, new PrintStyle(High: true));
            await printer.PrintTextAsync(consumer.address, PrintAlign.Left);

            if (d.PLAWD && typeDesc.Length > 0)
                await printer.PrintTextAsync(typeDesc, PrintAlign.Left);

            string accountNoText = (d.MVWD || d.SJWD)
                ? "AccountNo   : " + consumer.accountNo + "-" + consumer.consumer_type + consumer.metersize
                : "AccountNo   : " + consumer.accountNo;
            await printer.PrintTextAsync(accountNoText, PrintAlign.Left, new PrintStyle(High: true));

            string prevAcctNo = (consumer.prev_account_no ?? "").Trim();
            if (prevAcctNo.Length > 0)
                await printer.PrintTextAsync("Prev AcctNo : " + prevAcctNo, PrintAlign.Left);

            if (d.SJWD && typeDesc.Length > 0)
                await printer.PrintTextAsync(typeDesc, PrintAlign.Left);

            await printer.PrintTextAsync("Meter No.   : " + consumer.meterno, PrintAlign.Left);
            if (d.SJWD)
                await printer.PrintTextAsync("Seq. No.    : " + consumer.seqno, PrintAlign.Left);

            await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
            await printer.PrintRowAsync("Period From", printPrevDate);
            await printer.PrintRowAsync("Period To", printCurrentDate);
            await printer.PrintRowAsync("Due Date", printDueDate, new PrintStyle(High: true));
            await printer.PrintSeparatorAsync(SeparatorStyle.Broken);

            // ============= READINGS =============
            if (!bill.IsFlatRate || !d.MVWD)
            {
                await printer.PrintRowAsync("Pres Reading", consumer.presreading.ToString());
                await printer.PrintRowAsync("Prev Reading", consumer.prevreading.ToString());
                await printer.PrintRowAsync("Consumption", consumer.usage.ToString());
                await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
            }

            if (d.PLAWD && commodityLines is { Count: > 0 })
            {
                await printer.PrintTextAsync("COMMODITY CHARGES", PrintAlign.Left, new PrintStyle(Bold: true));
                foreach (var line in commodityLines)
                {
                    string left = line.IsMinimum ? line.Label : $"{line.Label} x {line.Rate:0.00}";
                    await printer.PrintRowAsync(left, RoundMLS(line.Amount, rule).ToString("#,##0.00"));
                }
                await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
            }

            // ============= AMOUNTS =============
            if (bill.CurrentBill > 0)
                await printer.PrintRowAsync("Current Bill", bill.CurrentBill.ToString("#,##0.00"));

            if (bill.SewerageAmount != 0)
                await printer.PrintRowAsync(
                    d.MVWD ? "Sewerage" : d.CWD ? "ESF" : d.PLAWD ? "Environmental/Septage Fee" : "Septage Mgmt. Fee",
                    bill.SewerageAmount.ToString("#,##0.00"));

            if (bill.MaintenanceFee != 0)
                await printer.PrintRowAsync("Maintenance Fee", bill.MaintenanceFee.ToString("#,##0.00"));

            if (bill.ShowSeniorDiscount)
                await printer.PrintRowAsync("Senior Discount", bill.SeniorDiscount.ToString("#,##0.00"));

            await printer.PrintRowAsync(consumer.arrears < 0 ? "Advance" : "Arrears", bill.Arrears.ToString("#,##0.00"));

            if (bill.Others > 0)
            {
                if (d.PLAWD)
                {
                    var otherItems = ParseOtherCharges(consumer.othercharges_details);
                    if (otherItems.Count > 0 && otherItems.Sum(o => o.Amount) == bill.Others)
                        foreach (var (name, amount) in otherItems)
                            await printer.PrintRowAsync(name, amount.ToString("#,##0.00"));
                }
                else
                    await printer.PrintRowAsync("Others", bill.Others.ToString("#,##0.00"));
            }

            await printer.PrintRowAsync(d.PLAWD ? "TOTAL DUE" : "BEFORE DUE",
                bill.BeforeDueTotal.ToString("#,##0.00"), new PrintStyle(High: true));
            await printer.PrintSeparatorAsync(SeparatorStyle.Broken);

            await printer.PrintTextAsync("* If unpaid after Due Date.", PrintAlign.Left, new PrintStyle(Underline: true));
            await printer.PrintRowAsync(d.PLAWD ? "Penalty (10%)" : "Penalty", bill.PenaltyAmount.ToString("#,##0.00"));

            if (d.PLAWD)
            {
                await printer.PrintRowAsync("AFTER DUE DATE", bill.AfterDueTotal.ToString("#,##0.00"), new PrintStyle(High: true));
                await printer.PrintSeparatorAsync(SeparatorStyle.DoubleBroken);
                await printer.PrintTextAsync("DISCONNECTION REMINDER", PrintAlign.Center, new PrintStyle(Bold: true, High: true));
                await printer.PrintTextAsync("FOR DISCONNECTION FIVE(5) DAYS", PrintAlign.Center, new PrintStyle(Bold: true, High: true));
                await printer.PrintTextAsync("AFTER DUE DATE", PrintAlign.Center, new PrintStyle(Bold: true, High: true));
                await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
            }
            else
            {
                await printer.PrintRowAsync("AFTER DUE", bill.AfterDueTotal.ToString("#,##0.00"), new PrintStyle(High: true));
                await printer.PrintSeparatorAsync(SeparatorStyle.DoubleBroken);
            }

            // ============= DISCONNECTION / READING DATE =============
            if (d.SJWD)
            {
                if (bill.AfterDueTotal > 0m)
                {
                    await printer.PrintTextAsync("DUE FOR DISCONNECTION ON " + printDiscDate,
                        PrintAlign.Center, new PrintStyle(High: true));
                    await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
                }
            }
            else
            {
                if (d.MVWD && bill.Arrears >= 215.5m)
                {
                    await printer.PrintRowAsync("Discon Date", printDiscDate, new PrintStyle(High: true));
                    await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
                }

                if (d.CWD)
                    await printer.PrintRowAsync("Reading Date/Time", currentbilldate.ToString("MM/dd/yyyy hh:mm tt"),
                        new PrintStyle(FontB: true, WidthMul: 1, HeightMul: 1));
                else
                    await printer.PrintRowAsync("Reading Date", currentbilldate.ToString("MM/dd/yyyy"));

                if (!d.PLAWD)
                    await printer.PrintSeparatorAsync(SeparatorStyle.Broken);
            }

            await printer.PrintRowAsync("Remarks", string.IsNullOrWhiteSpace(consumer.remarks) ? "Normal Reading" : consumer.remarks);
            await printer.PrintRowAsync("Meter Reader", consumer.billedby);
            await printer.FeedAsync();

            if (!d.PLAWD)
            {
                await printer.PrintTextAsync("This is not valid as", PrintAlign.Center);
                await printer.PrintTextAsync("Official Receipt", PrintAlign.Center);
                await printer.PrintTextAsync("Please disregard arrears if", PrintAlign.Center);
                await printer.PrintTextAsync("payment has been made", PrintAlign.Center);
                await printer.PrintTextAsync("PAY YOUR BILLS ON TIME TO AVOID", PrintAlign.Center);
                await printer.PrintTextAsync("PENALTY", PrintAlign.Center);
            }

            // ================= NOTICES =================
            string strNotif = LoginUser.login_user.msgmo;
            if (!string.IsNullOrWhiteSpace(strNotif))
            {
                if (d.PLAWD)
                {
                    await printer.PrintTextAsync("- ANNOUNCEMENT -", PrintAlign.Center);
                    await printer.PrintTextAsync(strNotif, PrintAlign.Left, new PrintStyle(FontB: true));
                }
                else
                    await printer.PrintTextAsync("REMINDER : " + strNotif, PrintAlign.Left);
            }

            if (d.PLAWD)
                await printer.PrintTextAsync("PAY YOUR BILLS ON TIME TO AVOID PENALTY", PrintAlign.Center);

            // ================= BARCODE (same for all districts) =================
            await printer.WriteBarcodeAsync(consumer.accountNo.Trim(), BarcodeType.Code128,
                widthFraction: 1.2, heightDots: 70, humanText: false, maxModuleDots: 3, align: PrintAlign.Center);

            await printer.FeedAsync(d.PLAWD ? 3 : 2);
            await printer.FlushAsync();
            result = "Done printing.";
        }
        catch (Exception ex)
        {
            result = "Print failed: " + ex.GetType().Name + " - " + ex.Message;
        }
        finally
        {
            await printer.CloseAsync();
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

    private async void ReadImgToBase64String()
    {

        string sourcefilename = "Datafiles/msl_print.bmp";
        //Stream stream = assembly.GetManifestResourceStream(resourcePrefix + fileName);
        using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(sourcefilename);
        byte[] imgByteArray = new byte[stream.Length];
        stream.Read(imgByteArray, 0, (int)stream.Length);
        logoBase64ImgStr = Convert.ToBase64String(imgByteArray);

    }
    private void SetBusy(bool on, string text = "Please wait...")
    {
        lblBusy.Text = text;
        busyOverlay.IsVisible = on;
    }
    private async void OnPrintBill(object sender, EventArgs e)
    => await UiGuard.RunAsync("reading-action", PrintBillAsync);
    private async Task PrintBillAsync()
    {
        if (string.IsNullOrEmpty(txtConsumerAcctNo.Text)) return;
        if (MyDeviceInfo.currentPrintConfig == null)
        {
            await DisplayAlertAsync("Print Notice", "No default printer loaded. Please check under Utilities.", "Ok");
            return;
        }

        if (!await DisplayAlertAsync("Confirm", "Print Selected Transaction?", "Ok", "Cancel")) return;

        var result = ConsumerStore.Instance.FindByAccountNo(txtConsumerAcctNo.Text);
        if (result == null) return;

        string output;
        SetBusy(true, "Printing, please wait...");
        try { output = await DoPrintBill(result); }
        finally { SetBusy(false); }

        await DisplayAlertAsync("Print Notice", output, "Ok");
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
    => await UiGuard.RunAsync("reading-action", SaveReadingAsync);
    private async Task SaveReadingAsync()
    {
        if (string.IsNullOrEmpty(txtConsumerAcctNo.Text))
        {
            return;
        }

        if (decimal.TryParse(txtConsumerCurrentBill.Text, out decimal bill) && bill == 0)
        {
            await DisplayAlertAsync("Notice", "Please enter the present reading first.", "Ok");   // NEW
            return;
        }

        string strRemarks = "";
        double present = ParseFloat(txtConsumerPresReading.Text);
        double previous = ParseFloat(txtConsumerPrevReading.Text);
        double usageNow = present - previous;

        bool isPresentLess = present < previous;
        if ((decimal.TryParse(txtConsumerUsage.Text, out decimal Usage) && Usage < 0) || isPresentLess)
        {
            bool confirmUsage = await DisplayAlertAsync("NEGATIVE CONSUMPTION!", "Present reading is less than the Previous reading!\n\nDo you want to continue?", "Yes", "No");
            if (!confirmUsage) return;

            bool confirmUsage2x = await DisplayAlertAsync("NEGATIVE CONSUMPTION!",
                $"Present   = {present}\n" +
                $"Previous = {previous}\n" +
                $"----------------------\n" +
                $"Usage     = {usageNow}\n\n" +
                $"Are you sure you want to save NEGATIVE consumption?",
                "Yes", "No");
            if (!confirmUsage2x) return;
        }
        else
        {
            strRemarks = UsageAnalysisHelper.GstrUsageAnalysisRemarks(usageNow, Fld2Dob(_averageCons));
            if (strRemarks != "")
            {
                bool continueEntry = await DisplayAlertAsync("VALIDATE CONSUMPTION!",
                    "Current usage is " + strRemarks + "!\n\nDo you want to continue?", "Yes", "No");
                if (!continueEntry) return;
            }
        }

        bool confirm = await DisplayAlertAsync("Confirm Saving", "Save this transaction?", "Yes", "No");
        if (!confirm) return;

        string selectedRemarks = pickerRemarks.SelectedIndex != -1 ? (pickerRemarks.SelectedItem?.ToString() ?? "") : "";

        Consumers? consumer = ConsumerStore.Instance.FindByAccountNo(txtConsumerAcctNo.Text);
        if (consumer == null) return;

        consumer.presreading = ParseFloat(txtConsumerPresReading.Text);
        consumer.remarks = (strRemarks != "" && string.IsNullOrEmpty(selectedRemarks)) ? strRemarks : selectedRemarks;

        dbService = new DBService();
        bool isSaveSuccess;
        SetBusy(true, "Saving...");
        try { isSaveSuccess = await dbService.ConsumerUpdateItemToDatabase(consumer); }
        finally { SetBusy(false); }
        if (isSaveSuccess)
        {
            ConsumerStore.Instance.MarkBilled(consumer);

            int selectedItem = pickerBillingFilter.SelectedIndex;
            if (selectedItem == 0)
                UpdateSummaryCount();
            else if (selectedItem == 1)
                UpdateSummaryCount();
            else
                UpdateSummaryCount();

            if (MyDeviceInfo.currentPrintConfig != null)
            {
                bool toPrint = isPrintOn ? true : await DisplayAlertAsync("Confirm", "Print Transaction?", "Yes", "No");
                if (toPrint)
                {
                    string output;
                    SetBusy(true, "Printing, please wait...");
                    try { output = await DoPrintBill(consumer); }
                    finally { SetBusy(false); }

                    if (output != "Done printing.")
                        MyDeviceInfo.currentPrintConfig = null;
                }
            }
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

    private async void OnRemarkChanged(object sender, EventArgs e)
    {
        int selectedindex = pickerRemarks.SelectedIndex;
        if (selectedindex != -1)
        {
            txtRemarks.Text = pickerRemarks.Items[selectedindex];
        }
        prevIndex = selectedindex;
        txtRemarks.IsVisible = true;
        pickerRemarks.IsVisible = false;
        btnSave.Focus();

        if (selectedindex != -1)
            await RecomputeCurrentAsync();      // NEW
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
        // hardware-key focus highlight (Honeywell keypad)
        if (sender is Button b)
        {
            b.BackgroundColor = Colors.Yellow;
            b.TextColor = Colors.Black;
        }
    }

    private void OnSaveUnFocus(object sender, FocusEventArgs e)
    {
        // go back to whatever the theme style says (light or dark)
        if (sender is Button b)
        {
            b.ClearValue(Button.BackgroundColorProperty);
            b.ClearValue(Button.TextColorProperty);
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