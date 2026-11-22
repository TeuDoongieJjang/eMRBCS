using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;
using eMRBCS.ViewModels;

namespace eMRBCS.Pages;

public partial class ReadingConsumers : ContentPage
{
    private readonly ReadingConsumersViewModel _vm = new();
    private bool _opening;

    public ReadingConsumers()
    {
        InitializeComponent();
        ApplyScreenScale();                      // NEW: responsive text + images
        BindingContext = _vm;
        ConsumerRecords.consumer_selectedtype = 0;
    }

    // NEW ------------------------------------------------------------------
    // Designed for 360 x 640 dp. Smaller screens shrink the text/icons a little,
    // bigger screens enlarge them (limited to 0.85x - 1.6x).
    private void ApplyScreenScale()
    {
        double w = MyDeviceInfo.device_width;    // dp
        double h = MyDeviceInfo.device_height;   // dp
        if (w <= 0 || h <= 0) return;

        double k = Math.Clamp(Math.Min(w / 360.0, h / 640.0), 0.85, 1.6);

        void Set(string key, double baseValue) => Resources[key] = Math.Round(baseValue * k, 1);

        Set("LcFsHeader", 18);
        Set("LcFsCap", 14);
        Set("LcFsVal", 18);
        Set("LcFsName", 16);
        Set("LcFsAddr", 14);
        Set("LcFsBody", 14);
        Set("LcFsStatus", 13);
        Set("LcFsSummary", 26);
        Set("LcIcon", 26);
        Set("LcHdrIcon", 50);
        Set("LcCardH", 86);
    }
    // ----------------------------------------------------------------------

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.SelectedFilterIndex = ConsumerRecords.consumer_selectedtype;
        _vm.Attach();
    }
    protected override void OnDisappearing() { base.OnDisappearing(); _vm.Detach(); }

    private async void TapImgBack(object? sender, TappedEventArgs e) => await Navigation.PopOnceAsync();

    private async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ConsumerRow row) return;
        ConsumerRecords.acctNo = row.AccountNo;
        ConsumerRecords.consumer_selectedtype = _vm.SelectedFilterIndex;
        await Navigation.PushOnceAsync(() => new Reading());
    }
}