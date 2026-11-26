namespace eMRBCS.Services;

/// <summary>Called before login. Demo/Activated = pass. Expired = ask for license key, otherwise exit the app.</summary>
public static class LicenseGate
{
    private static readonly SemaphoreSlim Sem = new(1, 1);

    public static async Task<bool> EnsureAsync(Page page)
    {
        await Sem.WaitAsync();
        try
        {
            var lic = LicenseService.Instance;
            await lic.InitializeAsync();

            var st = lic.GetStatus();
            if (st.State != LicenseState.Expired) return true;

            string reason = st.ClockRolledBack
                ? "The device date/time appears to have been changed. Correct the date/time, or enter a license key."
                : st.RecordTampered
                    ? "The license data is invalid. Please enter a license key."
                    : "The " + LicenseService.DemoMonths + "-month demo period has ended.";

            string prompt = $"{reason}\n\nProduct Key:\n{lic.DeviceId}\n\nEnter the license key to continue:";

            while (true)
            {
                string? key = await page.DisplayPromptAsync(
                    "License Required", prompt,
                    accept: "Activate", cancel: "Exit",
                    placeholder: "License key", maxLength: 32, keyboard: Keyboard.Text);

                if (key == null)            // Exit / back button
                {
                    ExitApp();
                    return false;
                }

                if (lic.TryActivate(key))
                {
                    await page.DisplayAlertAsync("License", "License activated. Thank you!", "Ok");
                    return true;
                }

                prompt = $"Invalid license key. Please try again.\n\nProduct Key:\n{lic.DeviceId}\n\nEnter the license key to continue:";
            }
        }
        finally { Sem.Release(); }
    }

    public static void ExitApp()
    {
#if ANDROID
        Platform.CurrentActivity?.FinishAffinity();
        global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
#else
        Application.Current?.Quit();
#endif
    }
}

/// <summary>
/// DEBUG ONLY. To remove: delete this class and the single line
///   await LicenseDebug.ShowAsync(this);
/// in Login.OnAppearing. (Or just set Enabled = false.)
/// </summary>
public static class LicenseDebug
{
    public static bool Enabled = true;

    public static async Task ShowAsync(Page page)
    {
        if (!Enabled) return;
        try
        {
            var lic = LicenseService.Instance;
            await lic.InitializeAsync();
            var st = lic.GetStatus();

            string text =
                $"Product Key (generated address):\n{lic.DeviceId}\n\n" +
                $"State: {st.State}\n" +
                $"Installed: {st.InstallUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                $"Demo ends: {st.ExpiresUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                $"Days left: {st.DaysLeft}\n" +
                $"Clock rolled back: {(st.ClockRolledBack ? "YES" : "No")}\n" +
                $"Record tampered: {(st.RecordTampered ? "YES" : "No")}\n" +
                $"Storage: {lic.StorageInfo}";

            await page.DisplayAlertAsync("LICENSE DEBUG", text, "Ok");
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("LICENSE DEBUG", "Error: " + ex.Message, "Ok");
        }
    }
}