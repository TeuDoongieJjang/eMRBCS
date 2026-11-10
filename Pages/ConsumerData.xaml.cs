using eMRBCS.Global;
using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class ConsumerData : ContentPage
{
    public ConsumerData()
    {
        InitializeComponent();

        consumer_acctID.Text = ConsumerRecords.acctNo;

    }

    private async void TapImgBack(object sender, EventArgs e)
    {
        await Navigation.PushOnceAsync(() => new ReadingConsumers());
    }
}