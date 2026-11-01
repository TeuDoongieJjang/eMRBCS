using eMRBCS.Global;

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
        await Navigation.PushAsync(new ReadingConsumers());        
    }
}