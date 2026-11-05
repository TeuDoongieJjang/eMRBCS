using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using SharpCifs.Util.Sharpen;
using SQLite;
using System.Timers;
using System.Windows.Input;




namespace eMRBCS.Pages;

public partial class ReadingConsumers : ContentPage
{
    private static SQLiteConnection? Database;
    string dbname = database_info.dbName;

    //List<Consumers> consumer_items;
    List<Remarks>? remarks_items;

    List<Grid>? gridConsumersUnbilled;
    List<Grid>? gridConsumersBilled;

    List<string> search_list_result;
    List<int> acctID_ULoaded;
    List<int> acctID_BLoaded;

    List<string> AutoID_ULoaded;
    List<string> AutoID_BLoaded;



    Grid? tmpgridConsumer;
    Grid tmpgridConsumerBilled;
    int rowindex = 0;

    DBService? dbService;

    int batchctr_u = 0;
    int batchctr_b = 0;
    int batchctr_s = 0;

    long selected_seqno;
    int selected_idno;

    bool isSearching = false;
    bool isLoadingGrid = false;
    bool allisLoadedU = false;
    bool allisLoadedB = false;

    bool showOptions = false;

    double scrollY = 0;

    int noofsearch = 0;
    string customError = "";

    private System.Timers.Timer _systimer;


    int selected_field = 0; //0 - Pres Reading, 1 - Acct No, 2 - Meter No

    private ICommand? FrameTappedCommand { get; set; }

    public ReadingConsumers()
    {
        InitializeComponent();

        //activityIndicator.IsVisible = true;
        //activityIndicator.IsRunning= true;
        stackLoading.IsVisible = true;
        pickerBillingFilter.IsEnabled = false;
        pickerFiledFilter.IsEnabled = false;
        searchFilterName.IsEnabled = false;

        gridFilter.IsVisible = false;
        gridSearch.IsVisible = false;

        overlaybox.BackgroundColor = Color.Parse("#80000000");

        _systimer = new System.Timers.Timer(3000);
        _systimer.Enabled = false;
        _systimer.Elapsed += OnTimedEventv2;

        //ScreenAdjustment();
    }

    private async void ScreenAdjustment()
    {
        if (MyDeviceInfo.device_heightpix > 2200)
        {


            gridContainerU.HeightRequest = 620;
            gridContainerB.HeightRequest = 620;
        }
        else if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
        {

            if (showOptions)
            {
                gridContainerU.HeightRequest = 220;
                gridContainerB.HeightRequest = 220;


            }

        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        //batchctr_u = ConsumerRecords.batchctr_u;
        //batchctr_b = ConsumerRecords.batchctr_b;

        InitializePickers();
        _systimer.Enabled = true;
        //StartLoadGridTimer();
        //await Task.Delay(2000);


        //LoadAllConsumersfromRaw();
    }

    private void OnTimedEventv2(Object source, ElapsedEventArgs e)
    {
        // Update the label text on the main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _systimer.Enabled = false;
            acctID_ULoaded = new List<int>();
            acctID_BLoaded = new List<int>();

            AutoID_ULoaded = new List<string>();
            AutoID_BLoaded = new List<string>();
            //AutoID_UIndex = new List<string>();
            LoadAllConsumersfromRaw();

        });
    }



    private async Task<bool> LoadGridfromCache()
    {
        bool isDone = false;
        try
        {
            await Task.Run(() =>
            {
                gridBoxU = new StackLayout();
                gridBoxU = ConsumerRecords.UconsumerStacks;
                //gridContainerU.IsEnabled = false;
                gridContainerU.Content = gridBoxU;

                gridBoxB = new StackLayout();
                gridBoxB = ConsumerRecords.BconsumerStacks;
                //gridContainerB.IsEnabled = false;
                gridContainerB.Content = gridBoxB;

                isDone = true;
            });
        }
        catch (Exception ex)
        {
            customError = ex.Message;
        }
        return isDone;
    }

    private void OnTimedEvent(Object source, ElapsedEventArgs e)
    {

    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        //activityIndicatorPage.IsRunning = false;
        //activityIndicatorPage.IsVisible = false;
    }


    private async Task LoadGridperBatch(List<Grid> _gridListConsumers, int _gridStatus)
    {
        string _gridID;
        int _gridbatch;
        try
        {
            //Check if batch is more than the count
            _gridbatch = _gridStatus == 0 ? batchctr_u : batchctr_b;
            if (_gridbatch >= _gridListConsumers.Count)
            {

                return;
            }
            if (_gridListConsumers.Count > 0)
            {
                if (_gridStatus == 0)
                {
                    gridContainerU.Content = gridBoxU;
                    _gridID = "UGrid_";
                }
                else
                {
                    gridContainerB.Content = gridBoxB;
                    _gridID = "BGrid_";
                }

                FrameTappedCommand = new Command<string>(OnFrameTappedv1);
                foreach (Grid _gridConsumer in _gridListConsumers)
                {
                    _gridbatch = _gridStatus == 0 ? batchctr_u : batchctr_b;
                    if (_gridConsumer.AutomationId == _gridID + _gridbatch)
                    {
                        foreach (var child in _gridConsumer.Children)
                        {
                            if (child is Frame frame)
                            {
                                foreach (var _gesture in frame.GestureRecognizers)
                                {
                                    if (_gesture is TapGestureRecognizer tapgesture)
                                    {
                                        tapgesture.Command = FrameTappedCommand;
                                    }
                                }
                                frame.IsVisible = true;
                            }
                            if (child is AbsoluteLayout abslayout)
                            {
                                abslayout.IsVisible = true;
                            }

                        }

                        Dispatcher.Dispatch(() =>
                        {
                            //gridBoxU.Children.Add(_gridConsumer);
                            try
                            {
                                if (_gridStatus == 0)
                                {
                                    //gridBoxU.Children.Add(_gridConsumer);
                                    gridBoxU.Add(_gridConsumer);
                                    batchctr_u++;
                                }
                                else
                                {
                                    gridBoxB.Children.Add(_gridConsumer);
                                    batchctr_b++;
                                }
                            }
                            catch (Exception ex)
                            {

                            }

                        });
                        await Task.Delay(100);

                        if (!isSearching)
                        {
                            break;
                        }

                    }

                }

            }

        }
        catch (Exception ex)
        {

        }

    }

    private async Task LoadGridperBatch_new(List<Grid> _gridListConsumers, int _gridStatus)
    {
        int _gridbatch = 0;
        try
        {
            //Check if batch is more than the count
            if (_gridListConsumers == null)
            {
                return;
            }
            int maxListCount = 0;

            if (_gridStatus == 0)
            {
                _gridbatch = batchctr_u;
                maxListCount = ConsumerRecords.gridConsumerUnbilledCollection.Count;
                if (_gridbatch >= maxListCount)
                {
                    allisLoadedU = true;
                    return;
                }
            }
            else
            {
                _gridbatch = batchctr_b;
                maxListCount = ConsumerRecords.gridConsumerBilledCollection.Count;
                if (_gridbatch >= maxListCount)
                {
                    allisLoadedB = true;
                    return;
                }
            }
            //_gridbatch = _gridStatus == 0 ? batchctr_u : batchctr_b;
            //int maxListCount = _gridListConsumers.Count - 1;

            if (maxListCount == 0)
            {
                return;
            }
            if (_gridListConsumers.Count > 0)
            {
                int maxindex = _gridbatch + 20;
                maxindex = maxindex >= maxListCount ? maxListCount : maxindex;

                FrameTappedCommand = new Command<string>(OnFrameTappedv1);

                bool withError = false;
                for (int ctrx = _gridbatch; ctrx < maxindex; ctrx++)
                {
                    Grid _gridConsumer = _gridListConsumers[ctrx];
                    _gridConsumer.IsVisible = true;
                    bool isGridFound = false;
                    if (_gridStatus == 0)
                    {
                        isGridFound = AutoID_ULoaded.Contains(_gridConsumer.AutomationId);
                    }
                    else
                    {
                        isGridFound = AutoID_BLoaded.Contains(_gridConsumer.AutomationId);
                    }

                    if (!isGridFound)
                    {
                        foreach (var child in _gridConsumer.Children)
                        {
                            int foundFrame = 0;
                            if (child is Frame frame)
                            {
                                foreach (var _gesture in frame.GestureRecognizers)
                                {
                                    if (_gesture is TapGestureRecognizer tapgesture)
                                    {
                                        tapgesture.Command = FrameTappedCommand;
                                        foundFrame = 1;
                                        break;
                                    }
                                }
                            }
                            if (foundFrame == 1)
                            {
                                break;
                            }
                        }

                        Dispatcher.Dispatch(async () =>
                        {
                            //gridBoxU.Children.Add(_gridConsumer);
                            try
                            {
                                if (_gridStatus == 0)
                                {
                                    AutoID_ULoaded.Add(_gridConsumer.AutomationId);
                                    gridMainU.Add(_gridConsumer, 0, ctrx);

                                    //add to index collection
                                    //AutoID_UIndex.Add(_gridConsumer.AutomationId + "," + ctrx);
                                    //acctID_ULoaded.Add(ctrx);

                                    if (gridMainU.RowDefinitions.Count < maxindex)
                                    {
                                        gridMainU.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                    }

                                    batchctr_u++;
                                }
                                else
                                {
                                    AutoID_BLoaded.Add(_gridConsumer.AutomationId);
                                    gridMainB.Add(_gridConsumer, 0, ctrx);
                                    //add to index collection
                                    //AutoID_UIndex.Add(_gridConsumer.AutomationId + "," + ctrx);

                                    if (gridMainB.RowDefinitions.Count < maxindex)
                                    {
                                        gridMainB.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                    }

                                    batchctr_b++;
                                }

                            }
                            catch (Exception ex)
                            {
                                withError = true;
                                customError = ex.Message;
                                //await DisplayAlertAsync("Notice", "Something went wrong attaching Grid in batch " + batchctr_u + "\n" + _gridConsumer.AutomationId + "\n" + ex.Message, "Ok");                                
                            }

                        });
                        if (withError)
                        {
                            break;
                        }
                        await Task.Delay(100);
                    }
                }

            }
            if (_gridStatus == 0)
            {
                ConsumerRecords.batchctr_u = batchctr_u;
            }
            else
            {
                ConsumerRecords.batchctr_b = batchctr_b;
            }

        }
        catch (Exception ex)
        {
            //await DisplayAlertAsync("Notice", "Something went wrong. Loading Grid " + _gridbatch + "\nStatus " + _gridStatus + "\n" +  ex.Message, "Ok");
            customError = ex.Message;
        }


    }

    private async Task LoadGridperBatch_Search(List<Grid> _gridListConsumers, int _gridStatus, List<Consumers> resulttoload)
    {
        customError = "";
        try
        {
            FrameTappedCommand = new Command<string>(OnFrameTappedv1);

            int _gridbatch = 0;
            //int maxUGridCount = ConsumerRecords.gridConsumerUnbilledCollection.Count;
            //int maxBGridCount = ConsumerRecords.gridConsumerBilledRecords.Count;

            if (_gridListConsumers == null)
            {
                return;
            }

            int maxindex = _gridListConsumers.Count - 1;

            List<string> result_autoID = new List<string>();

            int maxindex_search = resulttoload.Count;
            int searchIndex = 0;

            if (_gridStatus == 0)
            {
                foreach (Grid _gridConsumer in gridMainU.Children)
                {
                    _gridConsumer.IsVisible = false;
                }
            }
            else
            {
                foreach (Grid _gridConsumer in gridMainB.Children)
                {
                    _gridConsumer.IsVisible = false;
                }
            }


            foreach (var item in resulttoload)
            {
                int targetAcctID = item.accountID;
                string current_meterno = item.meterno;
                string gridInnerAutoID = $"UInnerGrid_{targetAcctID}";


                string foundAutoID = "";
                if (_gridStatus == 0)
                {
                    //gridInnerAutoID = $"UInnerGrid_{targetAcctID}";
                    foundAutoID = ConsumerRecords.AutoID_UIndex.Find(autoid => autoid.Contains(gridInnerAutoID));
                }
                else
                {
                    gridInnerAutoID = $"BInnerGrid_{targetAcctID}";
                    foundAutoID = ConsumerRecords.AutoID_BIndex.Find(autoid => autoid.Contains(gridInnerAutoID));
                    if (foundAutoID == null)
                    {
                        //try looking using Unbilled autoID
                        gridInnerAutoID = $"UInnerGrid_{targetAcctID}";
                        foundAutoID = ConsumerRecords.AutoID_BIndex.Find(autoid => autoid.Contains(gridInnerAutoID));
                    }
                }
                result_autoID.Add(gridInnerAutoID);

                if (foundAutoID != null)
                {
                    string[] foundindex = foundAutoID.Split(",");

                    int gridindex = int.Parse(foundindex[1]);

                    Grid _gridConsumer = _gridListConsumers[gridindex];

                    bool isGridFound = result_autoID.Contains(_gridConsumer.AutomationId);
                    if (isGridFound)
                    {
                        searchIndex++;
                        bool isGridalreadyLoaded = false;
                        if (_gridStatus == 0)
                        {
                            isGridalreadyLoaded = AutoID_ULoaded.Contains(_gridConsumer.AutomationId);
                        }
                        else
                        {
                            isGridalreadyLoaded = AutoID_BLoaded.Contains(_gridConsumer.AutomationId);
                        }
                        if (!isGridalreadyLoaded)
                        {
                            foreach (var child in _gridConsumer.Children)
                            {
                                int foundFrame = 0;
                                if (child is Frame frame)
                                {
                                    foreach (var _gesture in frame.GestureRecognizers)
                                    {
                                        if (_gesture is TapGestureRecognizer tapgesture)
                                        {
                                            tapgesture.Command = FrameTappedCommand;
                                            foundFrame = 1;
                                            break;
                                        }
                                    }
                                }
                                if (foundFrame == 1)
                                {
                                    break;
                                }
                            }

                            Dispatcher.Dispatch(async () =>
                            {
                                //gridBoxU.Children.Add(_gridConsumer);
                                try
                                {
                                    if (_gridStatus == 0)
                                    {
                                        gridMainU.Add(_gridConsumer, 0, gridindex);
                                        //acctID_ULoaded.Add(ctrx);
                                        AutoID_ULoaded.Add(_gridConsumer.AutomationId);
                                        if (gridMainU.RowDefinitions.Count < maxindex)
                                        {
                                            gridMainU.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                        }

                                        //batchctr_u++;
                                    }
                                    else
                                    {
                                        gridMainB.Add(_gridConsumer, 0, gridindex);
                                        //acctID_BLoaded.Add(gridindex);
                                        AutoID_BLoaded.Add(_gridConsumer.AutomationId);
                                        if (gridMainB.RowDefinitions.Count < maxindex)
                                        {
                                            gridMainB.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                        }

                                        //batchctr_b++;
                                    }

                                }
                                catch (Exception ex)
                                {
                                    customError = ex.Message;
                                    //await DisplayAlertAsync("Notice", "Something went wrong attaching Grid in batch " + batchctr_u + "\n" + ex.Message, "Ok");
                                }

                            });
                            await Task.Delay(100);
                        }
                        _gridConsumer.IsVisible = true;
                    }
                }




            }





        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Notice", "Something went wrong. Loading Grid\n" + ex.Message, "Ok");
        }


    }

    private async void LoadAllConsumersfromRaw()
    {
        customError = "";
        try
        {
            //Load Unbilled Consumers
            await Task.Delay(500);
            isSearching = false;

            //create all rows
            int maxUGridCount = 0;
            int maxBGridCount = 0;
            if (ConsumerRecords.gridConsumerUnbilledCollection != null)
            {
                maxUGridCount = ConsumerRecords.gridConsumerUnbilledCollection.Count;
            }
            if (ConsumerRecords.gridConsumerBilledCollection != null)
            {
                maxBGridCount = ConsumerRecords.gridConsumerBilledCollection.Count;
            }



            if (maxUGridCount > 0)
            {
                for (int rowctr = 0; rowctr < maxUGridCount; rowctr++)
                {
                    gridMainU.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
            }
            if (maxBGridCount > 0)
            {
                for (int rowctr = 0; rowctr < maxBGridCount; rowctr++)
                {
                    gridMainB.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
            }
            isLoadingGrid = true;
            //gridContainerU.IsEnabled = false;
            if (maxUGridCount > 0)
            {
                await LoadGridperBatch_new(ConsumerRecords.gridConsumerUnbilledCollection, 0);
            }
            if (maxBGridCount > 0)
            {
                await LoadGridperBatch_new(ConsumerRecords.gridConsumerBilledCollection, 1);
            }



            //await LoadGridperBatch(ConsumerRecords.gridConsumerUnbilledCollection, 0);
            //await LoadGridperBatch(ConsumerRecords.gridConsumerBilledCollection, 1);

            //int summarycount = ConsumerRecords.lstconsumers.Count - ConsumerRecords.noofrecordsBilled;
            if (isSearching)
            {
                lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else
            {
                if (pickerBillingFilter.SelectedIndex == 0)
                {
                    lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();

                }
                else if (pickerBillingFilter.SelectedIndex == 1)
                {
                    lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
                }
                else
                {
                    lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
                }
            }

            pickerBillingFilter.IsEnabled = true;
            pickerFiledFilter.IsEnabled = true;
            searchFilterName.IsEnabled = true;

            //activityIndicator.IsVisible = false;
            //activityIndicator.IsRunning = false;
            //lblLoadingData.IsVisible = false;

            stackLoading.IsVisible = false;

            //gridContainerU.IsEnabled = true;
            isLoadingGrid = false;
        }
        catch (Exception ex)
        {
            customError = ex.Message;
            //await DisplayAlertAsync("Warning!", "Something went wrong loading consumer grid\n" + ex.Message, "Ok");
        }
    }



    //Rewrite code - Original
    private async void LoadConsumerFromRaw()
    {

    }

    private async void OnGridScrolledU(object sender, ScrolledEventArgs e)
    {
        customError = "";
        try
        {
            if (isLoadingGrid)
            {
                await gridContainerU.ScrollToAsync(0, scrollY, false);
                return;
            }

            var scrollView = (ScrollView)sender;
            var scrolledY = e.ScrollY;
            var contentHeight = scrollView.ContentSize.Height;
            //if (ConsumerRecords.isFromCache)
            //{
            //    if (contentHeight < ConsumerRecords.Uscrollheight)
            //    {
            //        contentHeight = ConsumerRecords.Uscrollheight;
            //    }
            //}
            var scrollViewHeight = scrollView.Height;
            int totalscrolled = (int)(scrolledY + scrollViewHeight);

            // Check if the user has scrolled to the bottom
            //if ((totalscrolled >= (int)contentHeight) && (!isSearching) && (!ConsumerRecords.isGridReloaded))
            if ((totalscrolled >= (int)contentHeight) && (!isSearching))
            {
                if (allisLoadedU)
                {
                    return;
                }
                scrollY = scrolledY;
                //activityIndicator.IsVisible = true;
                //activityIndicator.IsRunning = true;
                //lblLoadingData.Text = "Loading more Customers.. Please wait...";
                //lblLoadingData.IsVisible = true;

                stackLoading.IsVisible = true;
                lblloading.Text = "Loading customers";

                await Task.Delay(100);
                //isSearching = false;
                //gridContainerU.IsEnabled = false;
                isLoadingGrid = true;
                await LoadGridperBatch_new(ConsumerRecords.gridConsumerUnbilledCollection, 0);
                isLoadingGrid = false;

                //activityIndicator.IsVisible = false;
                //activityIndicator.IsRunning = false;
                //lblLoadingData.IsVisible = false;

                stackLoading.IsVisible = false;

                //gridContainerU.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            customError = ex.Message;
            await DisplayAlertAsync("Warning", "Error on Search grid scroll\nError: " + customError, "Ok");
        }


    }

    private async void OnGridScrolledB(object sender, ScrolledEventArgs e)
    {
        customError = "";
        try
        {
            if (isLoadingGrid)
            {
                await gridContainerB.ScrollToAsync(0, scrollY, false);
                return;
            }
            var scrollView = (ScrollView)sender;
            var scrolledY = e.ScrollY;
            var contentHeight = scrollView.ContentSize.Height;

            var scrollViewHeight = scrollView.Height;
            int totalscrolled = (int)(scrolledY + scrollViewHeight);

            // Check if the user has scrolled to the bottom
            if ((totalscrolled >= (int)contentHeight) && (!isSearching))
            {
                if (allisLoadedB)
                {
                    return;
                }
                scrollY = scrolledY;
                //activityIndicator.IsVisible = true;
                //activityIndicator.IsRunning = true;
                //lblLoadingData.IsVisible = true;

                stackLoading.IsVisible = true;

                await Task.Delay(100);
                isLoadingGrid = true;
                await LoadGridperBatch_new(ConsumerRecords.gridConsumerBilledCollection, 1);
                isLoadingGrid = false;

                //activityIndicator.IsVisible = false;
                //activityIndicator.IsRunning = false;
                //lblLoadingData.IsVisible = false;

                stackLoading.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            customError = ex.Message;
            await DisplayAlertAsync("Warning", "Error on Search grid scroll\nError: " + customError, "Ok");
        }


    }




    private void InitializeDatabase()
    {
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, dbname);
        Database = new SQLiteConnection(dbPath);
    }

    private async void TapImgBack(object sender, EventArgs e)
    {


        await Navigation.PushAsync(new Mainmenu());
    }

    private async void InitializePickers()
    {
        pickerFiledFilter.ItemsSource = new List<string>
        {
            "Meter No",
            "Account No",
            "Name"
        };

        pickerBillingFilter.ItemsSource = new List<string>
        {
            "Unbilled Consumers",
            "Billed Consumers",
            "Collected Consumers"
        };
        List<string> remarksList = new List<string>();


        pickerBillingFilter.SelectedIndex = 0;
        pickerFiledFilter.SelectedIndex = 0;


    }

    private async void OnConsumerTypeChanged(object sender, EventArgs e)
    {
        int selectedItem = pickerBillingFilter.SelectedIndex;
        if (selectedItem == 0)
        {
            gridContainerB.IsVisible = false;
            gridContainerU.IsVisible = true;
            gridMainU.IsVisible = true;
            lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
        }
        else if (selectedItem == 1)
        {
            gridContainerB.IsVisible = true;
            gridContainerU.IsVisible = false;
            gridMainB.IsVisible = true;
            lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
        }
        else
        {
            gridContainerB.IsVisible = false;
            gridContainerU.IsVisible = false;
            lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
        }

    }

    private async void OnFrameTappedv1(string parameter)
    {
        if (isLoadingGrid)
        {
            return;
        }
        //activityIndicatorPage.IsRunning = true;
        //activityIndicatorPage.IsVisible = true;
        lblloading.Text = "Loading Information";
        stackLoading.IsVisible = true;

        await Task.Delay(500);
        ConsumerRecords.acctNo = parameter;
        ConsumerRecords.consumer_selectedtype = pickerBillingFilter.SelectedIndex;
        await Navigation.PushAsync(new Reading());
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



    private async void OnSearchFilter_new(object sender, EventArgs e)
    {
        customError = "";
        string OnErrorLocation = "Start";
        try
        {
            if (searchFilterName.Text.Length < 3)
            {
                await DisplayAlertAsync("Notice", "Please enter at least 3 characters.", "Ok");
                return;
            }
            int selectedFilterIndex = pickerFiledFilter.SelectedIndex;
            string searchValue = searchFilterName.Text;

            List<Consumers> result = new List<Consumers>();

            //txtcount.Text = DateTime.Now.ToString("HH:mm:ss");

            //activityIndicator.IsVisible = true;
            //activityIndicator.IsRunning = true;
            //lblLoadingData.Text = "Searching Customers.. Please wait...";
            //lblLoadingData.IsVisible = true;

            stackLoading.IsVisible = true;
            lblloading.Text = "Searching Customers";

            int _gridStatus = 0;
            List<Grid> gridHolder = new List<Grid>();
            if (pickerBillingFilter.SelectedIndex == 0)
            {
                gridBoxU.IsVisible = false;
                gridHolder = ConsumerRecords.gridConsumerUnbilledCollection;
                foreach (Grid _gridConsumer in gridMainU.Children)
                {
                    _gridConsumer.IsVisible = false;
                }
            }
            else if (pickerBillingFilter.SelectedIndex == 1)
            {
                _gridStatus = 1;
                gridBoxB.IsVisible = false;
                gridHolder = ConsumerRecords.gridConsumerBilledCollection;
                foreach (Grid _gridConsumer in gridMainB.Children)
                {
                    _gridConsumer.IsVisible = false;
                }
            }

            //load remaining grid before searching

            isSearching = true;
            //await LoadGridperBatch_new(ConsumerRecords.gridConsumerUnbilledCollection, 0);
            //await LoadGridperBatch_new(ConsumerRecords.gridConsumerBilledCollection, 1);



            switch (selectedFilterIndex)
            {
                case 0:
                    result = ConsumerRecords.lstconsumers.Where(c => c.meterno.Contains(searchValue, StringComparison.OrdinalIgnoreCase) && c.status == _gridStatus).ToList();
                    break;
                case 1:

                    result = ConsumerRecords.lstconsumers.Where(c => c.accountNo.Contains(searchValue, StringComparison.OrdinalIgnoreCase) && c.status == _gridStatus).ToList();
                    break;
                case 2:

                    result = ConsumerRecords.lstconsumers.Where(c => c.consumer_name.Contains(searchValue, StringComparison.OrdinalIgnoreCase) && c.status == _gridStatus).ToList();
                    break;
            }


            if (result.Count > 0)
            {
                OnErrorLocation = "Setting visibility on result";
                //Clear my existing list
                search_list_result = new List<string>();

                //Load the batch grid to parent grid first
                noofsearch = result.Count;
                await LoadGridperBatch_Search(gridHolder, _gridStatus, result);
                lblSummaryCount.TextColor = Colors.Red;
                if (pickerBillingFilter.SelectedIndex == 0)
                {

                    lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();

                }
                else if (pickerBillingFilter.SelectedIndex == 1)
                {
                    lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
                }
                else
                {
                    lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
                }
            }
            else
            {
                await DisplayAlertAsync("Notice", "No record(s) found for your criteria.", "Ok");
            }
            if (pickerBillingFilter.SelectedIndex == 0)
            {
                gridBoxU.IsVisible = true;
            }
            else if (pickerBillingFilter.SelectedIndex == 1)
            {
                gridBoxB.IsVisible = true;
            }

            //allGridLoaded = true;

            //activityIndicator.IsVisible = false;
            //activityIndicator.IsRunning = false;
            //lblLoadingData.IsVisible = false;

            stackLoading.IsVisible = false;

            //txtcount.Text = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            customError = ex.Message;
            await DisplayAlertAsync("Warning!", "Something weng wrong on your search.\n" + OnErrorLocation + "\n" + ex.Message, "Ok");
        }


    }
    private async void OnSearchFilter(object sender, EventArgs e)
    {
        if (searchFilterName.Text.Length < 3)
        {
            await DisplayAlertAsync("Notice", "Please enter at least 3 characters.", "Ok");
            return;
        }
        int selectedFilterIndex = pickerFiledFilter.SelectedIndex;
        string searchValue = searchFilterName.Text;
        List<Consumers> result = new List<Consumers>();

        //activityIndicator.IsVisible = true;
        //activityIndicator.IsRunning = true;
        //lblLoadingData.IsVisible = true;

        stackLoading.IsVisible = true;

        if (pickerBillingFilter.SelectedIndex == 0)
        {
            gridBoxU.IsVisible = false;
        }
        else if (pickerBillingFilter.SelectedIndex == 1)
        {
            gridBoxB.IsEnabled = false;
        }

        //load remaining grid before searching
        isSearching = true;
        //await LoadGridperBatch_new(ConsumerRecords.gridConsumerUnbilledCollection, 0);
        //await LoadGridperBatch_new(ConsumerRecords.gridConsumerBilledCollection, 1);

        switch (selectedFilterIndex)
        {
            case 0:
                result = ConsumerRecords.lstconsumers.Where(c => c.meterno.Contains(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();
                break;
            case 1:

                result = ConsumerRecords.lstconsumers.Where(c => c.accountNo.Contains(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();
                break;
            case 2:

                result = ConsumerRecords.lstconsumers.Where(c => c.consumer_name.Contains(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();
                break;
        }

        if (result != null)
        {
            //Make all child isvisible to false
            foreach (Grid _gridConsumer in gridBoxU)
            {
                foreach (var child in _gridConsumer.Children)
                {
                    if (child is AbsoluteLayout tmpAbsolutelayout)
                    {

                        tmpAbsolutelayout.IsVisible = false;

                    }
                    if (child is Frame tmpFrame)
                    {

                        tmpFrame.IsVisible = false;

                    }

                }
            }
            //int searchCount = 0;
            noofsearch = 0;
            foreach (var item in result)
            {
                long targetSeqNo = item.seqno;
                int gridchilds = gridBoxU.Children.Count;
                //Look for the grid and update
                foreach (Grid _gridConsumer in gridBoxU)
                {
                    foreach (var child in _gridConsumer.Children)
                    {
                        if (child is AbsoluteLayout tmpAbsolutelayout)
                        {
                            if (tmpAbsolutelayout.AutomationId == "AbsoluteLayout_" + targetSeqNo)
                            {
                                tmpAbsolutelayout.IsVisible = true;
                                noofsearch++;
                            }

                        }
                        if (child is Frame tmpFrame)
                        {
                            if (tmpFrame.AutomationId == "Lframe_" + targetSeqNo || tmpFrame.AutomationId == "Uframe_" + targetSeqNo)
                            {
                                tmpFrame.IsVisible = true;
                            }

                        }

                    }
                }
            }
            lblSummaryCount.TextColor = Colors.Red;
            if (pickerBillingFilter.SelectedIndex == 0)
            {

                lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();

            }
            else if (pickerBillingFilter.SelectedIndex == 1)
            {
                lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else
            {
                lblSummaryCount.Text = noofsearch.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }


        }
        else
        {
            await DisplayAlertAsync("Notice", "No record(s) found for your criteria.", "Ok");
        }
        if (pickerBillingFilter.SelectedIndex == 0)
        {
            gridBoxU.IsVisible = true;
        }
        else if (pickerBillingFilter.SelectedIndex == 1)
        {
            gridBoxB.IsEnabled = false;
        }

        //activityIndicator.IsVisible = false;
        //activityIndicator.IsRunning = false;
        //lblLoadingData.IsVisible = false;

        stackLoading.IsVisible = false;

    }

    private async void OnSearchClear(object sender, TextChangedEventArgs e)
    {
        if (!isSearching)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            //activityIndicator.IsVisible = true;
            //activityIndicator.IsRunning = true;
            //lblLoadingData.Text = "Resetting Loaded Customers.. Please wait...";
            //lblLoadingData.IsVisible = true;

            stackLoading.IsVisible = true;
            lblloading.Text = "Resetting Loaded Customers";


            _systimer = new System.Timers.Timer(1000);
            _systimer.Elapsed += OnTimedEventSearchClear;
            _systimer.Enabled = true;




        }

    }

    private void OnTimedEventSearchClear(Object source, ElapsedEventArgs e)
    {
        // Update the label text on the main thread
        _systimer.Enabled = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (pickerBillingFilter.SelectedIndex == 0)
            {
                gridBoxU.IsEnabled = false;
                foreach (Grid _gridConsumer in gridMainU.Children)
                {
                    _gridConsumer.IsVisible = true;

                }
            }
            else if (pickerBillingFilter.SelectedIndex == 1)
            {
                gridBoxB.IsEnabled = false;
                foreach (Grid _gridConsumer in gridMainB.Children)
                {
                    _gridConsumer.IsVisible = true;

                }
            }


            lblSummaryCount.TextColor = Colors.Black;
            if (pickerBillingFilter.SelectedIndex == 0)
            {
                gridBoxU.IsEnabled = true;
                lblSummaryCount.Text = (ConsumerRecords.noofrecords_import - ConsumerRecords.noofrecordsBilled).ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else if (pickerBillingFilter.SelectedIndex == 1)
            {
                gridBoxB.IsEnabled = true;
                lblSummaryCount.Text = ConsumerRecords.noofrecordsBilled.ToString() + " / " + ConsumerRecords.noofrecords_import.ToString();
            }
            else
            {
                lblSummaryCount.Text = "0 / " + ConsumerRecords.noofrecords_import.ToString();
            }
            isSearching = false;
            //activityIndicator.IsVisible = false;
            //activityIndicator.IsRunning = false;
            //lblLoadingData.IsVisible = false;

            stackLoading.IsVisible = false;

            isSearching = false;

        });
    }

    private void TapShowOPtions(object sender, TappedEventArgs e)
    {
        if (!showOptions)
        {
            gridFilter.IsVisible = true;
            gridSearch.IsVisible = true;

            //gridContainerU.HeightRequest = 220;
            //gridContainerB.HeightRequest = 220;

            showOptions = true;
            imgoption.Source = "menuicons/options.png";
        }
        else
        {
            gridFilter.IsVisible = false;
            gridSearch.IsVisible = false;

            //gridContainerU.HeightRequest = 350;
            //gridContainerB.HeightRequest = 350;
            showOptions = false;
            imgoption.Source = "menuicons/options1.png";
        }
    }
}