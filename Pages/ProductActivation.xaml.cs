
using eMRBCS.Global;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace eMRBCS.Pages;

public partial class ProductActivation : ContentPage
{
    string encryptedText;

    public ProductActivation()
    {
        InitializeComponent();
        txtproduct_id.Text = MyDeviceInfo.device_AndroidID.ToUpper();

    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnSaveChanges(object sender, EventArgs e)
    {
        string filePath = Path.Combine(FileSystem.AppDataDirectory, MyDeviceInfo.activation_file);
        if (!File.Exists(filePath))
        {

            encryptedText = EncryptString(txtproduct_id.Text, "manny");
            string alphanumericString = Regex.Replace(encryptedText, "[^a-zA-Z0-9]", "");
            string comparetxt = alphanumericString.Length > 8 ? alphanumericString.Substring(0, 8) : alphanumericString;
            //MgN6h2pZ
            if (txtactivation_key.Text.ToUpper() == comparetxt.ToUpper())
            {

                File.WriteAllText(filePath, encryptedText);
            }

        }
    }

    public static string EncryptString(string plainText, string passphrase)
    {


        using (var aes = Aes.Create())
        {
            var keyDerivationFunction = new Rfc2898DeriveBytes(passphrase, Encoding.UTF8.GetBytes(passphrase), 10000);
            aes.Key = keyDerivationFunction.GetBytes(32); // 256-bit key
            aes.IV = keyDerivationFunction.GetBytes(16);  // 128-bit IV


            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }

                var encryptedBytes = ms.ToArray();
                return Convert.ToBase64String(encryptedBytes);
            }
        }
    }


}