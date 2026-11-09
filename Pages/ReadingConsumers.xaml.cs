using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.ViewModels;

namespace eMRBCS.Pages;

public partial class ReadingConsumers : ContentPage
{
    private readonly ReadingConsumersViewModel _vm = new();
    private bool _opening;

    public ReadingConsumers()
    {
        InitializeComponent();
        BindingContext = _vm;
    }

    protected override void OnAppearing() { base.OnAppearing(); _vm.Attach(); }
    protected override void OnDisappearing() { base.OnDisappearing(); _vm.Detach(); }

    private async void TapImgBack(object? sender, TappedEventArgs e) => await Navigation.PopAsync();

    private async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (_opening || (sender as BindableObject)?.BindingContext is not ConsumerRow row) return;
        _opening = true;
        try
        {
            ConsumerRecords.acctNo = row.AccountNo;
            ConsumerRecords.consumer_selectedtype = _vm.SelectedFilterIndex;
            await Navigation.PushAsync(new Reading());
        }
        finally { _opening = false; }
    }
}