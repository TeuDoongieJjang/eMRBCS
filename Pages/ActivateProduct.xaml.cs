using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;

namespace eMRBCS.Pages;

public partial class ActivateProduct : ContentPage
{
    string encryptedText;

    public ActivateProduct()
	{
		InitializeComponent();
        lblnotice.Text = "";
        activityIndicator.IsRunning = false;
        activityIndicator.IsVisible = false;

#if ANDROID

        txtproduct_id.Text = Android.Provider.Settings.Secure.GetString(Android.App.Application.Context.ContentResolver, Android.Provider.Settings.Secure.AndroidId).ToUpper();

#endif
    }



    private async void TapCopyText(object sender, TappedEventArgs e)
    {
        await Clipboard.SetTextAsync(lblActivationKey.Text);
        lblnotice.Text = "Activation Key Copied to Clipboard";
        await Task.Delay(1000);
        lblnotice.Text = "";

    }

    private async void OnGenerateKey(object sender, EventArgs e)
    {
        activityIndicator.IsRunning = true;
        activityIndicator.IsVisible = true;
        encryptedText = await EncryptString(txtproduct_id.Text.ToUpper(), "manny");
        string alphanumericString = Regex.Replace(encryptedText, "[^a-zA-Z0-9]", "");
        string comparetxt = alphanumericString.Length > 8 ? alphanumericString.Substring(0, 8) : alphanumericString;
        lblActivationKey.Text = comparetxt.ToUpper();
        activityIndicator.IsRunning = false;
        activityIndicator.IsVisible = false;
    }

    public async Task<string> EncryptString(string plainText, string passphrase)
    {

        // Generate a key and IV based on the passphrase
        string encryptvalue = "";
        await Task.Run(() =>
        {
            using (var aes = Aes.Create())
            {
                var keyDerivationFunction = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(passphrase), 10000);
                aes.Key = keyDerivationFunction.GetBytes(32); // 256-bit key
                aes.IV = keyDerivationFunction.GetBytes(16);  // 128-bit IV

                // Encrypt the data
                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(plainText);
                    }

                    var encryptedBytes = ms.ToArray();
                    encryptvalue= Convert.ToBase64String(encryptedBytes);
                }
            }

        });
        return encryptvalue;
    }
}