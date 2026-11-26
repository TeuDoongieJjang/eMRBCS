using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class ProductActivation : ContentPage
{
    public ProductActivation()
    {
        InitializeComponent();
        txtproduct_id.Text = LicenseService.Instance.DeviceId;
        txtproduct_id.IsReadOnly = true;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var st = LicenseService.Instance.GetStatus();
        bool activated = st.State == LicenseState.Activated;

        lblLicenseStatus.Text = st.State switch
        {
            LicenseState.Activated => "License: ACTIVATED",
            LicenseState.Demo => $"License: DEMO Version",
            _ => "License: EXPIRED"
        };

        txtactivation_key.IsEnabled = !activated;
        btnActivate.IsEnabled = !activated;
    }
    private async void TapCopyText(object sender, TappedEventArgs e)
    {
        string key = (txtproduct_id.Text ?? "").Trim();
        if (key.Length == 0) return;

        await Clipboard.Default.SetTextAsync(key);

        lblCopied.IsVisible = true;
        await Task.Delay(1500);
        lblCopied.IsVisible = false;
    }
    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopOnceAsync();
    }

    private async void OnSaveChanges(object sender, EventArgs e)
    {
        string key = (txtactivation_key.Text ?? "").Trim();
        if (key.Length == 0)
        {
            await DisplayAlertAsync("Notice", "Please enter the activation key.", "Ok");
            return;
        }

        bool ok = await Task.Run(() => LicenseService.Instance.TryActivate(key));
        if (ok)
        {
            await DisplayAlertAsync("Notice", "Product activated. Thank you!", "Ok");
            txtactivation_key.Text = "";
        }
        else
        {
            await DisplayAlertAsync("Notice", "Invalid activation key.", "Ok");
        }
        RefreshStatus();
    }
}