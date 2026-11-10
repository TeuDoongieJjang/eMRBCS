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
        BindingContext = _vm;
        ConsumerRecords.consumer_selectedtype = 0;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.SelectedFilterIndex = ConsumerRecords.consumer_selectedtype;   // NEW
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