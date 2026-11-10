using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace eMRBCS
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Orientation, ScreenOrientation = ScreenOrientation.Portrait)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            eMRBCS.Platforms.Android.WifiNetworkBinder.Start();
        }
        public override bool OnKeyDown(Keycode keyCode, KeyEvent e)
        {
            // Send keypress event to the shared MAUI code
            MessagingCenter.Send<object, Keycode>(this, "KeyDown", keyCode);
            return base.OnKeyDown(keyCode, e);
        }

    }

    public class KeyDownMessage : ValueChangedMessage<string>
    {
        public KeyDownMessage(string value) : base(value)
        {
        }
    }

}
