using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using System.Text;

namespace eMRBCS
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if ANDROID
            builder.ConfigureLifecycleEvents(events =>
            {
                events.AddAndroid(android => android
                    .OnResume(_ =>
                    {
                        eMRBCS.Services.ApkInstaller.OnActivityResumed();
                        eMRBCS.Services.ServerStatusMonitor.Instance.RequestCheck();
                    }));
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return builder.Build();
        }
    }
}
